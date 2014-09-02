using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text;
using System.IO;
using CannedBytes.Media.IO;
using CannedBytes.Midi;
using CannedBytes.Midi.IO;
using CannedBytes.Midi.Message;

namespace MidiSplit
{
    public class MidiSplit
    {
        public static int Main(string[] args)
        {
            try
            {
                string midiInFilePath = null;
                string midiOutFilePath = null;

                // parse argument
                if (args.Length == 0)
                {
                    Console.WriteLine("Usage: MidiSplit input.mid output.mid");
                    return 1;
                }
                else if (args.Length == 1)
                {
                    midiInFilePath = args[0];
                    midiOutFilePath = Path.Combine(
                        Path.GetDirectoryName(midiInFilePath),
                        Path.GetFileNameWithoutExtension(midiInFilePath) + "-split.mid"
                    );
                }
                else if (args.Length == 2)
                {
                    midiInFilePath = args[0];
                    midiOutFilePath = args[1];
                }
                else
                {
                    Console.WriteLine("too many arguments");
                    return 1;
                }

                // for debug...
                Console.WriteLine("Reading midi file: " + midiInFilePath);
                Console.WriteLine("Output midi file: " + midiOutFilePath);

                MidiFileData midiInData = MidiSplit.ReadMidiFile(midiInFilePath);
                MidiFileData midiOutData = MidiSplit.SplitMidiFile(midiInData);
                using (MidiFileSerializer midiFileSerializer = new MidiFileSerializer(midiOutFilePath))
                {
                    midiFileSerializer.Serialize(midiOutData);
                }
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                return 1;
            }
        }

        static MidiFileData SplitMidiFile(MidiFileData midiInData)
        {
            MidiFileData midiOutData = new MidiFileData();
            midiOutData.Header = new MThdChunk();
            midiOutData.Header.Format = (ushort)MidiFileFormat.MultipleTracks;
            midiOutData.Header.TimeDivision = midiInData.Header.TimeDivision;

            IList<MTrkChunk> tracks = new List<MTrkChunk>();
            foreach (MTrkChunk midiTrackIn in midiInData.Tracks)
            {
                foreach (MTrkChunk midiTrackOut in MidiSplit.SplitMidiTrack(midiTrackIn))
                {
                    tracks.Add(midiTrackOut);
                }
            }
            midiOutData.Tracks = tracks;
            midiOutData.Header.NumberOfTracks = (ushort)tracks.Count;
            return midiOutData;
        }

        protected struct MTrkChannelParam
        {
            public int MidiChannel;
            public int ProgramNumber;
            public int BankNumber;
        }

        protected class MTrkChunkWithInstrInfo
        {
            public MTrkChannelParam Channel;
            public MTrkChunk Track;
            public int SortIndex;
        }

        static IEnumerable<MTrkChunk> SplitMidiTrack(MTrkChunk midiTrackIn)
        {
            const int MaxChannels = 16;
            const int MaxNotes = 128;

            int midiEventIndex;
            long absoluteEndTime = 0;
            IDictionary<int, MTrkChannelParam> midiEventMapTo = new Dictionary<int, MTrkChannelParam>();

            // initialize channel variables
            int[] newBankNumber = new int[MaxChannels];
            int[] currentBankNumber = new int[MaxChannels];
            int[] currentProgramNumber = new int[MaxChannels];
            int[] boundaryEventIndex = new int[MaxChannels];
            int[] firstChannelEventIndex = new int[MaxChannels];
            bool[] firstProgramChange = new bool[MaxChannels];
            int[,] midiNoteOn = new int[MaxChannels, MaxNotes];
            for (int channel = 0; channel < MaxChannels; channel++)
            {
                newBankNumber[channel] = 0;
                currentBankNumber[channel] = 0;
                currentProgramNumber[channel] = 0;
                boundaryEventIndex[channel] = 0;
                firstChannelEventIndex[channel] = -1;
                firstProgramChange[channel] = true;
                for (int note = 0; note < MaxNotes; note++)
                {
                    midiNoteOn[channel, note] = 0;
                }
            }

            // pre-scan input events
            midiEventIndex = 0;
            foreach (MidiFileEvent midiEvent in midiTrackIn.Events)
            {
                // update the end time of the track
                absoluteEndTime = midiEvent.AbsoluteTime;

                // dispatch message
                if (midiEvent.Message is MidiChannelMessage)
                {
                    MidiChannelMessage channelMessage = midiEvent.Message as MidiChannelMessage;

                    // remember the first channel messeage index
                    if (firstChannelEventIndex[channelMessage.MidiChannel] == -1)
                    {
                        firstChannelEventIndex[channelMessage.MidiChannel] = midiEventIndex;

                        // determine the output track temporalily,
                        // for tracks that do not have any program changes
                        MTrkChannelParam channelParam = new MTrkChannelParam();
                        channelParam.MidiChannel = channelMessage.MidiChannel;
                        channelParam.BankNumber = 0;
                        channelParam.ProgramNumber = 0;
                        midiEventMapTo[midiEventIndex] = channelParam;
                    }

                    // dispatch channel message
                    if (channelMessage.Command == MidiChannelCommand.NoteOff ||
                        (channelMessage.Command == MidiChannelCommand.NoteOn && channelMessage.Parameter2 == 0))
                    {
                        // note off
                        byte noteNumber = channelMessage.Parameter1;
                        if (midiNoteOn[channelMessage.MidiChannel, noteNumber] > 0)
                        {
                            // deactivate existing note
                            midiNoteOn[channelMessage.MidiChannel, noteNumber]--;

                            // check if all notes are off
                            bool allNotesOff = true;
                            for (int note = 0; note < MaxNotes; note++)
                            {
                                if (midiNoteOn[channelMessage.MidiChannel, note] != 0)
                                {
                                    allNotesOff = false;
                                    break;
                                }
                            }

                            // save the trigger timing
                            if (allNotesOff)
                            {
                                boundaryEventIndex[channelMessage.MidiChannel] = midiEventIndex + 1;
                            }
                        }
                    }
                    else if (channelMessage.Command == MidiChannelCommand.NoteOn)
                    {
                        // note on: activate note
                        byte noteNumber = channelMessage.Parameter1;
                        midiNoteOn[channelMessage.MidiChannel, noteNumber]++;
                        boundaryEventIndex[channelMessage.MidiChannel] = midiEventIndex + 1;
                    }
                    else if (channelMessage.Command == MidiChannelCommand.ProgramChange)
                    {
                        // program change
                        byte programNumber = channelMessage.Parameter1;
                        currentBankNumber[channelMessage.MidiChannel] = newBankNumber[channelMessage.MidiChannel];
                        currentProgramNumber[channelMessage.MidiChannel] = programNumber;

                        // determine the output track
                        MTrkChannelParam channelParam = new MTrkChannelParam();
                        channelParam.MidiChannel = channelMessage.MidiChannel;
                        channelParam.BankNumber = currentBankNumber[channelMessage.MidiChannel];
                        channelParam.ProgramNumber = currentProgramNumber[channelMessage.MidiChannel];

                        // switch the track from the last silence
                        if (firstProgramChange[channelMessage.MidiChannel])
                        {
                            midiEventMapTo[firstChannelEventIndex[channelMessage.MidiChannel]] = channelParam;
                            firstProgramChange[channelMessage.MidiChannel] = false;
                        }
                        else
                        {
                            midiEventMapTo[boundaryEventIndex[channelMessage.MidiChannel]] = channelParam;
                        }

                        // update the trigger timing
                        boundaryEventIndex[channelMessage.MidiChannel] = midiEventIndex + 1;
                    }
                    else if (channelMessage.Command == MidiChannelCommand.ControlChange)
                    {
                        MidiControllerMessage controllerMessage = midiEvent.Message as MidiControllerMessage;

                        // dispatch bank select
                        if (controllerMessage.ControllerType == MidiControllerType.BankSelect)
                        {
                            newBankNumber[channelMessage.MidiChannel] &= ~(127 << 7);
                            newBankNumber[channelMessage.MidiChannel] |= controllerMessage.Value << 7;
                        }
                        else if (controllerMessage.ControllerType == MidiControllerType.BankSelectFine)
                        {
                            newBankNumber[channelMessage.MidiChannel] &= ~127;
                            newBankNumber[channelMessage.MidiChannel] |= controllerMessage.Value;
                        }
                    }
                }

                midiEventIndex++;
            }

            // create output tracks
            IDictionary<MTrkChannelParam, MTrkChunk> trackAssociatedWith = new Dictionary<MTrkChannelParam, MTrkChunk>();
            List<MTrkChunkWithInstrInfo> trackInfos = new List<MTrkChunkWithInstrInfo>();
            if (midiEventMapTo.Count > 0)
            {
                foreach (KeyValuePair<int, MTrkChannelParam> aMidiEventMapTo in midiEventMapTo)
                {
                    //Console.WriteLine(String.Format("Event: {0}, Channel: {1}, Bank: {2}, Program: {3}",
                    //    aMidiEventMapTo.Key, aMidiEventMapTo.Value.MidiChannel, aMidiEventMapTo.Value.BankNumber, aMidiEventMapTo.Value.ProgramNumber));
                    if (!trackAssociatedWith.ContainsKey(aMidiEventMapTo.Value))
                    {
                        MTrkChunkWithInstrInfo trackInfo = new MTrkChunkWithInstrInfo();
                        trackInfo.Track = new MTrkChunk();
                        trackInfo.Track.Events = new List<MidiFileEvent>();
                        trackInfo.Channel = aMidiEventMapTo.Value;
                        trackInfo.SortIndex = trackInfos.Count;
                        trackInfos.Add(trackInfo);
                        trackAssociatedWith[aMidiEventMapTo.Value] = trackInfo.Track;
                    }
                }

                // sort by channel number
                trackInfos.Sort((a, b) => {
                    if (a.Channel.MidiChannel != b.Channel.MidiChannel)
                    {
                        return a.Channel.MidiChannel - b.Channel.MidiChannel;
                    }
                    else
                    {
                        return a.SortIndex - b.SortIndex;
                    }
                });
            }
            else
            {
                // special case: track does not have any channel messages
                MTrkChunkWithInstrInfo trackInfo = new MTrkChunkWithInstrInfo();
                trackInfo.Track = new MTrkChunk();
                trackInfo.Track.Events = new List<MidiFileEvent>();
                trackInfos.Add(trackInfo);
            }

            // start copying midi events
            midiEventIndex = 0;
            IDictionary<int, MTrkChunk> currentOutputTrack = new Dictionary<int, MTrkChunk>();
            Queue<MTrkChunk>[,] notesAssociatedWithTrack = new Queue<MTrkChunk>[MaxChannels, MaxNotes];
            foreach (MidiFileEvent midiEvent in midiTrackIn.Events)
            {
                MTrkChunk targetTrack = null;

                // dispatch message
                if (midiEvent.Message is MidiChannelMessage)
                {
                    MidiChannelMessage channelMessage = midiEvent.Message as MidiChannelMessage;

                    // switch output track if necessary
                    if (midiEventMapTo.ContainsKey(midiEventIndex))
                    {
                        MTrkChannelParam aMidiEventMapTo = midiEventMapTo[midiEventIndex];
                        currentOutputTrack[aMidiEventMapTo.MidiChannel] = trackAssociatedWith[aMidiEventMapTo];
                    }

                    // determine output track
                    targetTrack = currentOutputTrack[channelMessage.MidiChannel];

                    // dispatch note on/off
                    if (channelMessage.Command == MidiChannelCommand.NoteOff ||
                        (channelMessage.Command == MidiChannelCommand.NoteOn && channelMessage.Parameter2 == 0))
                    {
                        // note off
                        byte noteNumber = channelMessage.Parameter1;
                        if (notesAssociatedWithTrack[channelMessage.MidiChannel, noteNumber] != null &&
                            notesAssociatedWithTrack[channelMessage.MidiChannel, noteNumber].Count != 0)
                        {
                            targetTrack = notesAssociatedWithTrack[channelMessage.MidiChannel, noteNumber].Dequeue();
                        }
                    }
                    else if (channelMessage.Command == MidiChannelCommand.NoteOn)
                    {
                        // note on
                        byte noteNumber = channelMessage.Parameter1;
                        if (notesAssociatedWithTrack[channelMessage.MidiChannel, noteNumber] == null)
                        {
                            // allocate a queue if not available
                            notesAssociatedWithTrack[channelMessage.MidiChannel, noteNumber] = new Queue<MTrkChunk>();
                        }
                        // remember the output track
                        notesAssociatedWithTrack[channelMessage.MidiChannel, noteNumber].Enqueue(targetTrack);
                    }
                }
                else
                {
                    targetTrack = trackInfos[0].Track;
                }

                // add event to the list, if it's not end of track
                if (!(midiEvent.Message is MidiMetaMessage) ||
                    (midiEvent.Message as MidiMetaMessage).MetaType != MidiMetaType.EndOfTrack)
                {
                    IList<MidiFileEvent> targetEventList = targetTrack.Events as IList<MidiFileEvent>;
                    targetEventList.Add(midiEvent);
                }

                midiEventIndex++;
            }

            // construct the plain track list
            IList<MTrkChunk> tracks = new List<MTrkChunk>();
            foreach (MTrkChunkWithInstrInfo trackInfo in trackInfos)
            {
                tracks.Add(trackInfo.Track);
            }

            // fix some conversion problems
            foreach (MTrkChunk track in tracks)
            { 
                // fixup delta time artifically...
                MidiFileEvent midiLastEvent = null;
                foreach (MidiFileEvent midiEvent in track.Events)
                {
                    midiEvent.DeltaTime = midiEvent.AbsoluteTime - (midiLastEvent != null ? midiLastEvent.AbsoluteTime : 0);
                    midiLastEvent = midiEvent;
                }

                // add end of track manually
                MidiFileEvent endOfTrack = new MidiFileEvent();
                endOfTrack.AbsoluteTime = absoluteEndTime;
                endOfTrack.DeltaTime = absoluteEndTime - midiLastEvent.AbsoluteTime;
                endOfTrack.Message = new MidiMetaMessage(MidiMetaType.EndOfTrack, new byte[] { });
                (track.Events as IList<MidiFileEvent>).Add(endOfTrack);
            }

            return tracks;
        }

        static MidiFileData ReadMidiFile(string filePath)
        {
            MidiFileData data = new MidiFileData();
            FileChunkReader reader = FileReaderFactory.CreateReader(filePath);

            data.Header = reader.ReadNextChunk() as MThdChunk;

            List<MTrkChunk> tracks = new List<MTrkChunk>();

            for (int i = 0; i < data.Header.NumberOfTracks; i++)
            {
                try
                {
                    var track = reader.ReadNextChunk() as MTrkChunk;

                    if (track != null)
                    {
                        tracks.Add(track);
                    }
                    else
                    {
                        Console.WriteLine(String.Format("Track '{0}' was not read successfully.", i + 1));
                    }
                }
                catch (Exception e)
                {
                    reader.SkipCurrentChunk();

                    ConsoleColor prevConsoleColor = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Failed to read track: " + (i + 1));
                    Console.WriteLine(e);
                    Console.ForegroundColor = prevConsoleColor;
                }
            }

            data.Tracks = tracks;
            return data;
        }
    }
}
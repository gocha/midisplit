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

        protected class MTrkChunkWithInstrInfo
        {
            public int? MidiChannel;
            public int? ProgramNumber;
            public MTrkChunk Track;
        }

        static IEnumerable<MTrkChunk> SplitMidiTrack(MTrkChunk midiTrackIn)
        {
            const int MaxChannels = 16;
            const int MaxNotes = 128;
            int?[] currentProgramNumber = new int?[MaxChannels];
            IList<MTrkChunkWithInstrInfo> trackInfos = new List<MTrkChunkWithInstrInfo>();
            
            // create default output track
            trackInfos.Add(new MTrkChunkWithInstrInfo());
            trackInfos[0].Track = new MTrkChunk();
            trackInfos[0].Track.Events = new List<MidiFileEvent>();

            // dispatch events from beginning
            // (events must be sorted by absolute time)
            MidiFileEvent midiLastEvent = null;
            MTrkChunk[,] tracksWithMissingNoteOff = new MTrkChunk[MaxChannels, MaxNotes];
            foreach (MidiFileEvent midiEvent in midiTrackIn.Events)
            {
                MTrkChunk targetTrack = null;

                // save the last event to verify end of track
                midiLastEvent = midiEvent;

                if (midiEvent.Message is MidiChannelMessage)
                {
                    MidiChannelMessage channelMessage = midiEvent.Message as MidiChannelMessage;

                    // if this is the first channel message
                    if (trackInfos[0].MidiChannel == null)
                    {
                        // set the channel number to the first track
                        trackInfos[0].MidiChannel = channelMessage.MidiChannel;
                    }

                    // update current patch #
                    if (channelMessage.Command == MidiChannelCommand.ProgramChange)
                    {
                        currentProgramNumber[channelMessage.MidiChannel] = channelMessage.Parameter1;
                    }

                    // the location to put expected note-off event needs to be determined by note-on location
                    bool midiEventIsNoteOff = (channelMessage.Command == MidiChannelCommand.NoteOff ||
                        (channelMessage.Command == MidiChannelCommand.NoteOn && channelMessage.Parameter2 == 0));
                    if (midiEventIsNoteOff && tracksWithMissingNoteOff[channelMessage.MidiChannel, channelMessage.Parameter1] != null)
                    {
                        targetTrack = tracksWithMissingNoteOff[channelMessage.MidiChannel, channelMessage.Parameter1];
                        tracksWithMissingNoteOff[channelMessage.MidiChannel, channelMessage.Parameter1] = null;
                    }
                    else
                    {
                        // search target track
                        int trackIndex;
                        for (trackIndex = 0; trackIndex < trackInfos.Count; trackIndex++)
                        {
                            MTrkChunkWithInstrInfo trackInfo = trackInfos[trackIndex];

                            if (trackInfo.MidiChannel == channelMessage.MidiChannel &&
                                (trackInfo.ProgramNumber == null ||
                                  trackInfo.ProgramNumber == currentProgramNumber[channelMessage.MidiChannel]))
                            {
                                // set program number (we need to set it for the first time)
                                trackInfo.ProgramNumber = currentProgramNumber[channelMessage.MidiChannel];

                                // target track is determined, exit the loop
                                targetTrack = trackInfo.Track;
                                break;
                            }
                            else if (trackInfo.MidiChannel > channelMessage.MidiChannel)
                            {
                                // track list is sorted by channel number
                                // therefore, the rest isn't what we are searching for
                                // a new track needs to be assigned to the current index
                                break;
                            }
                        }

                        // add a new track if necessary
                        if (targetTrack == null)
                        {
                            MTrkChunkWithInstrInfo newTrackInfo = new MTrkChunkWithInstrInfo();
                            newTrackInfo.Track = new MTrkChunk();
                            newTrackInfo.Track.Events = new List<MidiFileEvent>();
                            newTrackInfo.MidiChannel = channelMessage.MidiChannel;
                            newTrackInfo.ProgramNumber = currentProgramNumber[channelMessage.MidiChannel];
                            trackInfos.Insert(trackIndex, newTrackInfo);
                            targetTrack = newTrackInfo.Track;
                        }

                        // remember new note, to know appropriate note-off location
                        if (channelMessage.Command == MidiChannelCommand.NoteOn && channelMessage.Parameter2 != 0)
                        {
                            tracksWithMissingNoteOff[channelMessage.MidiChannel, channelMessage.Parameter1] = targetTrack;
                        }
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
            }

            // determine the location of end of track
            long absoluteTimeOfEndOfTrack = 0;
            if (midiLastEvent != null)
            {
                absoluteTimeOfEndOfTrack = midiLastEvent.AbsoluteTime;
            }

            // construct the track list without extra info
            IList<MTrkChunk> tracks = new List<MTrkChunk>();
            foreach (MTrkChunkWithInstrInfo trackInfo in trackInfos)
            {
                tracks.Add(trackInfo.Track);
            }

            // fix some conversion problems
            foreach (MTrkChunk track in tracks)
            { 
                // fixup delta time artifically...
                midiLastEvent = null;
                foreach (MidiFileEvent midiEvent in track.Events)
                {
                    midiEvent.DeltaTime = midiEvent.AbsoluteTime - (midiLastEvent != null ? midiLastEvent.AbsoluteTime : 0);
                    midiLastEvent = midiEvent;
                }

                // add end of track manually
                MidiFileEvent endOfTrack = new MidiFileEvent();
                endOfTrack.AbsoluteTime = absoluteTimeOfEndOfTrack;
                endOfTrack.DeltaTime = absoluteTimeOfEndOfTrack - midiLastEvent.AbsoluteTime;
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
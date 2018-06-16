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
using System.Text.RegularExpressions;

namespace MidiSplit
{
    public class MidiSplit
    {
        public const string NAME = "MidiSplit";
        public const string VERSION = "1.2.5";
        public const string AUTHOR = "gocha";
        public const string URL = "http://github.com/gocha/midisplit";

        public static int Main(string[] args)
        {
            try
            {
                string midiInFilePath = null;
                string midiOutFilePath = null;
                bool copySeparatedControllers = false;
                IList<int> percMidiChannels = new List<int>();
                IList<int> percProgChanges = new List<int>();

                // parse options
                int argIndex = 0;
                while (argIndex < args.Length && args[argIndex].StartsWith("-"))
                {
                    string option = args[argIndex++];
                    if (option == "--help")
                    {
                        ShowUsage();
                        return 1;
                    }
                    else if (option == "-cs" || option == "--copy-separated")
                    {
                        copySeparatedControllers = true;
                    }
                    else if (option == "-sp" || option == "--split-note")
                    {
                        if (argIndex + 1 >= args.Length)
                        {
                            Console.WriteLine("Too few arguments for " + option);
                            return 1;
                        }

                        string[] targets = args[argIndex].Split(',');
                        foreach (var target_ in targets)
                        {
                            string target = target_.Trim();
                            if (target.StartsWith("ch"))
                            {
                                int midiChannel;
                                if (!int.TryParse(target.Substring(2), out midiChannel))
                                {
                                    Console.WriteLine("Invalid channel number format for " + option);
                                    return 1;
                                }

                                if (midiChannel < 1 || midiChannel > 16)
                                {
                                    Console.WriteLine("Invalid channel number for " + option + " (valid range is 1..16)");
                                    return 1;
                                }
                                midiChannel--;

                                if (!percMidiChannels.Contains(midiChannel))
                                {
                                    percMidiChannels.Add(midiChannel);
                                }
                            }
                            else if (target.StartsWith("prg"))
                            {
                                string param = target.Trim().Substring(3);
                                int progChange;
                                int bankMSB = 0;
                                int bankLSB = 0;

                                try
                                {
                                    if (Regex.IsMatch(param, "\\d+:\\d+:\\d+", RegexOptions.ECMAScript))
                                    {
                                        string[] tokens = param.Split(':');
                                        bankMSB = int.Parse(tokens[0]);
                                        bankLSB = int.Parse(tokens[1]);
                                        progChange = int.Parse(tokens[2]);
                                    }
                                    else
                                    {
                                        progChange = int.Parse(param);
                                    }
                                }
                                catch (FormatException)
                                {
                                    Console.WriteLine("Invalid program number format for " + option);
                                    return 1;
                                }

                                if (progChange < 1 || progChange > 128)
                                {
                                    Console.WriteLine("Invalid program number for " + option + " (valid range is 1..128)");
                                    return 1;
                                }
                                if (bankMSB < 0 || bankMSB > 127)
                                {
                                    Console.WriteLine("Invalid bank MSB for " + option + " (valid range is 0..127)");
                                    return 1;
                                }
                                if (bankLSB < 0 || bankLSB > 127)
                                {
                                    Console.WriteLine("Invalid bank LSB for " + option + " (valid range is 0..127)");
                                    return 1;
                                }
                                progChange--;

                                int progChangeWithBank = progChange + (bankLSB << 7) + (bankMSB << 14);
                                if (!percProgChanges.Contains(progChangeWithBank))
                                {
                                    percProgChanges.Add(progChangeWithBank);
                                }
                            }
                            else
                            {
                                Console.WriteLine("Invalid parameter \"" + target + "\" for option " + option);
                                return 1;
                            }
                        }
                        argIndex++;
                    }
                    else
                    {
                        Console.WriteLine("Unknown option " + option);
                        return 1;
                    }
                }

                // parse argument
                int mainArgCount = args.Length - argIndex;
                if (mainArgCount == 0)
                {
                    ShowUsage();
                    return 1;
                }
                else if (mainArgCount == 1)
                {
                    midiInFilePath = args[argIndex];
                    midiOutFilePath = Path.Combine(
                        Path.GetDirectoryName(midiInFilePath),
                        Path.GetFileNameWithoutExtension(midiInFilePath) + "-split.mid"
                    );
                }
                else if (mainArgCount == 2)
                {
                    midiInFilePath = args[argIndex];
                    midiOutFilePath = args[argIndex + 1];
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
                MidiFileData midiOutData = MidiSplit.SplitMidiFile(midiInData, copySeparatedControllers, percMidiChannels, percProgChanges);
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

        public static void ShowUsage()
        {
            Console.WriteLine("# " + NAME);
            Console.WriteLine();
            Console.WriteLine(NAME + " version " + VERSION + " by " + AUTHOR + " <" + URL + ">");
            Console.WriteLine();
            Console.WriteLine("Usage: MidiSplit (options) input.mid output.mid");
            Console.WriteLine();
            Console.WriteLine("### Options");
            Console.WriteLine();
            Console.WriteLine("- `-cs` `--copy-separated`: Copy controller events that are updated by other tracks.");
            Console.WriteLine("- `-sp targets` `--split-note targets`: Split given channel/instrument into each note numbers. Example: `-p \"ch10, prg127, prg127:0:1\"`");
        }

        static MidiFileData SplitMidiFile(MidiFileData midiInData, bool copySeparatedControllers, IList<int> percMidiChannels, IList<int> percProgChanges)
        {
            if (percMidiChannels == null)
            {
                percMidiChannels = new List<int>();
            }

            if (percProgChanges == null)
            {
                percProgChanges = new List<int>();
            }

            MidiFileData midiOutData = new MidiFileData();
            midiOutData.Header = new MThdChunk();
            midiOutData.Header.Format = (ushort)MidiFileFormat.MultipleTracks;
            midiOutData.Header.TimeDivision = midiInData.Header.TimeDivision;

            IList<MTrkChunk> tracks = new List<MTrkChunk>();
            foreach (MTrkChunk midiTrackIn in midiInData.Tracks)
            {
                foreach (MTrkChunk midiTrackOut in MidiSplit.SplitMidiTrack(midiTrackIn, copySeparatedControllers, percMidiChannels, percProgChanges))
                {
                    tracks.Add(midiTrackOut);
                }
            }
            midiOutData.Tracks = tracks;
            midiOutData.Header.NumberOfTracks = (ushort)tracks.Count;
            return midiOutData;
        }

        // keys for switcing tracks
        protected struct MTrkChannelParam
        {
            public int MidiChannel;
            public int ProgramNumber;
            public int BankNumber;
            public int NoteNumber;

            public override string ToString()
            {
                return "(" + this.GetType().Name + ")[MidiChannel: " + MidiChannel + ", ProgramNumber: " + ProgramNumber + ", BankNumber: " + BankNumber + ", NoteNumber: " + NoteNumber + "]";
            }
        }

        protected class MTrkChunkWithInfo
        {
            public MTrkChannelParam Channel;
            public MTrkChunk Track;
            public int SortIndex; // a number used for stable sort
            public MidiChannelStatus Status;
        }

        static IEnumerable<MTrkChunk> SplitMidiTrack(MTrkChunk midiTrackIn, bool copySeparatedControllers, IList<int> percMidiChannels, IList<int> percProgChanges)
        {
            bool verbose = false;

            if (percMidiChannels == null)
            {
                percMidiChannels = new List<int>();
            }

            if (percProgChanges == null)
            {
                percProgChanges = new List<int>();
            }

            const int MaxChannels = 16;
            const int MaxNotes = 128;

            //int midiEventIndex;
            long absoluteEndTime = 0;
            IDictionary<int, MTrkChannelParam> midiEventMapTo = new Dictionary<int, MTrkChannelParam>();

            // initialize channel variables
            int[] newBankNumber = new int[MaxChannels];
            int[] currentBankNumber = new int[MaxChannels];
            int[] currentProgramNumber = new int[MaxChannels];
            int[] lastNoteNumber = new int[MaxChannels];
            int[] firstChannelEventIndex = new int[MaxChannels];
            int[] boundaryEventIndex = new int[MaxChannels];
            int[] lastTrackMapIndex = new int[MaxChannels];
            int[,] midiNoteOn = new int[MaxChannels, MaxNotes];
            for (int channel = 0; channel < MaxChannels; channel++)
            {
                newBankNumber[channel] = 0;
                currentBankNumber[channel] = 0;
                currentProgramNumber[channel] = -1;
                firstChannelEventIndex[channel] = -1;
                lastNoteNumber[channel] = -1;
                boundaryEventIndex[channel] = 0;
                lastTrackMapIndex[channel] = -1;
                for (int note = 0; note < MaxNotes; note++)
                {
                    midiNoteOn[channel, note] = 0;
                }
            }

            // copy midi events to a list
            IList<MidiFileEvent> midiEventListIn = new List<MidiFileEvent>(midiTrackIn.Events);

            // pre-scan input events
            for (int midiEventIndex = 0; midiEventIndex < midiEventListIn.Count; midiEventIndex++)
            {
                MidiFileEvent midiEvent = midiEventListIn[midiEventIndex];

                // update the end time of the track
                absoluteEndTime = midiEvent.AbsoluteTime;

                // dispatch message
                const int DEFAULT_PROGNUMBER = 0;
                if (midiEvent.Message is MidiChannelMessage)
                {
                    MidiChannelMessage channelMessage = midiEvent.Message as MidiChannelMessage;
                    byte midiChannel = channelMessage.MidiChannel;
                    int currentProgNumber = (currentProgramNumber[midiChannel] != -1) ? currentProgramNumber[midiChannel] : DEFAULT_PROGNUMBER;
                    bool percussion = percMidiChannels.Contains(midiChannel) ||
                        percProgChanges.Contains(currentProgNumber | (currentBankNumber[midiChannel] << 7));

                    // remember the first channel messeage index
                    if (firstChannelEventIndex[midiChannel] == -1)
                    {
                        firstChannelEventIndex[midiChannel] = midiEventIndex;

                        // determine the output track temporalily,
                        // for tracks that do not have any program changes
                        MTrkChannelParam channelParam = new MTrkChannelParam();
                        channelParam.MidiChannel = midiChannel;
                        channelParam.BankNumber = 0;
                        channelParam.ProgramNumber = DEFAULT_PROGNUMBER;
                        channelParam.NoteNumber = -1;
                        midiEventMapTo[midiEventIndex] = channelParam;
                    }

                    // dispatch channel message
                    if (channelMessage.Command == MidiChannelCommand.NoteOff ||
                        (channelMessage.Command == MidiChannelCommand.NoteOn && channelMessage.Parameter2 == 0))
                    {
                        // note off
                        byte noteNumber = channelMessage.Parameter1;
                        if (midiNoteOn[midiChannel, noteNumber] > 0)
                        {
                            // deactivate existing note
                            midiNoteOn[midiChannel, noteNumber]--;

                            // check if all notes are off
                            bool allNotesOff = true;
                            for (int note = 0; note < MaxNotes; note++)
                            {
                                if (midiNoteOn[midiChannel, note] != 0)
                                {
                                    allNotesOff = false;
                                    break;
                                }
                            }

                            // save the trigger timing
                            if (allNotesOff)
                            {
                                // find the next channel message of the same channel
                                boundaryEventIndex[midiChannel] = midiEventIndex + 1 + midiEventListIn.Skip(midiEventIndex + 1).TakeWhile(ev =>
                                    !(ev.Message is MidiChannelMessage && ((MidiChannelMessage)ev.Message).MidiChannel == midiChannel)).Count();
                            }
                        }
                    }
                    else if (channelMessage.Command == MidiChannelCommand.NoteOn)
                    {
                        // note on: activate note
                        byte noteNumber = channelMessage.Parameter1;
                        midiNoteOn[midiChannel, noteNumber]++;

                        if (currentProgramNumber[midiChannel] == -1)
                        {
                            currentProgramNumber[midiChannel] = DEFAULT_PROGNUMBER;
                        }

                        if (lastTrackMapIndex[midiChannel] == -1)
                        {
                            lastTrackMapIndex[midiChannel] = firstChannelEventIndex[midiChannel];
                        }

                        if (percussion)
                        {
                            // check the most recent marker
                            MTrkChannelParam channelParam = midiEventMapTo[lastTrackMapIndex[midiChannel]];
                            if (channelParam.NoteNumber == -1)
                            {
                                channelParam.NoteNumber = noteNumber;
                            }
                            else if (channelParam.NoteNumber != noteNumber)
                            {
                                channelParam = new MTrkChannelParam();
                                channelParam.MidiChannel = midiChannel;
                                channelParam.BankNumber = currentBankNumber[midiChannel];
                                channelParam.ProgramNumber = currentProgramNumber[midiChannel];
                                channelParam.NoteNumber = noteNumber;

                                lastTrackMapIndex[midiChannel] = boundaryEventIndex[midiChannel];
                            }

                            try
                            {
                                midiEventMapTo.Add(lastTrackMapIndex[midiChannel], channelParam);
                            }
                            catch (ArgumentException e)
                            {
                                // debug output and fallback
                                int key = lastTrackMapIndex[midiChannel];
                                Console.WriteLine(e);
                                Console.WriteLine("   " + key + " => " + midiEventMapTo[key]);
                                Console.WriteLine("      is overwritten by " + channelParam);
                                midiEventMapTo[key] = channelParam;
                            }
                        }

                        // find the next channel message of the same channel
                        boundaryEventIndex[midiChannel] = midiEventIndex + 1 + midiEventListIn.Skip(midiEventIndex + 1).TakeWhile(ev =>
                            !(ev.Message is MidiChannelMessage && ((MidiChannelMessage)ev.Message).MidiChannel == midiChannel)).Count();
                    }
                    else if (channelMessage.Command == MidiChannelCommand.ProgramChange)
                    {
                        // program change
                        byte programNumber = channelMessage.Parameter1;
                        if (currentProgramNumber[midiChannel] != programNumber ||
                            currentBankNumber[midiChannel] != newBankNumber[midiChannel])
                        {
                            currentBankNumber[midiChannel] = newBankNumber[midiChannel];
                            currentProgramNumber[midiChannel] = programNumber;

                            // determine the output track
                            MTrkChannelParam channelParam;
                            if (lastTrackMapIndex[midiChannel] == -1)
                            {
                                // update the first checkpoint
                                channelParam = midiEventMapTo[firstChannelEventIndex[midiChannel]];
                                channelParam.BankNumber = currentBankNumber[midiChannel];
                                channelParam.ProgramNumber = currentProgramNumber[midiChannel];

                                midiEventMapTo[firstChannelEventIndex[midiChannel]] = channelParam;
                                lastTrackMapIndex[midiChannel] = firstChannelEventIndex[midiChannel];
                            }
                            else
                            {
                                // put new checkpoint
                                channelParam = new MTrkChannelParam();
                                channelParam.MidiChannel = midiChannel;
                                channelParam.BankNumber = currentBankNumber[midiChannel];
                                channelParam.ProgramNumber = currentProgramNumber[midiChannel];
                                channelParam.NoteNumber = -1; // will be filled later

                                try
                                {
                                    midiEventMapTo.Add(boundaryEventIndex[midiChannel], channelParam);
                                }
                                catch (ArgumentException e)
                                {
                                    // debug output and fallback
                                    int key = boundaryEventIndex[midiChannel];
                                    Console.WriteLine(e);
                                    Console.WriteLine("   " + key + " => " + midiEventMapTo[key]);
                                    Console.WriteLine("      is overwritten by " + channelParam);
                                    midiEventMapTo[key] = channelParam;
                                }
                                lastTrackMapIndex[midiChannel] = boundaryEventIndex[midiChannel];
                            }

                            // update the trigger timing
                            // find the next channel message of the same channel
                            boundaryEventIndex[midiChannel] = midiEventIndex + 1 + midiEventListIn.Skip(midiEventIndex + 1).TakeWhile(ev =>
                                !(ev.Message is MidiChannelMessage && ((MidiChannelMessage)ev.Message).MidiChannel == midiChannel)).Count();
                        }
                    }
                    else if (channelMessage.Command == MidiChannelCommand.ControlChange)
                    {
                        MidiControllerMessage controllerMessage = midiEvent.Message as MidiControllerMessage;

                        // dispatch bank select
                        if (controllerMessage.ControllerType == MidiControllerType.BankSelect)
                        {
                            newBankNumber[midiChannel] &= ~(127 << 7);
                            newBankNumber[midiChannel] |= controllerMessage.Value << 7;
                        }
                        else if (controllerMessage.ControllerType == MidiControllerType.BankSelectFine)
                        {
                            newBankNumber[midiChannel] &= ~127;
                            newBankNumber[midiChannel] |= controllerMessage.Value;
                        }
                    }
                }
            }

            // prepare for midi event writing
            IDictionary<MTrkChannelParam, MTrkChunkWithInfo> trackAssociatedWith = new Dictionary<MTrkChannelParam, MTrkChunkWithInfo>();
            List<MTrkChunkWithInfo> trackInfos = new List<MTrkChunkWithInfo>();
            if (midiEventMapTo.Count > 0)
            {
                // erase redundant items
                MTrkChannelParam? lastChannelParam = null;
                for (int midiEventIndex = 0; midiEventIndex < midiEventListIn.Count; midiEventIndex++)
                {
                    if (midiEventMapTo.ContainsKey(midiEventIndex))
                    {
                        // remove if item is exactly identical to previos one
                        if (lastChannelParam.HasValue && midiEventMapTo[midiEventIndex].Equals(lastChannelParam.Value))
                        {
                            midiEventMapTo.Remove(midiEventIndex);
                        }
                        else
                        {
                            lastChannelParam = midiEventMapTo[midiEventIndex];
                        }
                    }
                }

                // create output tracks
                foreach (KeyValuePair<int, MTrkChannelParam> aMidiEventMapTo in midiEventMapTo)
                {
                    if (verbose)
                    {
                        Console.WriteLine(String.Format("[MidiEventMap] Index: {0} => MidiChannel: {1}, BankNumber: {2}, ProgramNumber: {3}, NoteNumber: {4}",
                            aMidiEventMapTo.Key, aMidiEventMapTo.Value.MidiChannel, aMidiEventMapTo.Value.BankNumber, aMidiEventMapTo.Value.ProgramNumber, aMidiEventMapTo.Value.NoteNumber));
                    }

                    if (!trackAssociatedWith.ContainsKey(aMidiEventMapTo.Value))
                    {
                        MTrkChunkWithInfo trackInfo = new MTrkChunkWithInfo();
                        trackInfo.Track = new MTrkChunk();
                        trackInfo.Track.Events = new List<MidiFileEvent>();
                        trackInfo.Channel = aMidiEventMapTo.Value;
                        trackInfo.SortIndex = trackInfos.Count;
                        trackInfos.Add(trackInfo);
                        trackAssociatedWith[aMidiEventMapTo.Value] = trackInfo;
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
                MTrkChunkWithInfo trackInfo = new MTrkChunkWithInfo();
                trackInfo.Track = new MTrkChunk();
                trackInfo.Track.Events = new List<MidiFileEvent>();
                trackInfos.Add(trackInfo);
            }

            // initialize controller slots
            MidiChannelStatus[] status = new MidiChannelStatus[MaxChannels];
            for (int channel = 0; channel < MaxChannels; channel++)
            {
                status[channel] = new MidiChannelStatus();
            }

            // start copying midi events
            IDictionary<int, MTrkChunkWithInfo> currentOutputTrackInfo = new Dictionary<int, MTrkChunkWithInfo>();
            Queue<MTrkChunk>[,] notesAssociatedWithTrack = new Queue<MTrkChunk>[MaxChannels, MaxNotes];
            for (int midiEventIndex = 0; midiEventIndex < midiEventListIn.Count; midiEventIndex++)
            {
                MidiFileEvent midiEvent = midiEventListIn[midiEventIndex];
                MTrkChunk targetTrack = null;
                bool broadcastToAllTracks = false;

                if (verbose)
                {
                    Console.Write("[MidiEvent] Index: " + midiEventIndex);
                    Console.Write(", AbsoluteTime: " + midiEvent.AbsoluteTime);
                    Console.Write(", MessageType: " + midiEvent.Message.GetType().Name);
                    if (midiEvent.Message is MidiChannelMessage)
                    {
                        MidiChannelMessage channelMessage = midiEvent.Message as MidiChannelMessage;
                        Console.Write(", MidiChannel: " + channelMessage.MidiChannel);
                        Console.Write(", Command: " + channelMessage.Command);
                        Console.Write(", Parameter1: " + channelMessage.Parameter1);
                        Console.Write(", Parameter2: " + channelMessage.Parameter2);
                    }
                    Console.WriteLine();
                }

                // switch output track if necessary
                if (midiEventMapTo.ContainsKey(midiEventIndex))
                {
                    MTrkChannelParam aMidiEventMapTo = midiEventMapTo[midiEventIndex];
                    MTrkChunkWithInfo newTrackInfo = trackAssociatedWith[aMidiEventMapTo];
                    int channel = aMidiEventMapTo.MidiChannel;

                    MTrkChunkWithInfo oldTrackInfo = null;
                    if (currentOutputTrackInfo.ContainsKey(channel))
                    {
                        oldTrackInfo = currentOutputTrackInfo[channel];
                    }

                    if (oldTrackInfo != newTrackInfo)
                    {
                        // switch output track
                        currentOutputTrackInfo[channel] = newTrackInfo;

                        // copy separated controller values
                        if (copySeparatedControllers)
                        {
                            // readahead initialization events for new track (read until the first note on)
                            MidiChannelStatus initStatus = new MidiChannelStatus();
                            initStatus.DataEntryForRPN = status[channel].DataEntryForRPN;
                            initStatus.ParseMidiEvents(midiEventListIn.Skip(midiEventIndex).TakeWhile(ev =>
                                !(ev.Message is MidiChannelMessage && ((MidiChannelMessage)ev.Message).Command == MidiChannelCommand.NoteOn)), (byte)channel);

                            status[channel].AddUpdatedMidiEvents(newTrackInfo.Track, newTrackInfo.Status, midiEvent.AbsoluteTime, channel, initStatus);
                        }

                        // save current controller values
                        if (oldTrackInfo != null)
                        {
                            oldTrackInfo.Status = new MidiChannelStatus(status[channel]);
                        }
                    }
                }

                // dispatch message
                if (midiEvent.Message is MidiChannelMessage)
                {
                    MidiChannelMessage channelMessage = midiEvent.Message as MidiChannelMessage;
                    byte midiChannel = channelMessage.MidiChannel;

                    // determine output track
                    targetTrack = currentOutputTrackInfo[midiChannel].Track;

                    // dispatch note on/off
                    if (channelMessage.Command == MidiChannelCommand.NoteOff ||
                        (channelMessage.Command == MidiChannelCommand.NoteOn && channelMessage.Parameter2 == 0))
                    {
                        // note off
                        byte noteNumber = channelMessage.Parameter1;
                        if (notesAssociatedWithTrack[midiChannel, noteNumber] != null &&
                            notesAssociatedWithTrack[midiChannel, noteNumber].Count != 0)
                        {
                            targetTrack = notesAssociatedWithTrack[midiChannel, noteNumber].Dequeue();
                        }
                    }
                    else if (channelMessage.Command == MidiChannelCommand.NoteOn)
                    {
                        // note on
                        byte noteNumber = channelMessage.Parameter1;
                        if (notesAssociatedWithTrack[midiChannel, noteNumber] == null)
                        {
                            // allocate a queue if not available
                            notesAssociatedWithTrack[midiChannel, noteNumber] = new Queue<MTrkChunk>();
                        }
                        // remember the output track
                        notesAssociatedWithTrack[midiChannel, noteNumber].Enqueue(targetTrack);
                    }

                    // update channel status
                    status[midiChannel].ParseMidiEvents(midiEventListIn.Skip(midiEventIndex).Take(1), midiChannel);
                }
                else
                {
                    targetTrack = trackInfos[0].Track;

                    if (midiEvent.Message is MidiMetaMessage)
                    {
                        MidiMetaMessage metaMessage = midiEvent.Message as MidiMetaMessage;
                        if ((byte)metaMessage.MetaType == 0x21) // Unofficial port select
                        {
                            broadcastToAllTracks = true;
                        }
                    }
                }

                // add event to the list, if it's not end of track
                if (!(midiEvent.Message is MidiMetaMessage) ||
                    (midiEvent.Message as MidiMetaMessage).MetaType != MidiMetaType.EndOfTrack)
                {
                    if (broadcastToAllTracks)
                    {
                        foreach (MTrkChunkWithInfo trackInfo in trackInfos)
                        {
                            IList<MidiFileEvent> targetEventList = trackInfo.Track.Events as IList<MidiFileEvent>;
                            targetEventList.Add(midiEvent);
                        }
                    }
                    else
                    {
                        IList<MidiFileEvent> targetEventList = targetTrack.Events as IList<MidiFileEvent>;
                        targetEventList.Add(midiEvent);
                    }
                }
            }

            // construct the plain track list
            IList<MTrkChunk> tracks = new List<MTrkChunk>();
            foreach (MTrkChunkWithInfo trackInfo in trackInfos)
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
                endOfTrack.DeltaTime = absoluteEndTime - (midiLastEvent != null ? midiLastEvent.AbsoluteTime : 0);
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
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
                bool copySeparatedControllers = false;

                // parse options
                int argIndex = 0;
                while (argIndex < args.Length && args[argIndex].StartsWith("-"))
                {
                    string option = args[argIndex];
                    if (option == "--help")
                    {
                        ShowUsage();
                        return 1;
                    }
                    else if (option == "-cs" || option == "--copy-separated")
                    {
                        copySeparatedControllers = true;
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
                MidiFileData midiOutData = MidiSplit.SplitMidiFile(midiInData, copySeparatedControllers);
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
            Console.WriteLine("Usage: MidiSplit (options) input.mid output.mid");
            Console.WriteLine();
            Console.WriteLine("### Options");
            Console.WriteLine();
            Console.WriteLine("- `-cs` `--copy-separated`: Copy controller events that are updated by other tracks.");
        }

        static MidiFileData SplitMidiFile(MidiFileData midiInData, bool copySeparatedControllers)
        {
            MidiFileData midiOutData = new MidiFileData();
            midiOutData.Header = new MThdChunk();
            midiOutData.Header.Format = (ushort)MidiFileFormat.MultipleTracks;
            midiOutData.Header.TimeDivision = midiInData.Header.TimeDivision;

            IList<MTrkChunk> tracks = new List<MTrkChunk>();
            foreach (MTrkChunk midiTrackIn in midiInData.Tracks)
            {
                foreach (MTrkChunk midiTrackOut in MidiSplit.SplitMidiTrack(midiTrackIn, true))
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
        }

        protected class MidiChannelStatus
        {
            public IDictionary<int, int> ControlValue;
            public IDictionary<int, int> RPNValue;
            public IDictionary<int, int> NRPNValue;
            public int? PitchBendValue;
            public int? CurrentRPN;
            public int? CurrentNRPN;
            public bool DataEntryForRPN;

            public MidiChannelStatus()
            {
                ControlValue = new Dictionary<int, int>();
                RPNValue = new Dictionary<int, int>();
                NRPNValue = new Dictionary<int, int>();
                CurrentRPN = null;
                CurrentNRPN = null;
                PitchBendValue = null;
                DataEntryForRPN = true;
            }

            public MidiChannelStatus(MidiChannelStatus previousStatus)
            {
                ControlValue = new Dictionary<int, int>(previousStatus.ControlValue);
                RPNValue = new Dictionary<int, int>(previousStatus.RPNValue);
                NRPNValue = new Dictionary<int, int>(previousStatus.NRPNValue);
                CurrentRPN = previousStatus.CurrentRPN;
                CurrentNRPN = previousStatus.CurrentNRPN;
                PitchBendValue = previousStatus.PitchBendValue;
                DataEntryForRPN = previousStatus.DataEntryForRPN;
            }

            public void ParseMidiEvents(IEnumerable<MidiFileEvent> midiEventList)
            {
                foreach (var midiEvent in midiEventList)
                {
                    if (midiEvent.Message is MidiChannelMessage)
                    {
                        MidiChannelMessage channelMessage = midiEvent.Message as MidiChannelMessage;

                        if (channelMessage.Command == MidiChannelCommand.ControlChange)
                        {
                            MidiControllerMessage controllerMessage = midiEvent.Message as MidiControllerMessage;

                            if (controllerMessage.ControllerType == MidiControllerType.RegisteredParameterCoarse)
                            {
                                if (!CurrentRPN.HasValue)
                                {
                                    CurrentRPN = 0;
                                }

                                CurrentRPN &= ~(127 << 7);
                                CurrentRPN |= controllerMessage.Value << 7;
                                DataEntryForRPN = true;
                            }
                            else if (controllerMessage.ControllerType == MidiControllerType.RegisteredParameterFine)
                            {
                                if (!CurrentRPN.HasValue)
                                {
                                    CurrentRPN = 0;
                                }

                                CurrentRPN &= ~127;
                                CurrentRPN |= controllerMessage.Value;
                                DataEntryForRPN = true;
                            }
                            else if (controllerMessage.ControllerType == MidiControllerType.NonregisteredParameterCoarse)
                            {
                                if (!CurrentNRPN.HasValue)
                                {
                                    CurrentNRPN = 0;
                                }

                                CurrentNRPN &= ~(127 << 7);
                                CurrentNRPN |= controllerMessage.Value << 7;
                                DataEntryForRPN = false;
                            }
                            else if (controllerMessage.ControllerType == MidiControllerType.NonregisteredParameterFine)
                            {
                                if (!CurrentNRPN.HasValue)
                                {
                                    CurrentNRPN = 0;
                                }

                                CurrentNRPN &= ~127;
                                CurrentNRPN |= controllerMessage.Value;
                                DataEntryForRPN = false;
                            }
                            // Data Entry
                            else if (controllerMessage.ControllerType == MidiControllerType.DataEntrySlider ||
                                controllerMessage.ControllerType == MidiControllerType.DataEntrySliderFine)
                            {
                                // RPN or NRPN?
                                IDictionary<int, int> currentValue;
                                int? targetSlot;
                                if (DataEntryForRPN)
                                {
                                    currentValue = RPNValue;
                                    targetSlot = CurrentRPN;
                                }
                                else
                                {
                                    currentValue = NRPNValue;
                                    targetSlot = CurrentNRPN;
                                }

                                // MSB or LSB?
                                int bitShift = 0;
                                if (controllerMessage.ControllerType == MidiControllerType.DataEntrySlider)
                                {
                                    bitShift = 7;
                                }

                                // save the data value
                                if (targetSlot.HasValue)
                                {
                                    if (currentValue.ContainsKey(targetSlot.Value))
                                    {
                                        currentValue[targetSlot.Value] &= ~(127 << bitShift);
                                        currentValue[targetSlot.Value] |= controllerMessage.Value << bitShift;
                                    }
                                    else
                                    {
                                        currentValue[targetSlot.Value] = controllerMessage.Value << bitShift;
                                    }
                                }
                            }
                            // Reset All Controllers
                            else if (controllerMessage.ControllerType == MidiControllerType.AllControllersOff)
                            {
                                // See: General MIDI Level 2 Recommended Practice (RP024)
                                ControlValue[(int)MidiControllerType.ModulationWheel] = 0;
                                ControlValue[(int)MidiControllerType.Expression] = 127;
                                ControlValue[(int)MidiControllerType.HoldPedal] = 0;
                                ControlValue[(int)MidiControllerType.Portamento] = 0;
                                ControlValue[(int)MidiControllerType.SustenutoPedal] = 0;
                                ControlValue[(int)MidiControllerType.SoftPedal] = 0;
                                CurrentRPN = 0x3fff;
                                PitchBendValue = 0;
                                // Channel pressure 0 (off)
                            }
                            // anything else
                            else if (controllerMessage.ControllerType != MidiControllerType.BankSelect &&
                                controllerMessage.ControllerType != MidiControllerType.BankSelectFine &&
                                controllerMessage.ControllerType != MidiControllerType.AllSoundOff &&
                                controllerMessage.ControllerType != MidiControllerType.LocalKeyboard &&
                                controllerMessage.ControllerType != MidiControllerType.AllNotesOff &&
                                controllerMessage.ControllerType != MidiControllerType.OmniModeOff &&
                                controllerMessage.ControllerType != MidiControllerType.OmniModeOn &&
                                controllerMessage.ControllerType != MidiControllerType.MonoOperation &&
                                controllerMessage.ControllerType != MidiControllerType.PolyOperation)
                            {
                                IDictionary<int, int> currentValue = ControlValue;
                                currentValue[(int)controllerMessage.ControllerType] = controllerMessage.Value;
                            }
                        }
                        else if (channelMessage.Command == MidiChannelCommand.PitchWheel)
                        {
                            int pitchBendValue = (channelMessage.Parameter1 | (channelMessage.Parameter2 << 7)) - 8192;
                            PitchBendValue = pitchBendValue;
                        }
                        else if (channelMessage.Command == MidiChannelCommand.ChannelPressure)
                        {
                            // not supported
                        }
                        else if (channelMessage.Command == MidiChannelCommand.PolyPressure)
                        {
                            // not supported
                        }
                    }
                }
            }

            public void AddControllerMessage(MTrkChunk midiTrack, long absoluteTime, int channel, MidiControllerType controller, int value)
            {
                IList<MidiFileEvent> midiEvents = midiTrack.Events as IList<MidiFileEvent>;
                MidiMessageFactory midiMessageFactory = new MidiMessageFactory();
                MidiControllerMessage message = midiMessageFactory.CreateControllerMessage((byte)channel, controller, (byte)value);
                MidiFileEvent midiEvent = new MidiFileEvent();
                midiEvent.AbsoluteTime = absoluteTime;
                midiEvent.Message = message;
                midiEvents.Add(midiEvent);
            }

            public void AddPitchWheelMessage(MTrkChunk midiTrack, long absoluteTime, int channel, int value)
            {
                value = Math.Max(0, Math.Min(0x3fff, value + 8192));

                IList<MidiFileEvent> midiEvents = midiTrack.Events as IList<MidiFileEvent>;
                MidiMessageFactory midiMessageFactory = new MidiMessageFactory();
                MidiChannelMessage message = midiMessageFactory.CreateChannelMessage(MidiChannelCommand.PitchWheel, (byte)channel, (byte)(value & 0x7f), (byte)((value >> 7) & 0x7f));
                MidiFileEvent midiEvent = new MidiFileEvent();
                midiEvent.AbsoluteTime = absoluteTime;
                midiEvent.Message = message;
                midiEvents.Add(midiEvent);
            }

            public void AddRPNMessage(MTrkChunk midiTrack, long absoluteTime, int channel, int controller, int? value)
            {
                AddControllerMessage(midiTrack, absoluteTime, channel, MidiControllerType.RegisteredParameterCoarse, (controller >> 7) & 0x7f);
                AddControllerMessage(midiTrack, absoluteTime, channel, MidiControllerType.RegisteredParameterFine, controller & 0x7f);
                if (value != null && controller != 0x3fff) // not RPN NULL
                {
                    AddControllerMessage(midiTrack, absoluteTime, channel, MidiControllerType.DataEntrySlider, (value.Value >> 7) & 0x7f);
                    AddControllerMessage(midiTrack, absoluteTime, channel, MidiControllerType.DataEntrySliderFine, value.Value & 0x7f);
                }
            }

            public void AddNRPNMessage(MTrkChunk midiTrack, long absoluteTime, int channel, int controller, int? value)
            {
                AddControllerMessage(midiTrack, absoluteTime, channel, MidiControllerType.NonregisteredParameterCoarse, (controller >> 7) & 0x7f);
                AddControllerMessage(midiTrack, absoluteTime, channel, MidiControllerType.NonregisteredParameterFine, controller & 0x7f);
                if (value != null && controller != 0x3fff) // not NRPN NULL
                {
                    AddControllerMessage(midiTrack, absoluteTime, channel, MidiControllerType.DataEntrySlider, (value.Value >> 7) & 0x7f);
                    AddControllerMessage(midiTrack, absoluteTime, channel, MidiControllerType.DataEntrySliderFine, value.Value & 0x7f);
                }
            }

            public void AddUpdatedMidiEvents(MTrkChunk midiTrack, MidiChannelStatus previousStatus, long absoluteTime, int channel)
            {
                IDictionary<int, int> updatedControlValue = new Dictionary<int, int>();
                foreach (var control in ControlValue)
                {
                    IDictionary<int, int> previousValue = (previousStatus != null) ? previousStatus.ControlValue : null;
                    bool hasPreviousValue = previousValue != null && previousValue.ContainsKey(control.Key);
                    if (hasPreviousValue && previousValue[control.Key] == control.Value)
                    {
                        continue;
                    }
                    updatedControlValue.Add(control);
                }

                IDictionary<int, int> updatedRPNValue = new Dictionary<int, int>();
                foreach (var control in RPNValue)
                {
                    IDictionary<int, int> previousValue = (previousStatus != null) ? previousStatus.RPNValue : null;
                    bool hasPreviousValue = previousValue != null && previousValue.ContainsKey(control.Key);
                    if (hasPreviousValue && previousValue[control.Key] == control.Value)
                    {
                        continue;
                    }
                    updatedRPNValue.Add(control);
                }

                IDictionary<int, int> updatedNRPNValue = new Dictionary<int, int>();
                foreach (var control in NRPNValue)
                {
                    IDictionary<int, int> previousValue = (previousStatus != null) ? previousStatus.NRPNValue : null;
                    bool hasPreviousValue = previousValue != null && previousValue.ContainsKey(control.Key);
                    if (hasPreviousValue && previousValue[control.Key] == control.Value)
                    {
                        continue;
                    }
                    updatedNRPNValue.Add(control);
                }

                // control change
                foreach (var control in updatedControlValue)
                {
                    AddControllerMessage(midiTrack, absoluteTime, channel, (MidiControllerType)control.Key, (byte)control.Value);
                }

                // pitch bend
                if (PitchBendValue.HasValue && (previousStatus == null || previousStatus.PitchBendValue != PitchBendValue))
                {
                    AddPitchWheelMessage(midiTrack, absoluteTime, channel, PitchBendValue.Value);
                }

                // RPN
                foreach (var control in updatedRPNValue)
                {
                    // process write for current RPN at last, not now
                    if (control.Key == CurrentRPN)
                    {
                        continue;
                    }

                    AddRPNMessage(midiTrack, absoluteTime, channel, control.Key, control.Value);
                }
                // restore current RPN
                if (CurrentRPN.HasValue)
                {
                    if (updatedRPNValue.ContainsKey(CurrentRPN.Value))
                    {
                        AddRPNMessage(midiTrack, absoluteTime, channel, CurrentRPN.Value, RPNValue[CurrentRPN.Value]);
                    }
                    else
                    {
                        AddRPNMessage(midiTrack, absoluteTime, channel, CurrentRPN.Value, null);
                    }
                }

                // NRPN
                foreach (var control in updatedNRPNValue)
                {
                    IDictionary<int, int> previousValue = (previousStatus != null) ? previousStatus.NRPNValue : null;
                    bool hasPreviousValue = previousValue != null && previousValue.ContainsKey(control.Key);

                    // process write for current NRPN at last, not now
                    if (control.Key == CurrentNRPN)
                    {
                        continue;
                    }

                    AddNRPNMessage(midiTrack, absoluteTime, channel, control.Key, control.Value);
                }
                // restore current NRPN
                if (CurrentNRPN.HasValue)
                {
                    if (updatedNRPNValue.ContainsKey(CurrentNRPN.Value))
                    {
                        AddNRPNMessage(midiTrack, absoluteTime, channel, CurrentNRPN.Value, NRPNValue[CurrentNRPN.Value]);
                    }
                    else
                    {
                        AddNRPNMessage(midiTrack, absoluteTime, channel, CurrentNRPN.Value, null);
                    }
                }
            }
        }

        protected class MTrkChunkWithInfo
        {
            public MTrkChannelParam Channel;
            public MTrkChunk Track;
            public int SortIndex; // a number used for stable sort
            public MidiChannelStatus Status;
        }

        static IEnumerable<MTrkChunk> SplitMidiTrack(MTrkChunk midiTrackIn, bool copySeparatedControllers)
        {
            const int MaxChannels = 16;
            const int MaxNotes = 128;

            //int midiEventIndex;
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

            // copy midi events to a list
            IList<MidiFileEvent> midiEventListIn = new List<MidiFileEvent>(midiTrackIn.Events);

            // pre-scan input events
            for (int midiEventIndex = 0; midiEventIndex < midiEventListIn.Count; midiEventIndex++)
            {
                MidiFileEvent midiEvent = midiEventListIn[midiEventIndex];

                // update the end time of the track
                absoluteEndTime = midiEvent.AbsoluteTime;

                // dispatch message
                if (midiEvent.Message is MidiChannelMessage)
                {
                    MidiChannelMessage channelMessage = midiEvent.Message as MidiChannelMessage;
                    byte midiChannel = channelMessage.MidiChannel;

                    // remember the first channel messeage index
                    if (firstChannelEventIndex[midiChannel] == -1)
                    {
                        firstChannelEventIndex[midiChannel] = midiEventIndex;

                        // determine the output track temporalily,
                        // for tracks that do not have any program changes
                        MTrkChannelParam channelParam = new MTrkChannelParam();
                        channelParam.MidiChannel = midiChannel;
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
                                boundaryEventIndex[midiChannel] = midiEventIndex + 1;
                            }
                        }
                    }
                    else if (channelMessage.Command == MidiChannelCommand.NoteOn)
                    {
                        // note on: activate note
                        byte noteNumber = channelMessage.Parameter1;
                        midiNoteOn[midiChannel, noteNumber]++;
                        boundaryEventIndex[midiChannel] = midiEventIndex + 1;
                    }
                    else if (channelMessage.Command == MidiChannelCommand.ProgramChange)
                    {
                        // program change
                        byte programNumber = channelMessage.Parameter1;
                        currentBankNumber[midiChannel] = newBankNumber[midiChannel];
                        currentProgramNumber[midiChannel] = programNumber;

                        // determine the output track
                        MTrkChannelParam channelParam = new MTrkChannelParam();
                        channelParam.MidiChannel = midiChannel;
                        channelParam.BankNumber = currentBankNumber[midiChannel];
                        channelParam.ProgramNumber = currentProgramNumber[midiChannel];

                        // switch the track from the last silence
                        if (firstProgramChange[midiChannel])
                        {
                            midiEventMapTo[firstChannelEventIndex[midiChannel]] = channelParam;
                            firstProgramChange[midiChannel] = false;
                        }
                        else
                        {
                            midiEventMapTo[boundaryEventIndex[midiChannel]] = channelParam;
                        }

                        // update the trigger timing
                        boundaryEventIndex[midiChannel] = midiEventIndex + 1;
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

            // create output tracks
            IDictionary<MTrkChannelParam, MTrkChunkWithInfo> trackAssociatedWith = new Dictionary<MTrkChannelParam, MTrkChunkWithInfo>();
            List<MTrkChunkWithInfo> trackInfos = new List<MTrkChunkWithInfo>();
            if (midiEventMapTo.Count > 0)
            {
                foreach (KeyValuePair<int, MTrkChannelParam> aMidiEventMapTo in midiEventMapTo)
                {
                    //Console.WriteLine(String.Format("Event: {0}, Channel: {1}, Bank: {2}, Program: {3}",
                    //    aMidiEventMapTo.Key, aMidiEventMapTo.Value.MidiChannel, aMidiEventMapTo.Value.BankNumber, aMidiEventMapTo.Value.ProgramNumber));
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

                // dispatch message
                if (midiEvent.Message is MidiChannelMessage)
                {
                    MidiChannelMessage channelMessage = midiEvent.Message as MidiChannelMessage;
                    byte midiChannel = channelMessage.MidiChannel;

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

                        // switch output track
                        currentOutputTrackInfo[channel] = newTrackInfo;

                        // copy separated controller values
                        if (copySeparatedControllers)
                        {
                            status[channel].AddUpdatedMidiEvents(newTrackInfo.Track, newTrackInfo.Status, midiEvent.AbsoluteTime, channel);
                        }

                        // save current controller values
                        if (oldTrackInfo != null)
                        {
                            oldTrackInfo.Status = new MidiChannelStatus(status[channel]);
                        }
                    }

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
                    status[midiChannel].ParseMidiEvents(midiEventListIn.Skip(midiEventIndex).Take(1));
                }
                else
                {
                    targetTrack = trackInfos[0].Track;

                    if (midiEvent.Message is MidiMetaMessage)
                    {
                        MidiMetaMessage metaMessage = midiEvent.Message as MidiMetaMessage;
                        if ((byte)metaMessage.MetaType == 21) // Unofficial port select
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
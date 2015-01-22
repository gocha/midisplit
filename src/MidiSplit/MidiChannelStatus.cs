using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CannedBytes.Midi.IO;
using CannedBytes.Midi.Message;

namespace MidiSplit
{
    public class MidiChannelStatus
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

        public void ParseMidiEvents(IEnumerable<MidiFileEvent> midiEventList, byte midiChannel)
        {
            foreach (var midiEvent in midiEventList)
            {
                if (midiEvent.Message is MidiChannelMessage)
                {
                    MidiChannelMessage channelMessage = midiEvent.Message as MidiChannelMessage;

                    if (channelMessage.MidiChannel == midiChannel)
                    {
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

        public void AddUpdatedMidiEvents(MTrkChunk midiTrack, MidiChannelStatus previousStatus, long absoluteTime, int channel, MidiChannelStatus excludeStatus)
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
                if (excludeStatus != null && excludeStatus.ControlValue.ContainsKey(control.Key))
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
                if (excludeStatus != null && excludeStatus.RPNValue.ContainsKey(control.Key))
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
                if (excludeStatus != null && excludeStatus.NRPNValue.ContainsKey(control.Key))
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
                if (excludeStatus == null || !excludeStatus.PitchBendValue.HasValue)
                {
                    AddPitchWheelMessage(midiTrack, absoluteTime, channel, PitchBendValue.Value);
                }
            }

            // Swap RPN/NRPN order by current selection
            bool writeRPN = !DataEntryForRPN;
            for (int selectRPNorNRPN = 0; selectRPNorNRPN < 2; selectRPNorNRPN++)
            {
                if (writeRPN)
                {
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
                        else if (previousStatus == null || CurrentRPN != previousStatus.CurrentRPN)
                        {
                            AddRPNMessage(midiTrack, absoluteTime, channel, CurrentRPN.Value, null);
                        }
                    }
                }
                else
                {
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
                        else if (previousStatus == null || CurrentNRPN != previousStatus.CurrentNRPN)
                        {
                            AddNRPNMessage(midiTrack, absoluteTime, channel, CurrentNRPN.Value, null);
                        }
                    }
                }
                writeRPN = !writeRPN;
            }
        }
    }
}

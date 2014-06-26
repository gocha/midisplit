using System.Collections.Generic;
using CannedBytes.Midi.IO;

namespace MidiSplit
{
    class MidiFileData
    {
        public MThdChunk Header;
        public IEnumerable<MTrkChunk> Tracks;
    }
}
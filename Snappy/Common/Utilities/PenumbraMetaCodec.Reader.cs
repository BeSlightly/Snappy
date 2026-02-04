using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Snappy.Common.Utilities;

public static partial class PenumbraMetaCodec
{
    private ref struct SpanBinaryReader
    {
        private readonly ReadOnlySpan<byte> _span;
        public int Position { get; private set; }
        public int Remaining => _span.Length - Position;

        public SpanBinaryReader(ReadOnlySpan<byte> span)
        {
            _span = span;
            Position = 0;
        }

        public T Read<T>() where T : unmanaged
        {
            var size = Unsafe.SizeOf<T>();
            if (size > _span.Length - Position)
                throw new EndOfStreamException();

            var value = MemoryMarshal.Read<T>(_span.Slice(Position));
            Position += size;
            return value;
        }

        public int ReadInt32()
        {
            return Read<int>();
        }

        public uint ReadUInt32()
        {
            return Read<uint>();
        }
    }
}

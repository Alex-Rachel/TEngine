using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LightProto.Parser
{
    public sealed class ByteListProtoParser : IProtoParser<List<byte>>
    {
        public static IProtoReader<List<byte>> ProtoReader { get; } = new ByteListProtoReader();
        public static IProtoWriter<List<byte>> ProtoWriter { get; } = new ByteListProtoWriter();

        sealed class ByteListProtoReader : IProtoReader<List<byte>>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;
            public bool IsMessage => false;

            public List<byte> ParseFrom(ref ReaderContext input)
            {
                var length = input.ReadLength();
                var bytes = ParsingPrimitives.ReadRawBytes(ref input.buffer, ref input.state, length);
                return new List<byte>(bytes);
            }
        }

        sealed class ByteListProtoWriter : IProtoWriter<List<byte>>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;
            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public int CalculateSize(List<byte> value)
            {
                return CodedOutputStream.ComputeLengthSize(value.Count) + value.Count;
            }

            public void WriteTo(ref WriterContext output, List<byte> value)
            {
                output.WriteLength(value.Count);
                WritingPrimitives.WriteRawBytes(
                    ref output.buffer,
                    ref output.state,
#if NET5_0_OR_GREATER
                CollectionsMarshal.AsSpan(value)
#else
                    value.ToArray()
#endif
                );
            }
        }
    }
}

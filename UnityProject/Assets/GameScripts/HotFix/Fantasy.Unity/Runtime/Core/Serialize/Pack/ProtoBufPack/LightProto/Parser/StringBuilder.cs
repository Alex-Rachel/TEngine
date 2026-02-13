using System.Text;

namespace LightProto.Parser
{
    public sealed class StringBuilderProtoParser : IProtoParser<StringBuilder>
    {
        public static IProtoReader<StringBuilder> ProtoReader { get; } = new StringBuilderProtoReader();
        public static IProtoWriter<StringBuilder> ProtoWriter { get; } = new StringBuilderProtoWriter();

        sealed class StringBuilderProtoReader : IProtoReader<StringBuilder>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;
            public bool IsMessage => false;

            public StringBuilder ParseFrom(ref ReaderContext input)
            {
                return new StringBuilder(input.ReadString());
            }
        }

        sealed class StringBuilderProtoWriter : IProtoWriter<StringBuilder>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;
            public bool IsMessage => false;

            public int CalculateSize(StringBuilder value)
            {
#if NET5_0_OR_GREATER
            int size = 0;
            foreach (var readOnlyMemory in value.GetChunks())
            {
                int byteArraySize = WritingPrimitives.Utf8Encoding.GetByteCount(
                    readOnlyMemory.Span
                );
                size += CodedOutputStream.ComputeLengthSize(byteArraySize) + byteArraySize;
            }
            return size;
#else
                int byteArraySize = WritingPrimitives.Utf8Encoding.GetByteCount(value.ToString());
                return CodedOutputStream.ComputeLengthSize(byteArraySize) + byteArraySize;
#endif
            }

            public void WriteTo(ref WriterContext output, StringBuilder value)
            {
                output.WriteString(value.ToString());
            }
        }
    }
}

namespace LightProto.Parser
{
    public sealed class BooleanProtoParser : IProtoParser<bool>
    {
        public static IProtoReader<bool> ProtoReader { get; } = new BooleanProtoReader();
        public static IProtoWriter<bool> ProtoWriter { get; } = new BooleanProtoWriter();

        sealed class BooleanProtoReader : IProtoReader<bool>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.Varint;
            public bool IsMessage => false;

            public bool ParseFrom(ref ReaderContext input)
            {
                return input.ReadBool();
            }
        }

        sealed class BooleanProtoWriter : IProtoWriter<bool>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.Varint;
            public bool IsMessage => false;

            public int CalculateSize(bool value)
            {
                return CodedOutputStream.ComputeBoolSize(value);
            }

            public void WriteTo(ref WriterContext output, bool value)
            {
                output.WriteBool(value);
            }
        }
    }
}

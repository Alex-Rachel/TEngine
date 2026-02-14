namespace LightProto.Parser
{
#if NET5_0_OR_GREATER
public sealed class RuneProtoParser : IProtoParser<System.Text.Rune>
{
    public static IProtoReader<System.Text.Rune> ProtoReader { get; } = new RuneProtoReader();
    public static IProtoWriter<System.Text.Rune> ProtoWriter { get; } = new RuneProtoWriter();

    sealed class RuneProtoReader : IProtoReader<System.Text.Rune>
    {
        public WireFormat.WireType WireType => WireFormat.WireType.Varint;
        public bool IsMessage => false;

        public System.Text.Rune ParseFrom(ref ReaderContext input)
        {
            return new System.Text.Rune(input.ReadUInt32());
        }
    }

    sealed class RuneProtoWriter : IProtoWriter<System.Text.Rune>
    {
        public WireFormat.WireType WireType => WireFormat.WireType.Varint;
        public bool IsMessage => false;

        public int CalculateSize(System.Text.Rune value)
        {
            return CodedOutputStream.ComputeUInt32Size((uint)value.Value);
        }

        public void WriteTo(ref WriterContext output, System.Text.Rune value)
        {
            output.WriteUInt32((uint)value.Value);
        }
    }
}
#endif
}

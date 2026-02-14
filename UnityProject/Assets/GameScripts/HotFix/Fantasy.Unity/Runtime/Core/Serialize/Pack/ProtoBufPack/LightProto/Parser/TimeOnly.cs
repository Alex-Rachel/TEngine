namespace LightProto.Parser
{
#if NET6_0_OR_GREATER
public sealed class TimeOnlyProtoParser : IProtoParser<TimeOnly>
{
    public static IProtoReader<TimeOnly> ProtoReader { get; } = new TimeOnlyProtoReader();
    public static IProtoWriter<TimeOnly> ProtoWriter { get; } = new TimeOnlyProtoWriter();

    sealed class TimeOnlyProtoReader : IProtoReader<TimeOnly>
    {
        public WireFormat.WireType WireType => WireFormat.WireType.Varint;
        public bool IsMessage => false;

        public TimeOnly ParseFrom(ref ReaderContext input)
        {
            return new TimeOnly(input.ReadInt64());
        }
    }

    sealed class TimeOnlyProtoWriter : IProtoWriter<TimeOnly>
    {
        public WireFormat.WireType WireType => WireFormat.WireType.Varint;
        public bool IsMessage => false;

        public int CalculateSize(TimeOnly value)
        {
            return CodedOutputStream.ComputeInt64Size(value.Ticks);
        }

        public void WriteTo(ref WriterContext output, TimeOnly value)
        {
            output.WriteInt64(value.Ticks);
        }
    }
}
#endif
}

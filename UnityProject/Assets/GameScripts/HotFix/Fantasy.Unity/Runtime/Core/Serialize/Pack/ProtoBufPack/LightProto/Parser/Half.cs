namespace LightProto.Parser
{
#if NET5_0_OR_GREATER
public sealed class HalfProtoParser : IProtoParser<Half>
{
    public static IProtoReader<Half> ProtoReader { get; } = new HalfProtoReader();
    public static IProtoWriter<Half> ProtoWriter { get; } = new HalfProtoWriter();

    sealed class HalfProtoReader : IProtoReader<Half>
    {
        public WireFormat.WireType WireType => WireFormat.WireType.Fixed32;
        public bool IsMessage => false;

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
        )]
        public Half ParseFrom(ref ReaderContext input)
        {
            return (Half)input.ReadFloat();
        }
    }

    sealed class HalfProtoWriter : IProtoWriter<Half>
    {
        public WireFormat.WireType WireType => WireFormat.WireType.Fixed32;
        public bool IsMessage => false;

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
        )]
        public int CalculateSize(Half value)
        {
            return CodedOutputStream.ComputeFloatSize((float)value);
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
        )]
        public void WriteTo(ref WriterContext output, Half value)
        {
            output.WriteFloat((float)value);
        }
    }
}
#endif
}

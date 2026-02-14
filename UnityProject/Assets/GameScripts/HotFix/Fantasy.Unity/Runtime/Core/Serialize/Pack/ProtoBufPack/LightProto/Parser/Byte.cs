namespace LightProto.Parser
{
    public sealed class ByteProtoParser : IProtoParser<byte>
    {
        public static IProtoReader<byte> ProtoReader { get; } = new ByteProtoReader();
        public static IProtoWriter<byte> ProtoWriter { get; } = new ByteProtoWriter();

        sealed class ByteProtoReader : IProtoReader<byte>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.Varint;
            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public byte ParseFrom(ref ReaderContext input)
            {
                return (byte)input.ReadUInt32();
            }
        }

        sealed class ByteProtoWriter : IProtoWriter<byte>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.Varint;
            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public int CalculateSize(byte value)
            {
                return CodedOutputStream.ComputeUInt32Size(value);
            }

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public void WriteTo(ref WriterContext output, byte value)
            {
                output.WriteUInt32(value);
            }
        }
    }
}

namespace LightProto.Parser
{
    public sealed class Int32ProtoParser : IProtoParser<int>
    {
        public static IProtoReader<int> ProtoReader { get; } = new Int32ProtoReader();
        public static IProtoWriter<int> ProtoWriter { get; } = new Int32ProtoWriter();

        sealed class Int32ProtoReader : IProtoReader<int>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.Varint;
            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public int ParseFrom(ref ReaderContext input)
            {
                return input.ReadInt32();
            }
        }

        sealed class Int32ProtoWriter : IProtoWriter<int>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.Varint;
            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public int CalculateSize(int value)
            {
                return CodedOutputStream.ComputeInt32Size(value);
            }

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public void WriteTo(ref WriterContext output, int value)
            {
                output.WriteInt32(value);
            }
        }
    }
}

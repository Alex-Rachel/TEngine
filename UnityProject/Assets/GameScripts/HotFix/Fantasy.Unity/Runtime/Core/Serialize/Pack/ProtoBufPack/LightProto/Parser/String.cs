namespace LightProto.Parser
{
    public sealed class StringProtoParser : IProtoParser<string>
    {
        public static IProtoReader<string> ProtoReader { get; } = new StringProtoReader();
        public static IProtoWriter<string> ProtoWriter { get; } = new StringProtoWriter();

        sealed class StringProtoReader : IProtoReader<string>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;
            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public string ParseFrom(ref ReaderContext input)
            {
                return input.ReadString();
            }
        }

        sealed class StringProtoWriter : IProtoWriter<string>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;
            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public int CalculateSize(string value)
            {
                return CodedOutputStream.ComputeStringSize(value);
            }

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public void WriteTo(ref WriterContext output, string value)
            {
                output.WriteString(value);
            }
        }
    }
}

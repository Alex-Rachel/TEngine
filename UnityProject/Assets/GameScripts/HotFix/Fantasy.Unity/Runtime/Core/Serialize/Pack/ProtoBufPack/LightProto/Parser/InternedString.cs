namespace LightProto.Parser
{
    public sealed class InternedStringProtoParser : IProtoParser<string>
    {
        public static IProtoReader<string> ProtoReader { get; } = new InternedStringProtoReader();
        public static IProtoWriter<string> ProtoWriter => StringProtoParser.ProtoWriter;

        sealed class InternedStringProtoReader : IProtoReader<string>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;
            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public string ParseFrom(ref ReaderContext input)
            {
                return string.Intern(input.ReadString());
            }
        }
    }
}

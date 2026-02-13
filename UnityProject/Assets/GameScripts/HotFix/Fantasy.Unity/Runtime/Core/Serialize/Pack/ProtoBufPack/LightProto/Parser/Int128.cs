namespace LightProto.Parser
{
#if NET7_0_OR_GREATER
[ProtoSurrogateFor(typeof(Int128))]
[ProtoContract]
public partial struct Int128ProtoParser
{
    [ProtoMember(1)]
    internal ulong Upper { get; set; }

    [ProtoMember(2)]
    internal ulong Lower { get; set; }

    public static implicit operator Int128(Int128ProtoParser protoParser)
    {
        return new Int128(protoParser.Upper, protoParser.Lower);
    }

    public static implicit operator Int128ProtoParser(Int128 value)
    {
        ulong lower = (ulong)(value);
        ulong upper = (ulong)(value >> 64);
        return new Int128ProtoParser { Upper = upper, Lower = lower };
    }
}
#endif
}

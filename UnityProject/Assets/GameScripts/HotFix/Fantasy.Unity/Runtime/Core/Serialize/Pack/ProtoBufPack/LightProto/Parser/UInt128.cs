namespace LightProto.Parser
{
#if NET7_0_OR_GREATER
[ProtoSurrogateFor(typeof(UInt128))]
[ProtoContract]
public partial struct UInt128ProtoParser
{
    [ProtoMember(1)]
    internal ulong Upper { get; set; }

    [ProtoMember(2)]
    internal ulong Lower { get; set; }

    public static implicit operator UInt128(UInt128ProtoParser protoParser)
    {
        return new UInt128(protoParser.Upper, protoParser.Lower);
    }

    public static implicit operator UInt128ProtoParser(UInt128 value)
    {
        ulong lower = (ulong)(value);
        ulong upper = (ulong)(value >> 64);
        return new UInt128ProtoParser { Upper = upper, Lower = lower };
    }
}
#endif
}

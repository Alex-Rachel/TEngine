#if NET7_0_OR_GREATER
using System.Numerics;

namespace LightProto.Parser
{
[ProtoContract]
[ProtoSurrogateFor(typeof(Vector2))]
public partial struct Vector2ProtoParser
{
    [ProtoMember(1)]
    internal float X { get; set; }

    [ProtoMember(2)]
    internal float Y { get; set; }

    public static implicit operator Vector2(Vector2ProtoParser protoParser)
    {
        return new Vector2(protoParser.X, protoParser.Y);
    }

    public static implicit operator Vector2ProtoParser(Vector2 value)
    {
        return new Vector2ProtoParser() { X = value.X, Y = value.Y };
    }
}
}
#endif

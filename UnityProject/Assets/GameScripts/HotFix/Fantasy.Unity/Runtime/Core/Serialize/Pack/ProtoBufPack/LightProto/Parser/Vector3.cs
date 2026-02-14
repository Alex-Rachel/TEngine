#if NET7_0_OR_GREATER
using System.Numerics;

namespace LightProto.Parser
{
[ProtoContract]
[ProtoSurrogateFor(typeof(Vector3))]
public partial struct Vector3ProtoParser
{
    [ProtoMember(1)]
    internal float X { get; set; }

    [ProtoMember(2)]
    internal float Y { get; set; }

    [ProtoMember(3)]
    internal float Z { get; set; }

    public static implicit operator Vector3(Vector3ProtoParser protoParser)
    {
        return new Vector3(protoParser.X, protoParser.Y, protoParser.Z);
    }

    public static implicit operator Vector3ProtoParser(Vector3 value)
    {
        return new Vector3ProtoParser()
        {
            X = value.X,
            Y = value.Y,
            Z = value.Z,
        };
    }
}
}
#endif

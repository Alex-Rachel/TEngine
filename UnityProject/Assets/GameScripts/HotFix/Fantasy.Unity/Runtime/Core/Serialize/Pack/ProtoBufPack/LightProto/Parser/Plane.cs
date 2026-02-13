#if NET7_0_OR_GREATER
using System.Numerics;

namespace LightProto.Parser
{
[ProtoContract]
[ProtoSurrogateFor(typeof(Plane))]
public partial struct PlaneProtoParser
{
    [ProtoMember(1, IsPacked = true)]
    internal float[] Floats { get; set; }

    public static implicit operator Plane(PlaneProtoParser protoParser)
    {
        if (protoParser.Floats is null)
        {
            return default;
        }

        if (protoParser.Floats.Length != 4)
        {
            throw new ArgumentException(
                "Input array must contain 4 elements for Plane conversion.",
                nameof(protoParser)
            );
        }
        return new Plane(
            protoParser.Floats[0],
            protoParser.Floats[1],
            protoParser.Floats[2],
            protoParser.Floats[3]
        );
    }

    public static implicit operator PlaneProtoParser(Plane value)
    {
        return new PlaneProtoParser()
        {
            Floats = new[] { value.Normal.X, value.Normal.Y, value.Normal.Z, value.D },
        };
    }
}
}
#endif

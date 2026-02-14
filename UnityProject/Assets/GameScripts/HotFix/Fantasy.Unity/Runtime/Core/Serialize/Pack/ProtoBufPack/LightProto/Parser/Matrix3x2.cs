#if NET7_0_OR_GREATER
using System.Numerics;

namespace LightProto.Parser
{
[ProtoContract]
[ProtoSurrogateFor(typeof(Matrix3x2))]
public partial struct Matrix3x2ProtoParser
{
    [ProtoMember(1, IsPacked = true)]
    internal float[] Floats { get; set; }

    public static implicit operator Matrix3x2(Matrix3x2ProtoParser protoParser)
    {
        if (protoParser.Floats is null)
        {
            return default;
        }

        if (protoParser.Floats.Length != 6)
        {
            throw new ArgumentException(
                "Input array must contain 6 elements for Matrix3x2 conversion.",
                nameof(protoParser)
            );
        }

        return new Matrix3x2(
            protoParser.Floats[0],
            protoParser.Floats[1],
            protoParser.Floats[2],
            protoParser.Floats[3],
            protoParser.Floats[4],
            protoParser.Floats[5]
        );
    }

    public static implicit operator Matrix3x2ProtoParser(Matrix3x2 value)
    {
        return new Matrix3x2ProtoParser()
        {
            Floats = new[] { value.M11, value.M12, value.M21, value.M22, value.M31, value.M32 },
        };
    }
}
}
#endif

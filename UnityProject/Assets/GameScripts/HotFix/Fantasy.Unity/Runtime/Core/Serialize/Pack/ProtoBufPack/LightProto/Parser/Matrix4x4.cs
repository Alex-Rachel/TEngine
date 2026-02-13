#if NET7_0_OR_GREATER
using System.Numerics;

namespace LightProto.Parser
{
[ProtoContract]
[ProtoSurrogateFor(typeof(Matrix4x4))]
public partial struct Matrix4x4ProtoParser
{
    [ProtoMember(1, IsPacked = true)]
    internal float[] Floats { get; set; }

    public static implicit operator Matrix4x4(Matrix4x4ProtoParser protoParser)
    {
        if (protoParser.Floats is null)
        {
            return default;
        }

        if (protoParser.Floats.Length != 16)
        {
            throw new ArgumentException(
                "Input array must contain 16 elements for Matrix4x4 conversion.",
                nameof(protoParser)
            );
        }
        return new Matrix4x4(
            protoParser.Floats[0],
            protoParser.Floats[1],
            protoParser.Floats[2],
            protoParser.Floats[3],
            protoParser.Floats[4],
            protoParser.Floats[5],
            protoParser.Floats[6],
            protoParser.Floats[7],
            protoParser.Floats[8],
            protoParser.Floats[9],
            protoParser.Floats[10],
            protoParser.Floats[11],
            protoParser.Floats[12],
            protoParser.Floats[13],
            protoParser.Floats[14],
            protoParser.Floats[15]
        );
    }

    public static implicit operator Matrix4x4ProtoParser(Matrix4x4 value)
    {
        return new Matrix4x4ProtoParser()
        {
            Floats = new[]
            {
                value.M11,
                value.M12,
                value.M13,
                value.M14,
                value.M21,
                value.M22,
                value.M23,
                value.M24,
                value.M31,
                value.M32,
                value.M33,
                value.M34,
                value.M41,
                value.M42,
                value.M43,
                value.M44,
            },
        };
    }
}
}
#endif

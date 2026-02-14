using System.Numerics;

namespace LightProto.Parser
{
    [ProtoContract]
    [ProtoSurrogateFor(typeof(Complex))]
    public partial struct ComplexProtoParser
    {
        [ProtoMember(1)]
        internal double Real { get; set; }

        [ProtoMember(2)]
        internal double Imaginary { get; set; }

        public static implicit operator Complex(ComplexProtoParser proxy)
        {
            return new Complex(proxy.Real, proxy.Imaginary);
        }

        public static implicit operator ComplexProtoParser(Complex dt)
        {
            return new ComplexProtoParser { Real = dt.Real, Imaginary = dt.Imaginary };
        }
    }
}

using System.Collections;
using System.Runtime.CompilerServices;

namespace LightProto.Parser
{
    [ProtoContract]
    [ProtoSurrogateFor(typeof(BitArray))]
    public partial struct BitArrayProtoParser
    {
        [ProtoMember(1, IsPacked = true)]
        internal bool[] Bits { get; set; }

        public static implicit operator BitArray(BitArrayProtoParser proxy)
        {
            return new BitArray(proxy.Bits ?? System.Array.Empty<bool>());
        }

        public static implicit operator BitArrayProtoParser(BitArray value)
        {
            if (value is null)
                return new BitArrayProtoParser { Bits = System.Array.Empty<bool>() };
            bool[] bits = new bool[value.Count];
            value.CopyTo(bits, 0);
            return new BitArrayProtoParser { Bits = bits };
        }
    }
}

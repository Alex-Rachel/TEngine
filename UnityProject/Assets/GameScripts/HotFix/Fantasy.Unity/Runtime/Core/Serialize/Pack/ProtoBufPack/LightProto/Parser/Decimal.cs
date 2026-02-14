using System;
using System.Runtime.InteropServices;

namespace LightProto.Parser
{
//
// message Decimal {
// uint64 lo = 1; // the first 64 bits of the underlying value
// uint32 hi = 2; // the last 32 bis of the underlying value
// uint32 signScale = 3; // the number of decimal digits (bits 1-16), and the sign (bit 0)
// }

    [ProtoContract]
    [ProtoSurrogateFor(typeof(Decimal))]
    public partial struct DecimalProtoParser
    {
        /// <summary>
        /// the first 64 bits of the underlying value
        /// </summary>
        [ProtoMember(1)]
        internal ulong Low { get; set; }

        /// <summary>
        /// the last 32 bis of the underlying value
        /// </summary>
        [ProtoMember(2)]
        internal uint High { get; set; }

        /// <summary>
        /// the number of decimal digits (bits 1-16), and the sign (bit 0)
        /// </summary>
        [ProtoMember(3)]
        internal uint SignScale { get; set; }

        public static implicit operator Decimal(DecimalProtoParser protoParser)
        {
            int lo = (int)(protoParser.Low & 0xFFFFFFFFL),
                mid = (int)((protoParser.Low >> 32) & 0xFFFFFFFFL),
                hi = (int)protoParser.High;
            bool isNeg = (protoParser.SignScale & 0x0001) == 0x0001;
            byte scale = (byte)((protoParser.SignScale & 0x01FE) >> 1);
            return new decimal(lo, mid, hi, isNeg, scale);
        }

        public static implicit operator DecimalProtoParser(Decimal value)
        {
            var dec = new DecimalAccessor(value);
            ulong a = ((ulong)dec.Mid) << 32,
                b = ((ulong)dec.Lo) & 0xFFFFFFFFL;
            ulong low = a | b;
            uint high = (uint)dec.Hi;
            uint signScale = (uint)(((dec.Flags >> 15) & 0x01FE) | ((dec.Flags >> 31) & 0x0001));
            return new DecimalProtoParser
            {
                Low = low,
                High = high,
                SignScale = signScale,
            };
        }

        /// <summary>
        /// Provides access to the inner fields of a decimal.
        /// Similar to decimal.GetBits(), but faster and avoids the int[] allocation
        /// </summary>
        [StructLayout(LayoutKind.Explicit)]
        private readonly struct DecimalAccessor
        {
            [FieldOffset(0)] public readonly int Flags;

            [FieldOffset(4)] public readonly int Hi;

            [FieldOffset(8)] public readonly int Lo;

            [FieldOffset(12)] public readonly int Mid;

            [FieldOffset(0)] public readonly decimal Decimal;

            public DecimalAccessor(decimal value)
            {
                this = default;
                Decimal = value;
            }
        }
    }
}

using System;
using System.Buffers.Binary;
using System.Globalization;

namespace LightProto.Parser
{
    public sealed class Decimal300ProtoParser : IProtoParser<Decimal>
    {
        public static IProtoReader<Decimal> ProtoReader { get; } = new Decimal300ProtoReader();
        public static IProtoWriter<Decimal> ProtoWriter { get; } = new Decimal300ProtoWriter();

        sealed class Decimal300ProtoReader : IProtoReader<Decimal>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;
            public bool IsMessage => false;

            public Decimal ParseFrom(ref ReaderContext input)
            {
                var str = input.ReadString();
                return Decimal.Parse(str);
            }
        }

        sealed class Decimal300ProtoWriter : IProtoWriter<Decimal>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;
            public bool IsMessage => false;

            public int CalculateSize(Decimal value)
            {
                return CodedOutputStream.ComputeStringSize(value.ToString("G"));
            }

            public void WriteTo(ref WriterContext output, Decimal value)
            {
                output.WriteString(value.ToString("G"));
            }
        }
    }
}

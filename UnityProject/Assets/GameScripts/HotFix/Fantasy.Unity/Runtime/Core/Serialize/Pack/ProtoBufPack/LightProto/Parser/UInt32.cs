using System;

namespace LightProto.Parser
{
    public sealed class UInt32ProtoParser : IProtoParser<UInt32>
    {
        public static IProtoReader<UInt32> ProtoReader { get; } = new UInt32ProtoReader();
        public static IProtoWriter<UInt32> ProtoWriter { get; } = new UInt32ProtoWriter();

        sealed class UInt32ProtoReader : IProtoReader<UInt32>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.Varint;
            public bool IsMessage => false;

            public UInt32 ParseFrom(ref ReaderContext input)
            {
                return input.ReadUInt32();
            }
        }

        sealed class UInt32ProtoWriter : IProtoWriter<UInt32>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.Varint;
            public bool IsMessage => false;

            public int CalculateSize(UInt32 value)
            {
                return CodedOutputStream.ComputeUInt32Size(value);
            }

            public void WriteTo(ref WriterContext output, UInt32 value)
            {
                output.WriteUInt32(value);
            }
        }
    }
}

using System.Buffers.Binary;
using System.Globalization;

namespace LightProto.Parser
{
    public sealed class CultureInfoProtoParser : IProtoParser<CultureInfo>
    {
        public static IProtoReader<CultureInfo> ProtoReader { get; } = new CultureInfoProtoReader();
        public static IProtoWriter<CultureInfo> ProtoWriter { get; } = new CultureInfoProtoWriter();

        sealed class CultureInfoProtoReader : IProtoReader<CultureInfo>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;
            public bool IsMessage => false;

            public CultureInfo ParseFrom(ref ReaderContext input)
            {
                var name = input.ReadString();
                return new CultureInfo(name);
            }
        }

        sealed class CultureInfoProtoWriter : IProtoWriter<CultureInfo>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;
            public bool IsMessage => false;

            public int CalculateSize(CultureInfo value)
            {
                return CodedOutputStream.ComputeStringSize(value.Name);
            }

            public void WriteTo(ref WriterContext output, CultureInfo value)
            {
                output.WriteString(value.Name);
            }
        }
    }
}

using System;

namespace LightProto.Parser
{
    public sealed class Fixed32ProtoParser : IProtoParser<UInt32>
    {
        public static IProtoReader<UInt32> ProtoReader { get; } = new Fixed32ProtoReader();
        public static IProtoWriter<UInt32> ProtoWriter { get; } = new Fixed32ProtoWriter();

        sealed class Fixed32ProtoReader : IProtoReader<UInt32>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.Fixed32;
            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public UInt32 ParseFrom(ref ReaderContext input)
            {
                return input.ReadFixed32();
            }
        }

        sealed class Fixed32ProtoWriter : IProtoWriter<UInt32>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.Fixed32;
            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public int CalculateSize(UInt32 value)
            {
                return CodedOutputStream.ComputeFixed32Size(value);
            }

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public void WriteTo(ref WriterContext output, UInt32 value)
            {
                output.WriteFixed32(value);
            }
        }
    }
}

using System;

namespace LightProto.Parser
{
    public sealed class Fixed64ProtoParser : IProtoParser<UInt64>
    {
        public static IProtoReader<UInt64> ProtoReader { get; } = new Fixed64ProtoReader();
        public static IProtoWriter<UInt64> ProtoWriter { get; } = new Fixed64ProtoWriter();

        sealed class Fixed64ProtoReader : IProtoReader<UInt64>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.Fixed64;
            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public UInt64 ParseFrom(ref ReaderContext input)
            {
                return input.ReadFixed64();
            }
        }

        sealed class Fixed64ProtoWriter : IProtoWriter<UInt64>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.Fixed64;
            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public int CalculateSize(UInt64 value)
            {
                return CodedOutputStream.ComputeFixed64Size(value);
            }

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public void WriteTo(ref WriterContext output, UInt64 value)
            {
                output.WriteFixed64(value);
            }
        }
    }
}

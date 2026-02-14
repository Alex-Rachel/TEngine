using System;

namespace LightProto.Parser
{
    public sealed class SFixed64ProtoParser : IProtoParser<Int64>
    {
        public static IProtoReader<Int64> ProtoReader { get; } = new SFixed64ProtoReader();
        public static IProtoWriter<Int64> ProtoWriter { get; } = new SFixed64ProtoWriter();

        sealed class SFixed64ProtoReader : IProtoReader<Int64>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.Fixed64;
            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public Int64 ParseFrom(ref ReaderContext input)
            {
                return input.ReadSFixed64();
            }
        }

        sealed class SFixed64ProtoWriter : IProtoWriter<Int64>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.Fixed64;
            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public int CalculateSize(Int64 value)
            {
                return CodedOutputStream.ComputeSFixed64Size(value);
            }

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public void WriteTo(ref WriterContext output, Int64 value)
            {
                output.WriteSFixed64(value);
            }
        }
    }
}

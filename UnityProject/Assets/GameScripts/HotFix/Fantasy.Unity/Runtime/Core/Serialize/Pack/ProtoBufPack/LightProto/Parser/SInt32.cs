using System;

namespace LightProto.Parser
{
    public sealed class SInt32ProtoParser : IProtoParser<Int32>
    {
        public static IProtoReader<Int32> ProtoReader { get; } = new SInt32ProtoReader();
        public static IProtoWriter<Int32> ProtoWriter { get; } = new SInt32ProtoWriter();

        sealed class SInt32ProtoReader : IProtoReader<Int32>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.Varint;
            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public Int32 ParseFrom(ref ReaderContext input)
            {
                return input.ReadSInt32();
            }
        }

        sealed class SInt32ProtoWriter : IProtoWriter<Int32>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.Varint;
            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public int CalculateSize(Int32 value)
            {
                return CodedOutputStream.ComputeSInt32Size(value);
            }

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public void WriteTo(ref WriterContext output, Int32 value)
            {
                output.WriteSInt32(value);
            }
        }
    }
}

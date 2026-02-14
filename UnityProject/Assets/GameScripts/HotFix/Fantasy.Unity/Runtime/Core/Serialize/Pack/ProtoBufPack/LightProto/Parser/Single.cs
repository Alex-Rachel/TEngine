using System;

namespace LightProto.Parser
{
    public sealed class SingleProtoParser : IProtoParser<Single>
    {
        public static IProtoReader<Single> ProtoReader { get; } = new SingleProtoReader();
        public static IProtoWriter<Single> ProtoWriter { get; } = new SingleProtoWriter();

        sealed class SingleProtoReader : IProtoReader<Single>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.Fixed32;
            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public Single ParseFrom(ref ReaderContext input)
            {
                return input.ReadFloat();
            }
        }

        sealed class SingleProtoWriter : IProtoWriter<Single>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.Fixed32;
            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public int CalculateSize(Single value)
            {
                return CodedOutputStream.ComputeFloatSize(value);
            }

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public void WriteTo(ref WriterContext output, Single value)
            {
                output.WriteFloat(value);
            }
        }
    }
}

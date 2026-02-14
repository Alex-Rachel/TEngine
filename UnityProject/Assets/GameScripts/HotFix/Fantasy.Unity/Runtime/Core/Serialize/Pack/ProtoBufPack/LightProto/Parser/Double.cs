using System;

namespace LightProto.Parser
{
    public sealed class DoubleProtoParser : IProtoParser<Double>
    {
        public static IProtoReader<Double> ProtoReader { get; } = new DoubleProtoReader();
        public static IProtoWriter<Double> ProtoWriter { get; } = new DoubleProtoWriter();

        sealed class DoubleProtoReader : IProtoReader<Double>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.Fixed64;

            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public Double ParseFrom(ref ReaderContext input)
            {
                return input.ReadDouble();
            }
        }

        sealed class DoubleProtoWriter : IProtoWriter<Double>
        {
            public WireFormat.WireType WireType => WireFormat.WireType.Fixed64;

            public bool IsMessage => false;

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public int CalculateSize(Double value)
            {
                return CodedOutputStream.ComputeDoubleSize(value);
            }

            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
            )]
            public void WriteTo(ref WriterContext output, Double value)
            {
                output.WriteDouble(value);
            }
        }
    }
}

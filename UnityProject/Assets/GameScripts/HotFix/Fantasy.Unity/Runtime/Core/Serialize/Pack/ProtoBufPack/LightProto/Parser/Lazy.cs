using System;
using System.Diagnostics.CodeAnalysis;

namespace LightProto.Parser
{
    public sealed class LazyProtoReader<
#if NET7_0_OR_GREATER
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
#endif
        T
    > : IProtoReader<Lazy<T>>
    {
        public bool IsMessage => ValueReader.IsMessage;
        public IProtoReader<T> ValueReader { get; }
        public WireFormat.WireType WireType => ValueReader.WireType;

        public LazyProtoReader(IProtoReader<T> valueReader)
        {
            ValueReader = valueReader;
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
        )]
        public Lazy<T> ParseFrom(ref ReaderContext input)
        {
            var t = ValueReader.ParseMessageFrom(ref input);
            return new Lazy<T>(() => t);
        }
    }

    public sealed class LazyProtoWriter<
#if NET7_0_OR_GREATER
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
#endif
        T
    > : IProtoWriter<Lazy<T>>
    {
        public IProtoWriter<T> ValueWriter { get; }
        public WireFormat.WireType WireType => ValueWriter.WireType;
        public bool IsMessage => ValueWriter.IsMessage;

        public LazyProtoWriter(IProtoWriter<T> valueWriter)
        {
            ValueWriter = valueWriter;
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
        )]
        public int CalculateSize(Lazy<T> value)
        {
            return ValueWriter.CalculateMessageSize(value.Value);
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
        )]
        public void WriteTo(ref WriterContext output, Lazy<T> value)
        {
            ValueWriter.WriteMessageTo(ref output, value.Value);
        }
    }
}

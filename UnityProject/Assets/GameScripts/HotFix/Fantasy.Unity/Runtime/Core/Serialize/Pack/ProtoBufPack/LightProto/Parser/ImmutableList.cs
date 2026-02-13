using System.Collections.Immutable;

namespace LightProto.Parser
{
    public sealed class ImmutableListProtoWriter<T> : IEnumerableProtoWriter<ImmutableList<T>, T>
    {
        public ImmutableListProtoWriter(IProtoWriter<T> itemWriter, uint tag, int itemFixedSize)
            : base(itemWriter, tag, static collection => collection.Count, itemFixedSize)
        {
        }
    }

    public sealed class ImmutableListProtoReader<TItem> : ICollectionReader<ImmutableList<TItem>, TItem>
    {
        private readonly ArrayProtoReader<TItem> _arrayReader;
        public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;
        public bool IsMessage => false;

        public ImmutableList<TItem> ParseFrom(ref ReaderContext input)
        {
            return ImmutableList.Create(_arrayReader.ParseFrom(ref input));
        }

        public WireFormat.WireType ItemWireType => ItemReader.WireType;
        public IProtoReader<TItem> ItemReader { get; }
        public int ItemFixedSize { get; }

        public ImmutableListProtoReader(IProtoReader<TItem> itemReader, int itemFixedSize)
        {
            _arrayReader = new ArrayProtoReader<TItem>(itemReader, itemFixedSize);
            ItemReader = itemReader;
            ItemFixedSize = itemFixedSize;
        }

        public ImmutableListProtoReader(IProtoReader<TItem> itemReader, uint tag, int itemFixedSize)
            : this(itemReader, itemFixedSize)
        {
        }

        public ImmutableList<TItem> Empty => ImmutableList<TItem>.Empty;
    }
}

using System.Collections.ObjectModel;
using LightProto.Internal;

namespace LightProto.Parser
{
    public sealed class ReadOnlyCollectionProtoWriter<T>
        : IEnumerableProtoWriter<ReadOnlyCollection<T>, T>
    {
        public ReadOnlyCollectionProtoWriter(IProtoWriter<T> itemWriter, uint tag, int itemFixedSize)
            : base(itemWriter, tag, static collection => collection.Count, itemFixedSize)
        {
        }
    }

    public sealed class ReadOnlyCollectionProtoReader<TItem>
        : ICollectionReader<ReadOnlyCollection<TItem>, TItem>
    {
        private readonly ListProtoReader<TItem> _listReader;
        public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;
        public bool IsMessage => false;

        public ReadOnlyCollection<TItem> ParseFrom(ref ReaderContext input)
        {
            return _listReader.ParseFrom(ref input).AsReadOnly();
        }

        public WireFormat.WireType ItemWireType => ItemReader.WireType;
        public IProtoReader<TItem> ItemReader { get; }
        public int ItemFixedSize { get; }

        public ReadOnlyCollectionProtoReader(IProtoReader<TItem> itemReader, int itemFixedSize)
        {
            _listReader = new ListProtoReader<TItem>(itemReader, itemFixedSize);
            ItemReader = itemReader;
            ItemFixedSize = itemFixedSize;
        }

        public ReadOnlyCollectionProtoReader(
            IProtoReader<TItem> itemReader,
            uint tag,
            int itemFixedSize
        )
            : this(itemReader, itemFixedSize)
        {
        }

#if NET7_0_OR_GREATER
    public ReadOnlyCollection<TItem> Empty => ReadOnlyCollection<TItem>.Empty;
#else
        public ReadOnlyCollection<TItem> Empty => s_empty;
        static ReadOnlyCollection<TItem> s_empty { get; } = new(new Collection<TItem>());
#endif
    }
}

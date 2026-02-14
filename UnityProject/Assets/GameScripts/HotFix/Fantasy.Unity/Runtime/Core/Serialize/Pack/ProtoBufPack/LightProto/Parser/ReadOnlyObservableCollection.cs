using System.Collections.ObjectModel;
using LightProto.Internal;

namespace LightProto.Parser
{
    public sealed class ReadOnlyObservableCollectionProtoWriter<T>
        : IEnumerableProtoWriter<ReadOnlyObservableCollection<T>, T>
    {
        public ReadOnlyObservableCollectionProtoWriter(
            IProtoWriter<T> itemWriter,
            uint tag,
            int itemFixedSize
        )
            : base(itemWriter, tag, static collection => collection.Count, itemFixedSize)
        {
        }
    }

    public sealed class ReadOnlyObservableCollectionProtoReader<TItem>
        : ICollectionReader<ReadOnlyObservableCollection<TItem>, TItem>
    {
        private readonly ObservableCollectionProtoReader<TItem> _observableCollectionProtoReader;
        public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;
        public bool IsMessage => false;

        public ReadOnlyObservableCollection<TItem> ParseFrom(ref ReaderContext input)
        {
            return new ReadOnlyObservableCollection<TItem>(
                _observableCollectionProtoReader.ParseFrom(ref input)
            );
        }

        public WireFormat.WireType ItemWireType => ItemReader.WireType;
        public IProtoReader<TItem> ItemReader { get; }
        public int ItemFixedSize { get; }

        public ReadOnlyObservableCollectionProtoReader(
            IProtoReader<TItem> itemReader,
            uint tag,
            int itemFixedSize
        )
            : this(itemReader, itemFixedSize)
        {
        }

        public ReadOnlyObservableCollectionProtoReader(
            IProtoReader<TItem> itemReader,
            int itemFixedSize
        )
        {
            _observableCollectionProtoReader = new ObservableCollectionProtoReader<TItem>(
                itemReader,
                itemFixedSize
            );
            ItemReader = itemReader;
            ItemFixedSize = itemFixedSize;
        }

#if NET7_0_OR_GREATER
    public ReadOnlyObservableCollection<TItem> Empty => ReadOnlyObservableCollection<TItem>.Empty;
#else
        public ReadOnlyObservableCollection<TItem> Empty => s_empty;
        static ReadOnlyObservableCollection<TItem> s_empty { get; } =
            new(new ObservableCollection<TItem>());
#endif
    }
}

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LightProto.Parser
{
    public sealed class ICollectionProtoWriter<T> : IEnumerableProtoWriter<ICollection<T>, T>
    {
        public ICollectionProtoWriter(IProtoWriter<T> itemWriter, uint tag, int itemFixedSize)
            : base(itemWriter, tag, static collection => collection.Count, itemFixedSize) { }
    }

    public sealed class CollectionProtoWriter<T> : IEnumerableProtoWriter<Collection<T>, T>
    {
        public CollectionProtoWriter(IProtoWriter<T> itemWriter, uint tag, int itemFixedSize)
            : base(itemWriter, tag, static collection => collection.Count, itemFixedSize) { }
    }

    public sealed class CollectionProtoReader<T> : IEnumerableProtoReader<Collection<T>, T>
    {
        public CollectionProtoReader(IProtoReader<T> itemReader, uint tag, int itemFixedSize)
            : base(
                itemReader,
                static (capacity) => new Collection<T>(),
                static (collection, item) =>
                {
                    collection.Add(item);
                    return collection;
                },
                itemFixedSize
            ) { }
    }
}

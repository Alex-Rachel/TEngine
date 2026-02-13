using System.Collections.Concurrent;

namespace LightProto.Parser
{
    public sealed class BlockingCollectionProtoWriter<T>
        : IEnumerableProtoWriter<BlockingCollection<T>, T>
    {
        public BlockingCollectionProtoWriter(IProtoWriter<T> itemWriter, uint tag, int itemFixedSize)
            : base(itemWriter, tag, static collection => collection.Count, itemFixedSize) { }
    }

    public sealed class BlockingCollectionProtoReader<T>
        : IEnumerableProtoReader<BlockingCollection<T>, T>
    {
        public BlockingCollectionProtoReader(IProtoReader<T> itemReader, uint tag, int itemFixedSize)
            : base(
                itemReader,
                static (capacity) => new BlockingCollection<T>(),
                static (collection, item) =>
                {
                    collection.Add(item);
                    return collection;
                },
                itemFixedSize
            ) { }
    }
}

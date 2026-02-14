using System.Collections.Generic;

namespace LightProto.Parser
{
    public sealed class SortedSetProtoWriter<T> : IEnumerableProtoWriter<SortedSet<T>, T>
    {
        public SortedSetProtoWriter(IProtoWriter<T> itemWriter, uint tag, int itemFixedSize)
            : base(itemWriter, tag, static collection => collection.Count, itemFixedSize) { }
    }

    public sealed class SortedSetProtoReader<T> : IEnumerableProtoReader<SortedSet<T>, T>
    {
        public SortedSetProtoReader(IProtoReader<T> itemReader, uint tag, int itemFixedSize)
            : base(
                itemReader,
                static (size) => new SortedSet<T>(),
                static (collection, item) =>
                {
                    collection.Add(item);
                    return collection;
                },
                itemFixedSize
            ) { }
    }
}

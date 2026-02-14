using System.Collections.Generic;

namespace LightProto.Parser
{
    public sealed class HashSetProtoWriter<T> : IEnumerableProtoWriter<HashSet<T>, T>
    {
        public HashSetProtoWriter(IProtoWriter<T> itemWriter, uint tag, int itemFixedSize)
            : base(itemWriter, tag, static collection => collection.Count, itemFixedSize) { }
    }

    public sealed class HashSetProtoReader<T> : IEnumerableProtoReader<HashSet<T>, T>
    {
        public HashSetProtoReader(IProtoReader<T> itemReader, uint tag, int itemFixedSize)
            : base(
                itemReader,
#if NET7_0_OR_GREATER
            static (size) => new HashSet<T>(size),
#else
                static (size) => new HashSet<T>(),
#endif
                static (collection, item) =>
                {
                    collection.Add(item);
                    return collection;
                },
                itemFixedSize
            ) { }
    }
}

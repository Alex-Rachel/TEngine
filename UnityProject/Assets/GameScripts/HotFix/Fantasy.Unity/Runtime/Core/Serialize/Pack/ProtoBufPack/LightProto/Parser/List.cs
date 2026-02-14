using System.Collections.Generic;

namespace LightProto.Parser
{
    public sealed class ListProtoWriter<T> : IEnumerableProtoWriter<List<T>, T>
    {
        public ListProtoWriter(IProtoWriter<T> itemWriter, uint tag, int itemFixedSize)
            : base(itemWriter, tag, static collection => collection.Count, itemFixedSize) { }
    }

    public sealed class ListProtoReader<T> : IEnumerableProtoReader<List<T>, T>
    {
        public ListProtoReader(IProtoReader<T> itemReader, uint tag, int itemFixedSize)
            : this(itemReader, itemFixedSize) { }

        public ListProtoReader(IProtoReader<T> itemReader, int itemFixedSize)
            : base(
                itemReader,
                static capacity => new List<T>(capacity),
                static (collection, item) =>
                {
                    collection.Add(item);
                    return collection;
                },
                itemFixedSize
            ) { }
    }
}

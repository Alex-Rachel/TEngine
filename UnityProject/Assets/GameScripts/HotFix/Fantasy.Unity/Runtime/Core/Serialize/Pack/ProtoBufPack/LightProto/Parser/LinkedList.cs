using System.Collections.Generic;

namespace LightProto.Parser
{
    public sealed class LinkedListProtoWriter<T> : IEnumerableProtoWriter<LinkedList<T>, T>
    {
        public LinkedListProtoWriter(IProtoWriter<T> itemWriter, uint tag, int itemFixedSize)
            : base(itemWriter, tag, static collection => collection.Count, itemFixedSize) { }
    }

    public sealed class LinkedListProtoReader<T> : IEnumerableProtoReader<LinkedList<T>, T>
    {
        public LinkedListProtoReader(IProtoReader<T> itemReader, uint tag, int itemFixedSize)
            : base(
                itemReader,
                static (capacity) => new LinkedList<T>(),
                static (collection, item) =>
                {
                    collection.AddLast(item);
                    return collection;
                },
                itemFixedSize
            ) { }
    }
}

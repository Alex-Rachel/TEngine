using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using LightProto.Parser;

namespace LightProto
{
    public static partial class Serializer
    {
        public static IProtoWriter<ICollection<T>> GetCollectionWriter<T>(this IProtoWriter<T> writer)
        {
            uint tag = WireFormat.MakeTag(1, writer.WireType);
            return new IEnumerableProtoWriter<ICollection<T>, T>(
                writer,
                tag,
                (collection) => collection.Count,
                itemFixedSize: 0
            );
        }

        public static ArrayProtoReader<TItem> GetArrayReader<TItem>(this IProtoReader<TItem> reader)
        {
            return new ArrayProtoReader<TItem>(reader, itemFixedSize: 0);
        }

        public static IProtoReader<List<TItem>> GetListReader<TItem>(this IProtoReader<TItem> reader)
        {
            return new ListProtoReader<TItem>(reader, 0, 0);
        }

        public static IProtoReader<HashSet<TItem>> GetHashSetReader<TItem>(
            this IProtoReader<TItem> reader
        )
        {
            return new HashSetProtoReader<TItem>(reader, 0, 0);
        }

        public static IProtoReader<ConcurrentBag<TItem>> GetConcurrentBagReader<TItem>(
            this IProtoReader<TItem> reader
        )
        {
            return new ConcurrentBagProtoReader<TItem>(reader, 0, 0);
        }

        public static IProtoReader<ConcurrentQueue<TItem>> GetConcurrentQueueReader<TItem>(
            this IProtoReader<TItem> reader
        )
        {
            return new ConcurrentQueueProtoReader<TItem>(reader, 0, 0);
        }

        public static IProtoReader<LinkedList<TItem>> GetLinkedListReader<TItem>(
            this IProtoReader<TItem> reader
        )
        {
            return new LinkedListProtoReader<TItem>(reader, 0, 0);
        }

        public static IProtoReader<BlockingCollection<TItem>> GetBlockingCollectionReader<TItem>(
            this IProtoReader<TItem> reader
        )
        {
            return new BlockingCollectionProtoReader<TItem>(reader, 0, 0);
        }

        public static IProtoReader<Collection<TItem>> GetCollectionReader<TItem>(
            this IProtoReader<TItem> reader
        )
        {
            return new CollectionProtoReader<TItem>(reader, 0, 0);
        }

        public static IProtoReader<ReadOnlyCollection<TItem>> GetReadOnlyCollectionReader<TItem>(
            this IProtoReader<TItem> reader
        )
        {
            return new ReadOnlyCollectionProtoReader<TItem>(reader, itemFixedSize: 0);
        }

        public static IProtoReader<ObservableCollection<TItem>> GetObservableCollectionReader<TItem>(
            this IProtoReader<TItem> reader
        )
        {
            return new ObservableCollectionProtoReader<TItem>(reader, 0, 0);
        }

        public static IProtoReader<
            ReadOnlyObservableCollection<TItem>
        > GetReadOnlyObservableCollectionReader<TItem>(this IProtoReader<TItem> reader)
        {
            return new ReadOnlyObservableCollectionProtoReader<TItem>(reader, itemFixedSize: 0);
        }
    }
}

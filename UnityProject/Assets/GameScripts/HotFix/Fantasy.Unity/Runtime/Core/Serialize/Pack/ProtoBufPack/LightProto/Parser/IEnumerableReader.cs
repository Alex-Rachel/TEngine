using System;
using System.Collections.Generic;
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

namespace LightProto.Parser
{
    public interface ICollectionReader
    {
        public WireFormat.WireType ItemWireType { get; }
    }

    public interface ICollectionReader<out TCollection> : ICollectionReader
    {
        public TCollection Empty { get; }
    }

    public interface ICollectionItemReader<out TItem> : ICollectionReader
    {
        public IProtoReader<TItem> ItemReader { get; }
    }

    public interface ICollectionReader<out TCollection, out TItem>
        : IProtoReader<TCollection>,
            ICollectionReader<TCollection>,
            ICollectionItemReader<TItem>
    {
    }

    public class IEnumerableProtoReader<TCollection, TItem> : ICollectionReader<TCollection, TItem>
        where TCollection : IEnumerable<TItem>
    {
        public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;
        public bool IsMessage => false;
        private readonly Func<TCollection, TCollection>? _completeAction;
        public IProtoReader<TItem> ItemReader { get; }
        public Func<int, TCollection> CreateWithCapacity { get; }
        public TCollection Empty => CreateWithCapacity(0);
        public Func<TCollection, TItem, TCollection> AddItem { get; }
        public int ItemFixedSize { get; }
        public WireFormat.WireType ItemWireType => ItemReader.WireType;

        public IEnumerableProtoReader(
            IProtoReader<TItem> itemReader,
            Func<int, TCollection> createWithCapacity,
            Func<TCollection, TItem, TCollection> addItem,
            int itemFixedSize,
            Func<TCollection, TCollection>? completeAction = null
        )
        {
            _completeAction = completeAction;
            ItemReader = itemReader;
            CreateWithCapacity = createWithCapacity;
            AddItem = addItem;
            ItemFixedSize = itemFixedSize;
        }

        public TCollection ParseFrom(ref ReaderContext ctx)
        {
            var tag = ctx.state.lastTag;
            var fixedSize = ItemFixedSize;
            if (
                WireFormat.GetTagWireType(tag) is WireFormat.WireType.LengthDelimited
                && PackedRepeated.Support<TItem>()
            )
            {
                var length = ctx.ReadInt64();
                if (length <= 0)
                    return CreateWithCapacity(0);
                var oldLimit = SegmentedBufferHelper.PushLimit(ref ctx.state, length);

                try
                {
                    // If the content is fixed size then we can calculate the length
                    // of the repeated field and pre-initialize the underlying collection.
                    //
                    // Check that the supplied length doesn't exceed the underlying buffer.
                    // That prevents a malicious length from initializing a very large collection.
                    if (
                        fixedSize > 0
                        && length % fixedSize == 0
                        && ParsingPrimitives.IsDataAvailable(ref ctx.state, length)
                    )
                    {
                        var count = length / fixedSize;
                        var collection = CreateWithCapacity((int)count);
                        // if littleEndian treat array as bytes and directly copy from buffer for improved performance
                        // if (
                        //     collection is List<TItem> list
                        //     && BitConverter.IsLittleEndian
                        //     && Marshal.SizeOf<TItem>() == fixedSize
                        // )
                        // {
                        //     var itemSpan = CollectionsMarshal.AsSpan(list);
                        //
                        //     var byteSpan = MemoryMarshal.CreateSpan(
                        //         ref Unsafe.As<TItem, byte>(ref MemoryMarshal.GetReference(itemSpan)),
                        //         checked(itemSpan.Length * fixedSize)
                        //     );
                        //     ParsingPrimitives.ReadPackedFieldLittleEndian(
                        //         ref ctx.buffer,
                        //         ref ctx.state,
                        //         length,
                        //         byteSpan
                        //     );
                        //     CollectionsMarshal.SetCount(list, count);
                        // }
                        // else
                        {
                            while (!SegmentedBufferHelper.IsReachedLimit(ref ctx.state))
                            {
                                // Only FieldCodecs with a fixed size can reach here, and they are all known
                                // types that don't allow the user to specify a custom reader action.
                                // reader action will never return null.
                                collection = AddItem(collection, ItemReader.ParseMessageFrom(ref ctx));
                            }
                        }

                        return collection;
                    }
                    else
                    {
                        var collection = CreateWithCapacity(4);
                        // Content is variable size so add until we reach the limit.
                        while (!SegmentedBufferHelper.IsReachedLimit(ref ctx.state))
                        {
                            collection = AddItem(collection, ItemReader.ParseMessageFrom(ref ctx));
                        }

                        return collection;
                    }
                }
                finally
                {
                    SegmentedBufferHelper.PopLimit(ref ctx.state, oldLimit);
                }
            }
            else
            {
                // Not packed... (possibly not packable)
                var collection = CreateWithCapacity(4);
                do
                {
                    collection = AddItem(collection, ItemReader.ParseMessageFrom(ref ctx));
                } while (ParsingPrimitives.MaybeConsumeTag(ref ctx.buffer, ref ctx.state, tag));

                return _completeAction is null ? collection : _completeAction.Invoke(collection);
            }
        }
    }
}

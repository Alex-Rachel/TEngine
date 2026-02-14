namespace LightProto.Parser
{
    struct MessageWrapper<T>
    {
        public struct ProtoWriter : IProtoWriter<T>
        {
            private readonly uint tag;
            public bool IsMessage => true;
            public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;
            private readonly IProtoWriter<T> ItemWriter;

            public static ProtoWriter From(IProtoWriter<T> itemWriter, int fieldNumber = 1)
            {
                uint tag = WireFormat.MakeTag(fieldNumber, itemWriter.WireType);
                return new ProtoWriter(tag, itemWriter);
            }

            ProtoWriter(uint tag, IProtoWriter<T> itemWriter)
            {
                this.tag = tag;
                ItemWriter = itemWriter;
            }

            public void WriteTo(ref WriterContext output, T value)
            {
                if (ItemWriter is not ICollectionWriter)
                {
                    output.WriteTag(tag);
                }

                ItemWriter.WriteTo(ref output, value);
            }

            public int CalculateSize(T value)
            {
                int size = 0;
                if (ItemWriter is not ICollectionWriter)
                {
                    size += CodedOutputStream.ComputeRawVarint32Size(tag);
                }

                size += ItemWriter.CalculateSize(value);
                return size;
            }
        }

        public struct ProtoReader : IProtoReader<T>
        {
            private readonly uint _tag;
            private readonly uint? _tag2;
            public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;

            public static ProtoReader From(IProtoReader<T> itemReader)
            {
                uint tag = WireFormat.MakeTag(1, itemReader.WireType);
                uint? tag2 = null;
                if (itemReader is ICollectionReader collectionWriter)
                {
                    tag2 = WireFormat.MakeTag(1, collectionWriter.ItemWireType);
                }

                return new ProtoReader(tag, tag2, itemReader);
            }

            ProtoReader(uint tag, uint? tag2, IProtoReader<T> itemReader)
            {
                _tag = tag;
                _tag2 = tag2;
                _itemReader = itemReader;
            }

            public bool IsMessage => true;
            private readonly IProtoReader<T> _itemReader;

            public T ParseFrom(ref ReaderContext input)
            {
                T value = default(T)!;
                uint tag;
                while ((tag = input.ReadTag()) != 0)
                {
                    if ((tag & 7) == 4)
                    {
                        break;
                    }

                    if (tag == _tag || tag == _tag2)
                    {
                        value = _itemReader.ParseFrom(ref input);
                    }
                }

                if (value is null && _itemReader is ICollectionReader<T> collectionReader)
                {
                    return collectionReader.Empty;
                }

                return value;
            }
        }
    }
}

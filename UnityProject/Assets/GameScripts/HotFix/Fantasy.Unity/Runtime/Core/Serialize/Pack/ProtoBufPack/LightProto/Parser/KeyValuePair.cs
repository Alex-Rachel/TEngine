using System.Collections.Generic;

namespace LightProto.Parser
{
    public class KeyValuePairProtoReader<TKey, TValue> : IProtoReader<KeyValuePair<TKey, TValue>>
        where TKey : notnull
    {
        public bool IsMessage => true;
        public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;

        private readonly IProtoReader<TKey> _keyReader;
        private readonly IProtoReader<TValue> _valueReader;
        private readonly uint _keyTag;
        private readonly uint _keyTag2;
        private readonly uint _valueTag;
        private readonly uint _valueTag2;

        public KeyValuePairProtoReader(IProtoReader<TKey> keyReader, IProtoReader<TValue> valueReader)
        {
            _keyReader = keyReader;
            _valueReader = valueReader;
            if (keyReader is ICollectionReader keyCollectionReader)
            {
                _keyTag = WireFormat.MakeTag(1, keyCollectionReader.ItemWireType);
                _keyTag2 = WireFormat.MakeTag(1, WireFormat.WireType.LengthDelimited);
            }
            else
            {
                _keyTag = WireFormat.MakeTag(1, keyReader.WireType);
                ;
                _keyTag2 = _keyTag;
            }

            if (valueReader is ICollectionReader valueCollectionReader)
            {
                _valueTag = WireFormat.MakeTag(2, valueCollectionReader.ItemWireType);
                _valueTag2 = WireFormat.MakeTag(2, WireFormat.WireType.LengthDelimited);
            }
            else
            {
                _valueTag = WireFormat.MakeTag(2, valueReader.WireType);
                ;
                _valueTag2 = _valueTag;
            }
        }

        public KeyValuePair<TKey, TValue> ParseFrom(ref ReaderContext ctx)
        {
            TKey key = default!;
            TValue value = default!;
            uint tag;
            while ((tag = ctx.ReadTag()) != 0)
            {
                if ((tag & 7) == 4)
                {
                    break;
                }

                if (tag == _keyTag || tag == _keyTag2)
                {
                    if (_keyReader is ICollectionReader)
                    {
                        key = _keyReader.ParseFrom(ref ctx);
                    }
                    else
                    {
                        key = _keyReader.ParseMessageFrom(ref ctx);
                    }
                }
                else if (tag == _valueTag || tag == _valueTag2)
                {
                    if (_valueReader is ICollectionReader)
                    {
                        value = _valueReader.ParseFrom(ref ctx);
                    }
                    else
                    {
                        value = _valueReader.ParseMessageFrom(ref ctx);
                    }
                }
                else
                {
                    continue;
                }
            }

            return new KeyValuePair<TKey, TValue>(key, value);
        }
    }

    public class KeyValuePairProtoWriter<TKey, TValue> : IProtoWriter<KeyValuePair<TKey, TValue>>
        where TKey : notnull
    {
        public bool IsMessage => true;
        public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;

        public int CalculateSize(KeyValuePair<TKey, TValue> value)
        {
            int size = 0;
            if (_keyWriter is ICollectionWriter)
            {
                size += _keyWriter.CalculateSize(value.Key);
            }
            else
            {
                size += CodedOutputStream.ComputeRawVarint32Size(_keyTag);
                size += _keyWriter.CalculateMessageSize(value.Key);
            }

            if (_valueWriter is ICollectionWriter)
            {
                size += _valueWriter.CalculateSize(value.Value);
            }
            else
            {
                size += CodedOutputStream.ComputeRawVarint32Size(_valueTag);
                size += _valueWriter.CalculateMessageSize(value.Value);
            }

            return size;
        }

        public void WriteTo(ref WriterContext output, KeyValuePair<TKey, TValue> pair)
        {
            if (_keyWriter is ICollectionWriter)
            {
                _keyWriter.WriteTo(ref output, pair.Key);
            }
            else
            {
                output.WriteTag(_keyTag);
                _keyWriter.WriteMessageTo(ref output, pair.Key);
            }

            if (_valueWriter is ICollectionWriter)
            {
                _valueWriter.WriteTo(ref output, pair.Value);
            }
            else
            {
                output.WriteTag(_valueTag);
                _valueWriter.WriteMessageTo(ref output, pair.Value);
            }
        }

        private readonly IProtoWriter<TKey> _keyWriter;
        private readonly IProtoWriter<TValue> _valueWriter;
        private readonly uint _keyTag;
        private readonly uint _valueTag;

        public KeyValuePairProtoWriter(IProtoWriter<TKey> keyWriter, IProtoWriter<TValue> valueWriter)
        {
            _keyWriter = keyWriter;
            _valueWriter = valueWriter;
            _keyTag = WireFormat.MakeTag(1, keyWriter.WireType);
            _valueTag = WireFormat.MakeTag(2, valueWriter.WireType);
        }
    }
}

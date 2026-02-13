using System.Collections.Generic;
using System.Collections.Immutable;

namespace LightProto.Parser
{
    public sealed class ImmutableDictionaryProtoWriter<TKey, TValue>
        : IEnumerableKeyValuePairProtoWriter<ImmutableDictionary<TKey, TValue>, TKey, TValue>
        where TKey : notnull
    {
        public ImmutableDictionaryProtoWriter(
            IProtoWriter<TKey> keyWriter,
            IProtoWriter<TValue> valueWriter,
            uint tag
        )
            : base(keyWriter, valueWriter, tag, (dic) => dic.Count)
        {
        }
    }

    public sealed class ImmutableDictionaryProtoReader<TKey, TValue>
        : ICollectionReader<ImmutableDictionary<TKey, TValue>, KeyValuePair<TKey, TValue>>
        where TKey : notnull
    {
        private readonly DictionaryProtoReader<TKey, TValue> _dictionaryReader;
        public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;
        public bool IsMessage => false;

        ImmutableDictionary<TKey, TValue> IProtoReader<ImmutableDictionary<TKey, TValue>>.ParseFrom(
            ref ReaderContext input
        ) => _dictionaryReader.ParseFrom(ref input).ToImmutableDictionary();

        public WireFormat.WireType ItemWireType => ItemReader.WireType;

        public ImmutableDictionaryProtoReader(
            IProtoReader<TKey> keyReader,
            IProtoReader<TValue> valueReader,
            uint tag
        )
        {
            _dictionaryReader = new DictionaryProtoReader<TKey, TValue>(keyReader, valueReader, tag);
        }

        public ImmutableDictionary<TKey, TValue> Empty => ImmutableDictionary<TKey, TValue>.Empty;
        public IProtoReader<KeyValuePair<TKey, TValue>> ItemReader => _dictionaryReader.ItemReader;
    }
}

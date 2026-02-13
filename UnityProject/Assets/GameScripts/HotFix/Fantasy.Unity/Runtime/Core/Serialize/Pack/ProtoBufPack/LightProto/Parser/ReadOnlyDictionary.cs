using System.Collections.Generic;
using System.Collections.ObjectModel;
using LightProto.Internal;

namespace LightProto.Parser
{
    public sealed class IReadOnlyDictionaryProtoWriter<TKey, TValue>
        : IEnumerableKeyValuePairProtoWriter<IReadOnlyDictionary<TKey, TValue>, TKey, TValue>
        where TKey : notnull
    {
        public IReadOnlyDictionaryProtoWriter(
            IProtoWriter<TKey> keyWriter,
            IProtoWriter<TValue> valueWriter,
            uint tag
        )
            : base(keyWriter, valueWriter, tag, static (dic) => dic.Count)
        {
        }
    }

    public sealed class ReadOnlyDictionaryProtoWriter<TKey, TValue>
        : IEnumerableKeyValuePairProtoWriter<ReadOnlyDictionary<TKey, TValue>, TKey, TValue>
        where TKey : notnull
    {
        public ReadOnlyDictionaryProtoWriter(
            IProtoWriter<TKey> keyWriter,
            IProtoWriter<TValue> valueWriter,
            uint tag
        )
            : base(keyWriter, valueWriter, tag, static (dic) => dic.Count)
        {
        }
    }

    public sealed class ReadOnlyDictionaryProtoReader<TKey, TValue>
        : ICollectionReader<ReadOnlyDictionary<TKey, TValue>, KeyValuePair<TKey, TValue>>
        where TKey : notnull
    {
        private readonly DictionaryProtoReader<TKey, TValue> _dictionaryReader;
        public WireFormat.WireType WireType => WireFormat.WireType.LengthDelimited;
        public bool IsMessage => false;

        ReadOnlyDictionary<TKey, TValue> IProtoReader<ReadOnlyDictionary<TKey, TValue>>.ParseFrom(
            ref ReaderContext input
        ) => new(_dictionaryReader.ParseFrom(ref input));

        public WireFormat.WireType ItemWireType => ItemReader.WireType;

        public ReadOnlyDictionaryProtoReader(
            IProtoReader<TKey> keyReader,
            IProtoReader<TValue> valueReader,
            uint tag
        )
        {
            _dictionaryReader = new DictionaryProtoReader<TKey, TValue>(keyReader, valueReader, tag);
        }

        public ReadOnlyDictionary<TKey, TValue> Empty { get; } =
            new ReadOnlyDictionary<TKey, TValue>(new Dictionary<TKey, TValue>());

        public IProtoReader<KeyValuePair<TKey, TValue>> ItemReader => _dictionaryReader.ItemReader;
    }
}

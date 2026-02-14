using System.Collections.Concurrent;
#pragma warning disable 1591, 0612, 3021, 8981, CS9035

namespace LightProto.Parser
{
    public sealed class ConcurrentDictionaryProtoReader<TKey, TValue>
        : IEnumerableKeyValuePairProtoReader<ConcurrentDictionary<TKey, TValue>, TKey, TValue>
        where TKey : notnull
    {
        public ConcurrentDictionaryProtoReader(
            IProtoReader<TKey> keyReader,
            IProtoReader<TValue> valueReader,
            uint tag
        )
            : base(
                keyReader,
                valueReader,
                static (_) => new(),
                static (dic, pair) =>
                {
                    dic[pair.Key] = pair.Value;
                    return dic;
                }
            )
        {
        }
    }

    public sealed class ConcurrentDictionaryProtoWriter<TKey, TValue>
        : IEnumerableKeyValuePairProtoWriter<ConcurrentDictionary<TKey, TValue>, TKey, TValue>
        where TKey : notnull
    {
        public ConcurrentDictionaryProtoWriter(
            IProtoWriter<TKey> keyWriter,
            IProtoWriter<TValue> valueWriter,
            uint tag
        )
            : base(keyWriter, valueWriter, tag, (dic) => dic.Count)
        {
        }
    }
}

using System.Collections.Generic;
#pragma warning disable 1591, 0612, 3021, 8981, CS9035

namespace LightProto.Parser
{
    public sealed class DictionaryProtoReader<TKey, TValue>
        : IEnumerableKeyValuePairProtoReader<Dictionary<TKey, TValue>, TKey, TValue>
        where TKey : notnull
    {
        public DictionaryProtoReader(
            IProtoReader<TKey> keyReader,
            IProtoReader<TValue> valueReader,
            uint tag
        )
            : base(
                keyReader,
                valueReader,
                static capacity => new(capacity),
                static (dic, pair) =>
                {
                    dic[pair.Key] = pair.Value;
                    return dic;
                }
            ) { }
    }

    public sealed class DictionaryProtoWriter<TKey, TValue>
        : IEnumerableKeyValuePairProtoWriter<Dictionary<TKey, TValue>, TKey, TValue>
        where TKey : notnull
    {
        public DictionaryProtoWriter(
            IProtoWriter<TKey> keyWriter,
            IProtoWriter<TValue> valueWriter,
            uint tag
        )
            : base(keyWriter, valueWriter, tag, (dic) => dic.Count) { }
    }
}

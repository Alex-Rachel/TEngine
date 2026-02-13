using System.Collections.Generic;
#pragma warning disable 1591, 0612, 3021, 8981, CS9035

namespace LightProto.Parser
{
    public sealed class SortedListProtoReader<TKey, TValue>
        : IEnumerableKeyValuePairProtoReader<SortedList<TKey, TValue>, TKey, TValue>
        where TKey : notnull
    {
        public SortedListProtoReader(
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
            ) { }
    }

    public sealed class SortedListProtoWriter<TKey, TValue>
        : IEnumerableKeyValuePairProtoWriter<SortedList<TKey, TValue>, TKey, TValue>
        where TKey : notnull
    {
        public SortedListProtoWriter(
            IProtoWriter<TKey> keyWriter,
            IProtoWriter<TValue> valueWriter,
            uint tag
        )
            : base(keyWriter, valueWriter, tag, (dic) => dic.Count) { }
    }
}

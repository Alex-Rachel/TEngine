using System;
using System.Collections.Generic;

namespace LightProto.Parser
{
    public class IEnumerableKeyValuePairProtoWriter<TDictionary, TKey, TValue>
        : IEnumerableProtoWriter<TDictionary, KeyValuePair<TKey, TValue>>
        where TDictionary : IEnumerable<KeyValuePair<TKey, TValue>>
        where TKey : notnull
    {
        public IEnumerableKeyValuePairProtoWriter(
            IProtoWriter<TKey> keyWriter,
            IProtoWriter<TValue> valueWriter,
            uint tag,
            Func<TDictionary, int> getCount
        )
            : base(
                itemWriter: new KeyValuePairProtoWriter<TKey, TValue>(keyWriter, valueWriter),
                tag,
                getCount,
                itemFixedSize: 0
            ) { }
    }
}

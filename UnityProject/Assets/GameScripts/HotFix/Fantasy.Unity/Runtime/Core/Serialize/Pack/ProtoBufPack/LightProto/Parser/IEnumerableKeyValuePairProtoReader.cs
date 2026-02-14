using System;
using System.Collections.Generic;

namespace LightProto.Parser
{
    public class IEnumerableKeyValuePairProtoReader<TDictionary, TKey, TValue>
        : IEnumerableProtoReader<TDictionary, KeyValuePair<TKey, TValue>>
        where TDictionary : IEnumerable<KeyValuePair<TKey, TValue>>
        where TKey : notnull
    {
        public IEnumerableKeyValuePairProtoReader(
            IProtoReader<TKey> keyReader,
            IProtoReader<TValue> valueReader,
            Func<int, TDictionary> factory,
            Func<TDictionary, KeyValuePair<TKey, TValue>, TDictionary> addItem
        )
            : base(
                new KeyValuePairProtoReader<TKey, TValue>(keyReader, valueReader),
                factory,
                addItem,
                itemFixedSize: 0,
                completeAction: null
            ) { }
    }
}

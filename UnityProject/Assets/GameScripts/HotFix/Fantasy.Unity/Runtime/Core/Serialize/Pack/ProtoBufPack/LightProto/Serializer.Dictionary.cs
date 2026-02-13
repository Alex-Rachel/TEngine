using System.Buffers;
using System.Collections.Generic;
using LightProto.Parser;

namespace LightProto
{
    public static partial class Serializer
    {
        public static IProtoReader<TDictionary> GetDictionaryReader<TDictionary, TKey, TValue>(
            this IProtoReader<TKey> keyReader,
            IProtoReader<TValue> valueReader
        )
            where TDictionary : IDictionary<TKey, TValue>, new()
            where TKey : notnull
        {
            return new IEnumerableKeyValuePairProtoReader<TDictionary, TKey, TValue>(
                keyReader,
                valueReader,
                static capacity => new(),
                static (dic, pair) =>
                {
                    dic[pair.Key] = pair.Value;
                    return dic;
                }
            );
        }

        public static IProtoWriter<IDictionary<TKey, TValue>> GetDictionaryWriter<TKey, TValue>(
            this IProtoWriter<TKey> keyWriter,
            IProtoWriter<TValue> valueWriter
        )
            where TKey : notnull
        {
            uint tag = WireFormat.MakeTag(1, WireFormat.WireType.LengthDelimited);
            return new IEnumerableKeyValuePairProtoWriter<IDictionary<TKey, TValue>, TKey, TValue>(
                keyWriter,
                valueWriter,
                tag,
                (dic) => dic.Count
            );
        }
    }
}

#if !NET7_0_OR_GREATER
using LightProto;

namespace Fantasy.Serialize
{
    public static class ProtoParserAccessor<T>
    {
        public static readonly IProtoReader<T> Reader = ProtoBufHelper.GetReader<T>();
        public static readonly IProtoWriter<T> Writer = ProtoBufHelper.GetWriter<T>();
    }
}
#endif
using System;

namespace LightProto
{
    [Obsolete("compatibility protobuf-net only, no effect.")]
    public interface IExtensible
    {
        IExtension GetExtensionObject(bool createIfMissing);
    }

    [Obsolete("compatibility protobuf-net only, no effect.")]
    public interface IExtension { }

    [Obsolete("compatibility protobuf-net only, no effect.")]
    public class Extensible : IExtensible
    {
        public static IExtension GetExtensionObject(
            ref IExtension extensionObject,
            bool createIfMissing
        )
        {
            return null!;
        }

        public IExtension GetExtensionObject(bool createIfMissing)
        {
            return null!;
        }
    }
}

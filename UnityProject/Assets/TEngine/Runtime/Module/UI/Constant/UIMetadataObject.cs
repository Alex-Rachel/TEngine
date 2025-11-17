namespace TEngine
{
    internal class UIMetadataObject : ObjectBase
    {
        public static UIMetadataObject Create(UIMetadata target, string name)
        {
            UIMetadataObject obj = MemoryPool.Acquire<UIMetadataObject>();
            obj.Initialize(name, target);
            return obj;
        }


        protected internal override void Release(bool isShutdown)
        {
            UIMetadata metadata = (UIMetadata)Target;
            if (metadata != null)
            {
            }
        }
    }
}
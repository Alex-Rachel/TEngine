namespace LightProto
{
    internal class PackedRepeated
    {
        public static bool Support<TItem>()
        {
            return typeof(TItem) == typeof(bool)
                   || typeof(TItem) == typeof(int)
                   || typeof(TItem) == typeof(long)
                   || typeof(TItem) == typeof(uint)
                   || typeof(TItem) == typeof(ulong)
                   || typeof(TItem) == typeof(float)
                   || typeof(TItem) == typeof(double)
                   || typeof(TItem).IsEnum;
        }
    }
}

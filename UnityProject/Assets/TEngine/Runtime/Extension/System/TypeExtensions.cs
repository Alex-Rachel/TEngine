namespace System
{
    [UnityEngine.Scripting.Preserve]
    public static class TypeExtensions
    {
        [UnityEngine.Scripting.Preserve]
        public static bool IsImplWithInterface(this Type self, Type target)
        {
            return self.GetInterface(target.FullName) != null && !self.IsInterface && !self.IsAbstract;
        }
    }
}
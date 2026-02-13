#if FANTASY_NET
using System.ComponentModel;
using Fantasy.Entitas;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
namespace Fantasy.Serialize;

public static class SupportInitializeChecker<T> where T : Entity
{
    public static bool IsSupported { get; }

    static SupportInitializeChecker()
    {
        IsSupported = typeof(ISupportInitialize).IsAssignableFrom(typeof(T));
    }
}
#endif
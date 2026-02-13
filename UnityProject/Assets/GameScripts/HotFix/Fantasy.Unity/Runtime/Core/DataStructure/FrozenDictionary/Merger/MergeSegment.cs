// ReSharper disable CheckNamespace
// ReSharper disable ConvertToPrimaryConstructor
namespace Fantasy.DataStructure.Dictionary
{
    internal sealed class MergeSegment<T, T1>
    {
        public readonly int Count;
        public readonly T[] TArray;
        public readonly T1[] T1Array;

        public MergeSegment(int count, T[] tArray, T1[] t1Array)
        {
            Count = count;
            TArray = tArray;
            T1Array = t1Array;
        }
    }
}
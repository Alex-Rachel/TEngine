using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Fantasy.Async
{
    [StructLayout(LayoutKind.Auto)]
    [AsyncMethodBuilder(typeof(AsyncFTaskCompletedMethodBuilder))]
    public struct FTaskCompleted : ICriticalNotifyCompletion
    {
        [DebuggerHidden]
        public bool IsCompleted => true;
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FTaskCompleted GetAwaiter()
        {
            return this;
        }
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetResult() { }
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnCompleted(Action continuation) { }
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UnsafeOnCompleted(Action continuation) { }
    }
}
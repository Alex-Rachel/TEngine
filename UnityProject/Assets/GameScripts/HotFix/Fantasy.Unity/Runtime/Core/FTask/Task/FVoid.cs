using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Fantasy.Async
{
    [StructLayout(LayoutKind.Auto)]
    [AsyncMethodBuilder(typeof(AsyncFVoidMethodBuilder))]
    internal struct FVoid : ICriticalNotifyCompletion
    {
        public bool IsCompleted
        {
            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => true;
        }
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Coroutine() { }
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnCompleted(Action continuation) { }
        
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UnsafeOnCompleted(Action continuation) { }
    }
}
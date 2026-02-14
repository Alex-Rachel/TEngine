using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
// ReSharper disable MemberCanBePrivate.Global
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CS8604 // Possible null reference argument.

namespace Fantasy.Async
{
    [StructLayout(LayoutKind.Auto)]
    public readonly struct AsyncFTaskMethodBuilder
    {
        public FTask Task
        {
            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
        }

        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AsyncFTaskMethodBuilder Create()
        {
            return new AsyncFTaskMethodBuilder(FTask.Create());
        }

        public AsyncFTaskMethodBuilder(FTask fTask)
        {
            Task = fTask;
        }

        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            stateMachine.MoveNext();
        }

        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetResult()
        {
            Task.SetResult();
        }

        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetException(Exception exception)
        {
            Task.SetException(exception);
        }

        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            // 通常在异步方法中遇到 await 关键字时调用，并且需要将执行恢复到调用 await 之前的同步上下文。
            awaiter.OnCompleted(stateMachine.MoveNext);
        }

        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            // 通常在你不需要恢复到原始同步上下文时调用，这意味着你不关心在什么线程上恢复执行。
            awaiter.UnsafeOnCompleted(stateMachine.MoveNext);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            // 用于设置和保存异步方法的状态机实例。
            // 编译器在生成异步方法时要求其存在。
            // 编译器生成的代码已经足够处理状态机的管理,所以这里没有什么特殊要求所以保持空实现。
        }
    }

    [StructLayout(LayoutKind.Auto)]
    public readonly struct AsyncFTaskMethodBuilder<T>
    {
        public FTask<T> Task
        {
            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
        }
        
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AsyncFTaskMethodBuilder<T> Create()
        {
            return new AsyncFTaskMethodBuilder<T>(FTask<T>.Create());
        }

        public AsyncFTaskMethodBuilder(FTask<T> fTask)
        {
            Task = fTask;
        }
        
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            stateMachine.MoveNext();
        }

        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetResult(T value)
        {
            Task.SetResult(value);
        }
        
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetException(Exception exception)
        {
            Task.SetException(exception);
        }
        
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            awaiter.OnCompleted(stateMachine.MoveNext);
        }
        
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
        {
            awaiter.UnsafeOnCompleted(stateMachine.MoveNext);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetStateMachine(IAsyncStateMachine stateMachine) { }
    }
}
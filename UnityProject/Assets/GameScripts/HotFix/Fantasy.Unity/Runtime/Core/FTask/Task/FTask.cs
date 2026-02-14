using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
// ReSharper disable ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
// ReSharper disable CheckNamespace
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.

namespace Fantasy.Async
{
    public enum STaskStatus : byte
    {
        Pending = 0, // The operation has not yet completed.
        Succeeded = 1, // The operation completed successfully.
        Faulted = 2 // The operation completed with an error.
    }
    
    [AsyncMethodBuilder(typeof(AsyncFTaskMethodBuilder))]
    public sealed partial class FTask : ICriticalNotifyCompletion
    {
        private Action _callBack;
        private ExceptionDispatchInfo _exception;
        private STaskStatus _status = STaskStatus.Pending;
        public bool IsCompleted
        {
            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _status != STaskStatus.Pending;
        }

        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FTask GetAwaiter() => this;
        
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async FVoid InnerCoroutine()
        {
            await this;
        }
        
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Coroutine()
        {
            InnerCoroutine().Coroutine();
        }
        
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetResult()
        {
            switch (_status)
            {
                case STaskStatus.Succeeded:
                {
                    Return();
                    return;
                }
                case STaskStatus.Faulted:
                {
                    Return();

                    if (_exception == null)
                    {
                        return;
                    }
                    
                    var exception = _exception;
                    _exception = null;
                    exception.Throw();
                    return;
                }
                default:
                {
                    throw new NotSupportedException("Direct call to getResult is not allowed");
                }
            }
        }
        
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetResult()
        {
            if (_status != STaskStatus.Pending)
            {
                throw new InvalidOperationException("The task has been completed");
            }
            
            _status = STaskStatus.Succeeded;

            if (_callBack == null)
            {
                return;
            }
            
            var callBack = _callBack;
            _callBack = null;
            callBack.Invoke();
        }
        
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnCompleted(Action action)
        {
            UnsafeOnCompleted(action);
        }
        
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UnsafeOnCompleted(Action action)
        {
            if (_status != STaskStatus.Pending)
            {
                action?.Invoke();
                return;
            }

            _callBack = action;
        }
       
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetException(Exception exception)
        {
            if (_status != STaskStatus.Pending)
            {
                throw new InvalidOperationException("The task has been completed");
            }

            _status = STaskStatus.Faulted;
            _exception = ExceptionDispatchInfo.Capture(exception);
            _callBack?.Invoke();
        }
    }

    [AsyncMethodBuilder(typeof(AsyncFTaskMethodBuilder<>))]
    public sealed partial class FTask<T> : ICriticalNotifyCompletion
    {
        private T _value;
        private Action _callBack;
        private ExceptionDispatchInfo _exception;
        private STaskStatus _status = STaskStatus.Pending;
        public bool IsCompleted
        {
            [DebuggerHidden]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _status != STaskStatus.Pending;
        }
        
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FTask<T> GetAwaiter() => this;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [DebuggerHidden]
        private async FVoid InnerCoroutine()
        {
            await this;
        }
        
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Coroutine()
        {
            InnerCoroutine().Coroutine();
        }
        
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetResult()
        {
            switch (_status)
            {
                case STaskStatus.Succeeded:
                {
                    var value = _value;
                    Return();
                    return value;
                }
                case STaskStatus.Faulted:
                {
                    Return();
                    
                    if (_exception == null)
                    {
                        return default;
                    }
                    
                    var exception = _exception;
                    _exception = null;
                    exception.Throw();
                    return default;
                }
                default:
                {
                    throw new NotSupportedException("Direct call to getResult is not allowed");
                }
            }
        }
        
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetResult(T value)
        {
            if (_status != STaskStatus.Pending)
            {
                throw new InvalidOperationException("The task has been completed");
            }
            
            _value = value;
            _status = STaskStatus.Succeeded;

            if (_callBack == null)
            {
                return;
            }
            
            var callBack = _callBack;
            _callBack = null;
            callBack.Invoke();
        }
        
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnCompleted(Action action)
        {
            UnsafeOnCompleted(action);
        }
        
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UnsafeOnCompleted(Action action)
        {
            if (_status != STaskStatus.Pending)
            {
                action?.Invoke();
                return;
            }

            _callBack = action;
        }
        
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetException(Exception exception)
        {
            if (_status != STaskStatus.Pending)
            {
                throw new InvalidOperationException("The task has been completed");
            }

            _status = STaskStatus.Faulted;
            _exception = ExceptionDispatchInfo.Capture(exception);
            _callBack?.Invoke();
        }
    }
}
#nullable enable
using System;
using System.Reflection;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

namespace IlInterpreter.Interpreter
{
    partial class ScriptInterpreter
    {
    /// <summary>
    /// The value behind a delegate whose TYPE names a SCRIPT-defined type — e.g. <c>Action&lt;Node&gt;</c>
    /// where <c>Node</c> is a script class. Such a delegate type has NO runtime <see cref="Type"/>
    /// (only its type argument is a <see cref="ScriptTypeDescriptor"/>), so it can't be represented as a
    /// real CLR <see cref="Delegate"/> the way <see cref="ScriptDelegateAdapter"/> does — a CLR
    /// <c>Invoke(Node)</c> can't carry a <see cref="ScriptObject"/>. Instead this is an INTERNAL reference
    /// value living in O slots like any object; <c>invoke_script_delegate</c> re-enters the VM on
    /// <see cref="Target"/> (with <see cref="Receiver"/> prepended for instance targets) through the same
    /// <see cref="InvokeDelegateTarget"/> plumbing the adapter uses.
    ///
    /// Single-target only: no multicast (<c>+=</c>), no delegate equality, no BeginInvoke. Lifetime is the
    /// same generation contract as <see cref="ScriptDelegateAdapter"/> — invalid after a re-<c>Load</c>.
    /// </summary>
    internal sealed class ScriptDelegate
    {
        public readonly ParsedMethod Target;
        public readonly object?      Receiver;   // null for static targets; prepended as arg 0 otherwise
        public readonly object       Generation; // the ParsedAssembly the target was lowered with

        public ScriptDelegate(ParsedMethod target, object? receiver, object generation)
        {
            Target     = target;
            Receiver   = receiver;
            Generation = generation;
        }
    }

    /// <summary>
    /// The object behind a delegate whose target is an INTERPRETED method (a method group over a
    /// script method, or a lambda's display-class method). The host holds an ordinary CLR delegate
    /// of the exact requested type — <c>System.Action</c>, <c>EventCallback&lt;T&gt;</c>, a custom
    /// delegate — and each invocation re-enters the VM, like <see cref="ScriptEnumerator"/> does
    /// for iterators.
    ///
    /// No Reflection.Emit: the delegate is built with <c>Delegate.CreateDelegate</c> over a
    /// shape-matched generic forwarder (<c>V0..V4</c> for void, <c>F0..F4</c> otherwise) closed
    /// over the delegate's Invoke signature. Same AOT posture as HostBinding's typed fast paths;
    /// on IL2CPP a value-typed signature relies on full generic sharing.
    ///
    /// Lifetime: valid until the interpreter is re-<c>Load</c>ed or disposed — the same contract
    /// as every other object that crosses the boundary.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal sealed class ScriptDelegateAdapter
    {
        readonly ScriptInterpreter _owner;
        readonly ParsedMethod _target;
        readonly object? _receiver;   // null for static targets; prepended as arg 0 otherwise
        readonly object _generation;  // the ParsedAssembly this target was lowered with

        ScriptDelegateAdapter(ScriptInterpreter owner, ParsedMethod target, object? receiver)
        {
            _owner = owner;
            _target = target;
            _receiver = receiver;
            _generation = owner._parsed!;
        }

        public static Delegate Create(
            ScriptInterpreter owner, Type delegateType, ParsedMethod target, object? receiver)
        {
            var invoke = delegateType.GetMethod("Invoke")
                ?? throw new ScriptRuntimeException($"'{delegateType}' has no Invoke — not a delegate type");
            var ps = invoke.GetParameters();
            bool isVoid = invoke.ReturnType == typeof(void);
            if (ps.Length > MaxArity)
                throw new ScriptRuntimeException(
                    $"Delegate '{delegateType.Name}' has {ps.Length} parameters — method groups over " +
                    $"interpreted methods support up to {MaxArity}");
            foreach (var p in ps)
                if (p.ParameterType.IsByRef)
                    throw new ScriptRuntimeException(
                        $"Delegate '{delegateType.Name}' has a ref/out parameter — not supported " +
                        "for method groups over interpreted methods");

            var adapter = new ScriptDelegateAdapter(owner, target, receiver);
            var shape = typeof(ScriptDelegateAdapter).GetMethod(
                (isVoid ? "V" : "F") + ps.Length, BindingFlags.Public | BindingFlags.Instance)!;
            int typeArgCount = ps.Length + (isVoid ? 0 : 1);
            if (typeArgCount > 0)
            {
                var typeArgs = new Type[typeArgCount];
                for (int i = 0; i < ps.Length; i++) typeArgs[i] = ps[i].ParameterType;
                if (!isVoid) typeArgs[ps.Length] = invoke.ReturnType;
                shape = shape.MakeGenericMethod(typeArgs);
            }
            try
            {
                return Delegate.CreateDelegate(delegateType, adapter, shape);
            }
            catch (ArgumentException ex)
            {
                throw new ScriptRuntimeException(
                    $"Could not bind '{target.Name}' to delegate type '{delegateType.Name}': {ex.Message}");
            }
        }

        const int MaxArity = 4;

        object? Call(object?[] args)
        {
            if (_receiver == null)
                return _owner.InvokeDelegateTarget(_target, args, _generation);
            var withReceiver = new object?[args.Length + 1];
            withReceiver[0] = _receiver;
            Array.Copy(args, 0, withReceiver, 1, args.Length);
            return _owner.InvokeDelegateTarget(_target, withReceiver, _generation);
        }

        static readonly object?[] s_NoArgs = Array.Empty<object?>();

        // Shape-matched forwarders — one per (void?, arity). Public so CreateDelegate binds them.
        public void V0() => Call(s_NoArgs);
        public void V1<A>(A a) => Call(new object?[] { a });
        public void V2<A, B>(A a, B b) => Call(new object?[] { a, b });
        public void V3<A, B, C>(A a, B b, C c) => Call(new object?[] { a, b, c });
        public void V4<A, B, C, D>(A a, B b, C c, D d) => Call(new object?[] { a, b, c, d });
        public R F0<R>() => (R)Call(s_NoArgs)!;
        public R F1<A, R>(A a) => (R)Call(new object?[] { a })!;
        public R F2<A, B, R>(A a, B b) => (R)Call(new object?[] { a, b })!;
        public R F3<A, B, C, R>(A a, B b, C c) => (R)Call(new object?[] { a, b, c })!;
        public R F4<A, B, C, D, R>(A a, B b, C c, D d) => (R)Call(new object?[] { a, b, c, d })!;
    }
    }
}

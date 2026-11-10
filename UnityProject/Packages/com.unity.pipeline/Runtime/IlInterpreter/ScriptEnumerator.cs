#nullable enable
using System;
using System.Collections;

namespace IlInterpreter.Interpreter
{

sealed partial class ScriptInterpreter
{
    // Host-side IEnumerator over an interpreted iterator state machine. The host (Unity's
    // coroutine scheduler, foreach, a manual MoveNext loop) drives the interface; each call
    // re-enters the VM with the state machine ScriptObject as the receiver. Produced by
    // WrapForHost whenever a script enumerator crosses the host boundary.
    //
    // Lifetime: valid until the owning interpreter is Unload()ed/Dispose()d or reLoad()ed —
    // the state machine's methods belong to the loaded assembly. Driving a bridge after that
    // ends the iterator like any other script error (see the guard below).
    internal sealed class ScriptEnumerator : IEnumerator, IDisposable
    {
        readonly ScriptInterpreter _owner;
        readonly EnumeratorMembers _members;

        // Set when a pump threw: the iterator is over. Without this guard an exception from
        // an interpreted body propagates out of MoveNext into Unity's coroutine scheduler and
        // kills the ENTIRE host coroutine chain that yielded on this bridge (a method-reload
        // error froze the whole scene loop). A compiled body has the registry's
        // fall-back-to-original safety net at dispatch; a coroutine body runs after dispatch,
        // so this is its equivalent: log through the interpreter's sink and end the iterator —
        // the outer coroutine resumes as if it completed.
        bool _faulted;

        // Exposed for tests/diagnostics: the interpreted state machine instance.
        internal ScriptObject StateMachine { get; }

        // Exposed for tests/diagnostics: the exception that ended this iterator, if any. The
        // bridge deliberately swallows it (see _faulted) — but a differential harness driving
        // oracle and interpreter side by side still needs to SEE the fault to check parity.
        internal Exception? FaultException { get; private set; }

        internal ScriptEnumerator(ScriptInterpreter owner, ScriptObject stateMachine,
                                  EnumeratorMembers members)
        {
            _owner       = owner;
            StateMachine = stateMachine;
            _members     = members;
        }

        public bool MoveNext()
        {
            if (_faulted) return false;
            try
            {
                return _owner.InvokeEnumeratorMember(_members.MoveNext, StateMachine) is bool b && b;
            }
            catch (Exception ex)
            {
                Fault("MoveNext", ex);
                return false;
            }
        }

        // get_Current is a plain field read on the state machine — cheap and side-effect
        // free, so no caching. A nested interpreted enumerator yielded as Current comes back
        // wrapped (InvokeEnumeratorMember runs WrapForHost on its result).
        public object? Current
        {
            get
            {
                if (_faulted) return null;
                try
                {
                    return _owner.InvokeEnumeratorMember(_members.GetCurrent, StateMachine);
                }
                catch (Exception ex)
                {
                    Fault("Current", ex);
                    return null;
                }
            }
        }

        // Iterator state machines don't support Reset (their own Reset throws
        // NotSupportedException — the exact body the interpreter skips as a cold stub).
        public void Reset() => throw new NotSupportedException();

        public void Dispose()
        {
            if (_faulted || _members.Dispose == null) return;
            try
            {
                _owner.InvokeEnumeratorMember(_members.Dispose, StateMachine);
            }
            catch (Exception ex)
            {
                Fault("Dispose", ex);
            }
        }

        void Fault(string where, Exception ex)
        {
            _faulted = true;
            FaultException = ex;
            // Full exception (type + stack), not just the message: a ScriptRuntimeException
            // carries its location in the message, but a raw exception escaping the VM (an
            // interpreter bug or a fast-path host throw) is only diagnosable from its stack.
            _owner._logSink(
                $"MethodReload: interpreted coroutine '{StateMachine.Type.Name}' threw in {where} — " +
                $"ending this iterator so the host coroutine chain survives: {ex}");
        }
    }
}

}

#nullable enable
using System;
using System.Collections.Generic;
namespace IlInterpreter
{

/// <summary>Base exception for all IlInterpreter errors.</summary>
public class ScriptException : Exception
{
    /// <summary>Create the exception with a message.</summary>
    /// <param name="message">The exception message.</param>
    public ScriptException(string message) : base(message) { }
    /// <summary>Create the exception with a message and an inner cause.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="inner">The exception that caused this one, if any.</param>
    public ScriptException(string message, Exception? inner) : base(message, inner) { }
}

/// <summary>Raised when script source fails to compile.</summary>
public class ScriptCompileException : ScriptException
{
    /// <summary>The compiler diagnostics that failed the compilation.</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>Create the exception from the diagnostics that failed the compilation.</summary>
    /// <param name="diagnostics">The compiler diagnostics that failed the compilation.</param>
    public ScriptCompileException(IReadOnlyList<string> diagnostics)
        : base($"Script compilation failed with {diagnostics.Count} error(s).")
    {
        Diagnostics = diagnostics;
    }
}

/// <summary>Raised when compiled IL fails validation.</summary>
public class ScriptValidationException : ScriptException
{
    /// <summary>Create the exception with a message.</summary>
    /// <param name="message">The exception message.</param>
    public ScriptValidationException(string message) : base(message) { }
}

/// <summary>Raised during interpreter execution.</summary>
public class ScriptRuntimeException : ScriptException
{
    /// <summary>Create the exception with a message.</summary>
    /// <param name="message">The exception message.</param>
    public ScriptRuntimeException(string message) : base(message) { }
    /// <summary>Create the exception with a message and an inner cause.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="inner">The exception that caused this one, if any.</param>
    public ScriptRuntimeException(string message, Exception? inner) : base(message, inner) { }
}
}

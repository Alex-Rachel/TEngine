#nullable enable
using System;
using System.Collections.Generic;
namespace IlInterpreter
{

/// <summary>
/// Represents a compiled, validated script that can be executed by the interpreter.
/// Instances are produced by the host compilation layer (Unity.Pipeline.Compilation) and
/// consumed by <c>IlInterpreter.Interpreter</c>.
/// </summary>
interface IScript
{
    /// <summary>Name of the script class (as declared in source).</summary>
    string Name { get; }

    /// <summary>
    /// The compiled IL bytes. These have been validated by the IL validator and
    /// are safe to interpret.
    /// </summary>
    ReadOnlyMemory<byte> Il { get; }
}
}

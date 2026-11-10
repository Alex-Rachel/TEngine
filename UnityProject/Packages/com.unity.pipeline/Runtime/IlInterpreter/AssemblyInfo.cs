using System.Runtime.CompilerServices;

// The IlInterpreter VM is an implementation detail of eval/method reload, not a public scripting API:
// only the Script* exceptions are public (user code catches them from eval/reload failures).
// Everything else is internal, visible to the package
// assemblies that drive the VM and to the Unity test assemblies. The standalone dotnet suites
// (IlInterpreter/tests/IlInterpreter.*) compile these sources directly into their own assembly, so they need no
// grant here.
[assembly: InternalsVisibleTo("Unity.Pipeline")]
[assembly: InternalsVisibleTo("Unity.Pipeline.Editor")]
[assembly: InternalsVisibleTo("Unity.Pipeline.Tests.Editor")]
[assembly: InternalsVisibleTo("Unity.Pipeline.Tests.Runtime")]
// IlInterpreter/tests/IlInterpreter/IlInterpreter.csproj compiles these same sources (this file included) into a
// standalone IlInterpreter.dll so the fuzzer can instrument the VM in isolation; the fuzz and bench
// executables consume that DLL cross-assembly.
[assembly: InternalsVisibleTo("IlInterpreter.Fuzz")]
[assembly: InternalsVisibleTo("IlInterpreter.Bench")]

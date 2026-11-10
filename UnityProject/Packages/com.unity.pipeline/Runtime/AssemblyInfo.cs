using System.Runtime.CompilerServices;

// Lets the runtime test assembly's PipelineClient read a server's internal Token to authenticate.
[assembly: InternalsVisibleTo("Unity.Pipeline.Tests.Runtime")]
// Lets the EditMode test assembly unit-test internal runtime utilities (e.g. MaxLengthStream).
[assembly: InternalsVisibleTo("Unity.Pipeline.Tests.Editor")]
// Lets the Editor assembly feed CliProgress's internal ambient mirror (EditorProgressMirror)
// and consume internal interpreter glue (e.g. IlInterpreterHostBindings).
[assembly: InternalsVisibleTo("Unity.Pipeline.Editor")]

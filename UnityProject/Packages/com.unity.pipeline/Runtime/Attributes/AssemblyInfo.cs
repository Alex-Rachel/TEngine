using System.Runtime.CompilerServices;

// Lets the package's own commands use ImmediateCommandAttribute, which stays internal so
// only the package decides which commands run outside the queue.
[assembly: InternalsVisibleTo("Unity.Pipeline")]
[assembly: InternalsVisibleTo("Unity.Pipeline.Editor")]

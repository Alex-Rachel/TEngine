# Script commands

Commands for creating C# script files, attaching MonoBehaviours to GameObjects, reading/writing serialized fields, and running compiled project entry points in memory. Writing a `.cs` file does not make its type available — Unity must import and compile it (a domain reload) first. The flow is: `script create` → `script recompile` → poll `script recompile_status` (until completed/up_to_date) → `script attach`. For bulk construction that does **not** need a new persistent project type, skip the recompile entirely: [`script run`](#script-run) compiles a file in memory and runs it with no asset import and no domain reload.

Two commands run C# without a domain reload:

| | [`script eval`](#script-eval) | [`script run`](#script-run) |
|---|---|---|
| Runs | A C# script, inline or from a `.cs` file: statements, `using` lines, declarations, `await` | A named `static` method of a `.cs` file |
| Arguments | None | `args`, coerced to the method's parameters |
| Where | Editor and desktop development Player | Editor only |
| Use for | Queries, quick edits, waits | Builders you keep in a file and re-run; extra defines; in-place method replacement |

### `script create`
Create a new C# script (default base class MonoBehaviour) from a template under the authoring root. NOTE: the type does not exist until a recompile completes — to attach it, call recompile, poll `script recompile_status`, then `script attach`.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `name` | yes | – | Class/file name without extension, e.g. PlayerController. Must be a valid C# identifier. |
| `path` | no | `–` | Folder (relative to the authoring root; the Assets/ prefix is optional) to write the .cs into. Defaults to the authoring root. |
| `namespace` | no | `–` | Optional namespace to wrap the class in. Omit for the global namespace. |
| `base-class` | no | `MonoBehaviour` | Base class to derive from. Defaults to MonoBehaviour. |
| `overwrite` | no | `false` | Overwrite the file if it already exists. Defaults to false (an existing file is an error). |

**Returns:** `AuthoringResult`
**Notes:** Does not trigger a recompile; the created type is not usable until a recompile completes (poll `script recompile_status`).

### `script attach`
Add a MonoBehaviour to a GameObject by its (compiled) type name OR by its script asset path. Provide exactly one of 'type' or 'script'. If the type isn't compiled yet, returns a recoverable error: recompile, poll `script recompile_status`, then retry.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | – | Reference to the GameObject to add the component to (globalId/path/guid/instanceId/hierarchyPath). |
| `type` | no | `–` | Component type name to add, e.g. PlayerController or Game.Player.PlayerController. Must already be compiled. Mutually exclusive with 'script'. |
| `script` | no | `–` | Script asset path, e.g. 'Assets/Pool/Scripts/CueShooter.cs'. The backing class is resolved via MonoScript.GetClass(), so the class name may differ from the filename. Mutually exclusive with 'type'. |

**Returns:** `AuthoringResult`
**Notes:** Undo-able. Provide exactly one of `type` or `script`. A not-yet-compiled type returns a recoverable error (recompile, poll `script recompile_status`, then retry).

### `component set_field`
Set a serialized field on a component/asset. Supports primitives, enums, Vector/Color/Rect/Bounds, object references (value = an ObjectRef: asset by guid/fileId/path or scene object by instanceId/hierarchyPath), and array elements via 'name.Array.data[i]' (or 'name.Array.size' to resize).

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | – | Reference to the component or asset to modify (globalId/path/guid/instanceId/hierarchyPath). May be a GameObject when 'component' is given. |
| `field` | yes | – | SerializedProperty path, e.g. 'speed', 'settings.speed', or 'waypoints.Array.data[0]'. |
| `value` | yes | – | JSON value to assign. For object references pass an ObjectRef object (or null to clear). For enums pass the value name. |
| `component` | no | `–` | Component type name on the target GameObject (e.g. 'Rigidbody'). Use when 'target' is a GameObject; omit when 'target' is already a component handle. |

**Returns:** `AuthoringResult`
**Notes:** Undo-able.

### `component get_fields`
Read serialized fields of a component/asset. Returns each top-level field's name, type and value (object references are returned as re-usable handles). Pass 'field' to read a single SerializedProperty path. Pass detail='value' to return just the value(s) — a token-efficient read for scalars.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | – | Reference to the component or asset to read (globalId/path/guid/instanceId/hierarchyPath). May be a GameObject when 'component' is given. |
| `field` | no | `–` | Optional single SerializedProperty path to read (e.g. 'speed' or 'items.Array.data[0]'). Omit to read all top-level fields. |
| `component` | no | `–` | Component type name on the target GameObject (e.g. 'Rigidbody'). Use when 'target' is a GameObject; omit when 'target' is already a component handle. |
| `detail` | no | `full` | full for a descriptor per field {name,path,propertyType,isArray,arrayLength,value}; value for just the value(s): the raw value for a single 'field', or a {name: value} map for all fields. |

**Returns:** `object`

### `script eval`
Compile and run C# as a script: statements, `using` directives, top-level methods and types, and
`await`. The value of `return x;`, or of a trailing expression, is the result.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `code` | no | `–` | C# code to evaluate. Give either `code` or `file`. May be given first, without `--code`. |
| `file` | no | `–` | Path to a `.cs` file to evaluate instead of `code`. In the Editor, relative paths resolve against the project root (the parent of `Assets/`); in a player, against the working directory. |
| `timeout` | no | `30` | Timeout in seconds for the whole run, awaits included. |
| `dry-run` | no | `false` | Compile only: return diagnostics. Nothing is loaded or run. |
| `auto-import` | no | `false` | When a type or name is not found, add a `using` for the one namespace that declares it and compile again. The reply lists the usings it added in `addedUsings`. A name declared in several namespaces fails and lists them. |
| `internals` | no | `false` | Editor only. Compile so Unity's internal APIs are callable. Each run borrows an assembly name that Unity's modules grant internal access; once all of them are in use, it fails until the next domain reload. |

A script can use these without a `using` line: `System`, `System.Collections.Generic`,
`System.Linq`, `System.Threading.Tasks`, `UnityEngine`, `UnityEditor` (Editor only), and
`Unity.Pipeline.Scripting`. `Object` means `UnityEngine.Object` unless the script declares its own
`Object` alias.

`Wait` (in `Unity.Pipeline.Scripting`) awaits something that finishes later without blocking the
main thread: `Wait.Until(condition)`, `Wait.Until(getValue, condition)`, `Wait.For(asyncOperation)`,
and `Wait.ForImport(assetPath)` in the Editor. A wait that runs out of time throws
`WaitTimeoutException`; cancelling the job stops it. A wait cannot span a domain reload.

**Returns:** `EvalResponse` — `{ result, diagnostics, addedUsings }`. Diagnostic lines are 1-based
and count the script's own lines; warnings are returned on success too. A dry run returns no result.
**Errors:** a failed eval fails the reply, with its `EvalResponse` still in `result`:

| `errorCode` | When | HTTP |
|---|---|---|
| `INVALID_COMMAND_ARGS` | No code, both `code` and `file`, a missing, empty or non-`.cs` file, or `internals` in a player | 400 |
| `COMPILATION_ERROR` | The script does not compile; `errorDetails` lists each error with its line and column | 400 |
| `RUNTIME_EXCEPTION` | The script threw, including a `WaitTimeoutException`; `errorDetails` has the exception and its stack trace, with script lines | 400 |
| `TIMEOUT` | The run outlasted `timeout`; the script keeps running detached | 504 |
| `UNSUPPORTED_PLATFORM` | A player that is not a desktop development build | 400 |
| `EVAL_INTERNAL_ERROR` | The compiled script could not be loaded or started | 500 |

**Notes:** `MainThreadRequired = true`. Available on both editor and runtime. Run it as a job
(`--detach`) for long waits. Each run loads one small assembly that Unity cannot unload; a dry run
loads none.

#### Examples

Statements, a `using` alias, and top-level declarations with a trailing expression as the result:

```bash
unity command script eval 'return Application.unityVersion;'                                       # the Editor's version
unity command script eval 'return 2 + 2;'                                                          # 4
unity command script eval 'using Random = UnityEngine.Random; return Random.Range(1, 7);'           # 1 to 6
unity command script eval 'class P { public int V; } static int Twice(int x) => x * 2; Twice(new P { V = 21 }.V)'   # 42
```

`await` keeps the Editor running, so Unity objects stay usable across frames:

```bash
unity command script eval 'var started = Time.realtimeSinceStartup; await Task.Delay(300); return Time.realtimeSinceStartup - started >= 0.3f;'   # true
unity command script eval 'var go = new GameObject("Probe"); await Task.Yield(); var alive = go != null; Object.DestroyImmediate(go); return alive;'   # true
```

Wait for an import, or give up with a reason:

```bash
unity command script eval 'await Wait.Until(() => !EditorApplication.isUpdating); return AssetDatabase.FindAssets("t:Prefab").Length;'
unity command script eval 'var path = "Assets/E2EEvalDemo.txt"; System.IO.File.WriteAllText(path, "hello"); AssetDatabase.ImportAsset(path); await Wait.ForImport(path); var text = AssetDatabase.LoadAssetAtPath<TextAsset>(path).text; AssetDatabase.DeleteAsset(path); return text;'   # "hello"
unity command script eval 'await Wait.Until(() => false, timeoutMs: 200, because: "the build never finished");'
# RUNTIME_EXCEPTION: WaitTimeoutException: Wait.Until timed out after 200 ms: the build never finished
```

Run a long wait as a job, and cancel it:

```bash
unity command script eval 'await Wait.Until(() => GameObject.Find("E2ENeverSpawned") != null, timeoutMs: 600000); return "spawned";' --detach
unity job status <jobId>    # running
unity job cancel <jobId>
unity job status <jobId>    # canceled
```

Run a file. Keep it outside `Assets/` (here `AgentScripts/`) so writing it triggers no import.
`AgentScripts/CountByTag.cs`:

```csharp
using System.Text;
using UnityEngine.SceneManagement;

class Counted { public string Tag; public int Count; }

static Counted[] CountByTag(Scene scene) =>
    scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
        .GroupBy(t => t.tag)
        .Select(g => new Counted { Tag = g.Key, Count = g.Count() })
        .OrderByDescending(c => c.Count)
        .ToArray();

var report = new StringBuilder();
foreach (var c in CountByTag(SceneManager.GetActiveScene()))
    report.AppendLine($"{c.Tag}: {c.Count}");
report.ToString()
```

```bash
unity command script eval --file AgentScripts/CountByTag.cs             # "MainCamera: 1\nUntagged: 1\n"
unity command script eval --file AgentScripts/CountByTag.cs --dry-run   # compiles; result is null, nothing runs
```

Add missing `using` directives, or see which namespaces to choose from:

```bash
unity command script eval 'return new StringBuilder("auto").Append("-import").ToString();' --auto-import
# "auto-import", with addedUsings: ["System.Text"]
unity command script eval 'return EditorSceneManager.GetActiveScene().name;' --auto-import
# addedUsings: ["UnityEditor.SceneManagement"]
unity command script eval 'return new Image();' --auto-import
# COMPILATION_ERROR: ... auto-import found it in several namespaces: ..., UnityEngine.UIElements.Image. Add a using directive for the one you mean.
```

Call internal Editor APIs:

```bash
unity command script eval 'return UnityEditor.LogEntries.GetCount();' --internals   # the console's entry count
unity command script eval 'return UnityEditor.LogEntries.GetCount();'
# COMPILATION_ERROR: line 1, col 19: error CS0122: 'LogEntries' is inaccessible due to its protection level
```

Compile errors list each error with its line and column:

```bash
unity command script eval 'return Foo(;'
# COMPILATION_ERROR:
#   line 1, col 11: error CS1026: ) expected
#   line 1, col 7: error CS0103: The name 'Foo' does not exist in the current context
```

### `script eval_file`
Deprecated: use `script eval --file`. Takes the same parameters, with `file` required and given
first, and adds a deprecation warning to its result.

```bash
unity command script eval_file AgentScripts/CountByTag.cs   # same as: unity command script eval --file AgentScripts/CountByTag.cs
```

**Returns:** `EvalResponse`
**Notes:** `MainThreadRequired = true`. Available on both editor and runtime.

### `script run`
Compile a single project `.cs` file in memory (no domain reload, no code carried through the
protocol) and execute a named `static` entry point. This is the **builder-pattern** path for bulk
construction — see the note below. Editor command; still arbitrary code execution, so it is grouped
under the same `scripts/eval` tag as `script eval` and is disabled wherever the eval family is disabled.

In short: keep a small tool as a `.cs` file, and call one of its methods with arguments. Editing
the file and running it again costs an in-memory compile, not a domain reload.

The file is ordinary C# — types and methods, no top-level statements — unlike a
`script eval --file` script, which runs top to bottom and takes no arguments. Nothing in the file
runs unless `entry` names it.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `file` | yes | `–` | Path to a `.cs` file. Relative paths resolve against the **project root** (the parent of `Assets/`), not the process working directory. May live **outside `Assets/`** (e.g. `AgentScripts/`) so writing it never triggers an asset import or domain reload. |
| `entry` | no | `–` | Entry point as `Namespace.Type.Method` (or `Type.Method`, or a bare `Method`). Nested types may be written with dots (`Outer.Inner.Method`). Default: the single public static method if unambiguous, else a method named `Main`. Static, non-generic methods only in v1. Ephemeral mode only — rejected with `mode=hotpatch`. |
| `args` | no | `–` | JSON array of arguments, coerced to the entry's parameter types: primitives, enum names, `string[]`, and a string/object handle (`ObjectRef`) for `UnityEngine.Object` parameters. JSON `null` is only valid for reference-type and nullable parameters (never a silent `default(T)`). Ephemeral mode only — rejected with `mode=hotpatch`. |
| `mode` | no | `ephemeral` | `ephemeral`: compile to an in-memory assembly, run the entry, discard. `hotpatch`: apply in-place `[MethodReload]` method replacements to already-loaded types (delegates to `script.methodreload reload`). |
| `references` | no | `–` | Extra assembly name prefixes to reference. In the Editor all loaded assemblies are already referenced, so this is effectively a no-op there. Ephemeral mode only; **not applied** by hotpatch (the compile is `script.methodreload reload`'s). |
| `defines` | no | `–` | Extra scripting define symbols, **appended to the project's active editor defines** (`EditorUserBuildSettings.activeScriptCompilationDefines`) — so `UNITY_EDITOR`, version and platform symbols are already set and sources behave like project code; your defines extend that set rather than replace it. Ephemeral mode only; **not applied** by hotpatch. |
| `pdb` | no | `false` | Emit a portable PDB mapped to the source file so breakpoints bind. Ephemeral runs always emit source-mapped symbols so exception stack traces carry `file:line`. Compiles unoptimized. |
| `timeout` | no | `60` | Timeout in seconds for the whole run, like `script eval`. |
| `dry-run` | no | `false` | Compile only: return diagnostics; the emitted assembly is **not loaded** into the domain and nothing executes (the response carries no `assemblyName`). Rejected with `mode=hotpatch` — a hotpatch "dry run" would still apply live method replacements. |

**Returns:** `RunScriptResponse` — `{ result, diagnostics, compileMs, executeMs, assemblyName }` on top
of the standard envelope. The result carries no echo of the source (path + diagnostics only). Compile
errors return full Roslyn diagnostics (with line/column) and execute nothing; runtime exceptions
surface as structured errors with `file:line` mapped to the source file.
**Notes:** `MainThreadRequired = true`. Editor command.

- Executing runs always compile **unoptimized (Debug codegen)** so the emitted symbols keep full
  sequence points for `file:line` mapping — expect the generated code to run somewhat slower than
  regular project assemblies.
- **Async entry points** (`async Task` / `Task<T>`) are awaited to completion; `Task<T>.Result`
  becomes the command result and exceptions in the async work surface as runtime errors. The await
  is **asynchronous** — the Editor main thread keeps pumping, so awaits that resume on Unity's
  main-thread context (`Task.Yield()`, `Task.Delay()`, Awaitable, …) work naturally; no
  `ConfigureAwait(false)` contortions are required. The wait is bounded by what remains of
  `timeout`; on expiry the command returns a `Timeout` error and the task keeps running
  detached (its effects may still land), like an abandoned `script eval`.
- **hotpatch specifics:** `entry`, `args` and `dry-run` are rejected (Bad Request); `references` and
  `defines` are not applied. Path resolution follows `script.methodreload reload`, which probes under `Assets/`
  for bare filenames (unlike ephemeral's project-root resolution). Timing: `executeMs` carries the
  whole reload duration (compile + apply, not split by `script.methodreload reload`); `compileMs` is `0`. Advisory
  reload diagnostics are surfaced as severity-`warning` entries on success.
- Under Unity's Mono runtime the in-memory assembly emitted by an executing `ephemeral` run is
  **not** collectible/unloadable, so each run retains one small assembly (the same behavior as
  `script eval`); prefer a single builder entry over many tiny runs. `dry-run` loads nothing.

#### Examples

Build a grid of cubes whose size is chosen per run. `AgentScripts/Grid.cs`:

```csharp
using UnityEngine;

public static class Grid
{
    public static int Build(int columns, int rows, float spacing)
    {
        var root = new GameObject("Grid");
        for (var x = 0; x < columns; x++)
            for (var z = 0; z < rows; z++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"Cell_{x}_{z}";
                cube.transform.SetParent(root.transform);
                cube.transform.position = new Vector3(x * spacing, 0, z * spacing);
            }
        return columns * rows;
    }
}
```

```bash
unity command script run --file AgentScripts/Grid.cs --entry Grid.Build --args '[4, 3, 1.5]'
# { "result": 12, "compileMs": ..., "executeMs": ..., "diagnostics": [], "assemblyName": "PipelineRunScript_Grid_..." }
unity command script run --file AgentScripts/Grid.cs --args '[10, 10, 2]'   # entry omitted: Build is the only public static method
unity command script run --file AgentScripts/Grid.cs --dry-run              # compile only: diagnostics, nothing runs
```

A project audit called with different thresholds. `AgentScripts/Audit.cs`:

```csharp
using System.Linq;
using UnityEditor;

public static class Audit
{
    public static string[] LargeTextures(int maxSize)
    {
        return AssetDatabase.FindAssets("t:Texture2D")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => AssetImporter.GetAtPath(path) is TextureImporter importer && importer.maxTextureSize > maxSize)
            .ToArray();
    }
}
```

```bash
unity command script run --file AgentScripts/Audit.cs --entry Audit.LargeTextures --args '[1024]'   # paths of textures imported above 1024
unity command script run --file AgentScripts/Audit.cs --entry Audit.LargeTextures --args '[2048]'
```

#### The builder pattern (bulk construction)

For bulk construction — creating many objects, wiring private fields, generating content — put the
logic in a **versioned project script** and invoke it with `script run`. Write the file with
`asset write_text` (ideally under a non-`Assets/` folder such as `AgentScripts/` so the write triggers
no asset import), then call `script run --file AgentScripts/Build.cs --entry Build.All`. Iterating the
script and re-running it costs an in-memory compile (< ~2s), **not** a 15–20s domain reload.

The anti-patterns this replaces: carrying construction code as escaped C# strings through `script eval`
(verbose, unreviewable, shell-escaping hazards), and editing a builder under `Assets/` in a loop
(each edit pays a full domain reload). Code goes in files on disk. `script eval --file` runs a file as
a script, with `using` directives, declarations and `await`; `script run` calls a named entry point
with arguments, extra defines, or in-place method replacement.

See [Creating commands](../creating-commands.md) and [Connectivity](../connectivity.md).

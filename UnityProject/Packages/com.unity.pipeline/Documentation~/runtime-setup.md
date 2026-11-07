# Runtime connection & setup

How to run the Pipeline server inside a built Player so a client can drive it the same way it drives the Editor.

> **Experimental.** The Runtime Pipeline workflow may change in future versions. It currently targets desktop (Windows, macOS, Linux) Player builds; other platforms are not officially supported yet.

Two toggles in **Project Settings → Pipeline → Runtime** decide which builds contain the Pipeline:

| Toggle | Build it applies to |
|--------|---------------------|
| **Enable in Development & Profiled Builds** | Builds that define `UNITY_INCLUDE_INSTRUMENTATION` or `ENABLE_PROFILER`: development builds, and from Unity 6000.6 those with a Debug, Checked or Instrumented managed-code variant |
| **Enable in Release Builds** | Every other build |

The toggles are independent: turning on release builds alone leaves development builds without the Pipeline. A build whose toggle is off contains no Pipeline code at all — not the runtime assemblies (`Unity.Pipeline`, `Unity.Pipeline.IlInterpreter`), and not the bundled Roslyn DLLs.

Each toggle writes a scripting define symbol — `ENABLE_RUNTIME_PIPELINE_DEV` and `ENABLE_RUNTIME_PIPELINE` respectively — for every installed build target, so flipping one recompiles the project. You never need to type either symbol: the toggle is the supported way to set it.

Enable release builds deliberately. Such a build carries a local HTTP server that executes commands, available to anyone who can reach the machine.

## Configuring the runtime server

The runtime server is configured entirely through **Project Settings → Pipeline → Runtime**, which **Window → Pipeline → Runtime Settings...** opens directly. No GameObject or component needs to be added to any scene — `RuntimePipelineBootstrap` creates the runtime driver automatically at boot (Player start, or entering Play Mode in the Editor) whenever the Pipeline is in that build.

Entering Play Mode follows the **Enable in Development & Profiled Builds** toggle, since Play Mode stands in for a development build.

The fields below are never written as a file under `Assets/` — they live in `ProjectSettings/Packages/com.unity.pipeline/RuntimePipelineConfig.json`, a normal, tracked part of `ProjectSettings/` (the same place Unity itself keeps most other project-wide settings). Merely viewing the Project Settings page, or reading via `settings.runtime_pipeline get`, shows built-in defaults but never creates this file — it's written the first time you actually change a setting (through the page, or a confirmed `settings.runtime_pipeline set`). If the file does not exist, a build uses the defaults.

The two build toggles are not stored there. They are scripting defines, which is what keeps the assemblies out of a player, and they live in Unity's own Player settings.

**Security warning:** whenever either toggle is on, the settings page shows a persistent warning naming the build types that will carry the HTTP server and remote code execution.

### Settings

| Field | Default | Meaning |
|-------|---------|---------|
| `autoStart` | `true` | Start the server automatically when the Player boots. |
| `port` | `0` | Listen port. `0` = auto-assign from `7900`–`7949`. |
| `enableAuditLogging` | `true` | Log remote requests for auditing. |
| `maxWorkItemsPerFrame` | `10` | Dispatcher work items processed per frame. |

### Method reload on IL2CPP devices and managed stripping

An IL2CPP player only contains the managed members the build kept. Method reloaded onto such a device can call those, and a push warns about any member it references that the build stripped ("… not in the binding and will throw if reached"). The package does not change the project's stripping by itself.

To widen what reloaded code may reach, the settings page's **Method Reload** section can **Generate link.xml** into `ProjectSettings/Packages/com.unity.pipeline/MethodReload.link.xml` (also `script.methodreload generate_link_xml` from the CLI). It preserves, per type with all members: every engine and package type the game's own assemblies reference, the game's own types, the interpreter's host surface and the `[MethodReload]` targets. It never preserves whole engine modules or whole user assemblies, so a type the game never touches stays strippable; preserve such a type with the project's own `link.xml` under `Assets/` if needed. The file is applied to **development** builds only, and only while **Enable in Development & Profiled Builds** is on. It is a snapshot: **Regenerate link.xml** after larger code changes, or **Delete link.xml** to fall back to plain stripping.

If `autoStart` is off, drive the server manually via `RuntimePipelineBootstrap.Instance` — the driver `RuntimeInitializeOnLoadMethod` already created at boot — and call `StartServer()`/`StopServer()` on it. `RuntimePipelineBootstrap.Bootstrap()` is idempotent (a second call returns the existing instance rather than creating a duplicate), so it's also safe to call directly if you're not sure whether bootstrap has run yet.

### Generated build-only assets

Each build generates two **transient** assets under `Assets/Settings/Pipeline/Resources/`, both deleted again once the build finishes:

- `RuntimePipelineConfig.asset` — your current settings, baked in so the Player can find them (the authored copy stays in `ProjectSettings/`, never here).
- `RuntimePipelineBuildInfo.asset` — the method-reload allowlist: absolute paths to `Assets/` plus every loaded package's resolved location, captured at build time because a running Player can't resolve the project layout on its own. Read by `RuntimePipelineBootstrap` at boot; absent (e.g. Play Mode without ever having built) just means no method-reload roots are allowed, not an error.

Neither should ever be committed — add this to your project's `.gitignore`:

```
/Assets/Settings/Pipeline/Resources/RuntimePipelineConfig.asset*
/Assets/Settings/Pipeline/Resources/RuntimePipelineBuildInfo.asset*
```

A build interrupted mid-way (crash, force-quit) can leave either behind; if you see them in `git status` after such an interruption, it's safe to delete them. The next build you run will also delete them itself, at the very start of preprocessing, before anything else — so a leftover from an interrupted build can never get packaged into a later Player, even one built with both toggles off.

## How the gating works

The runtime assemblies and the bundled Roslyn DLLs carry define constraints equivalent to:

```
UNITY_EDITOR
  || (ENABLE_RUNTIME_PIPELINE_DEV && (UNITY_INCLUDE_INSTRUMENTATION || ENABLE_PROFILER))
  || (ENABLE_RUNTIME_PIPELINE     && !(UNITY_INCLUDE_INSTRUMENTATION || ENABLE_PROFILER))
```

A scripting define cannot vary between a development and a release build of the same target, so each toggle has its own symbol and the constraint pairs it with a symbol Unity itself emits per build. From Unity 6000.6 that is `UNITY_INCLUDE_INSTRUMENTATION`, which the Debug, Checked and Instrumented managed-code variants define and Release does not; below 6000.6 that symbol does not exist and `ENABLE_PROFILER`, which tracks the Development Build setting, carries it. Naming both covers the whole supported range without a version check. When a build does not satisfy the constraint, the code is not compiled into it and nothing in the Player can start a server.

## Step-by-step: enable in a dev build

1. Open **Project Settings → Pipeline → Runtime**.
2. Tick **Enable in Development & Profiled Builds**. Leave `autoStart` on and `port` at `0` (auto). The project recompiles.
3. Open **File → Build Settings** and enable **Development Build**.
4. Build and run the Player.
5. On start, the bootstrap creates a `RuntimePipelineServer`, which binds the first free port in `7900`–`7949` and writes the runtime descriptor `.unity-pipeline-runtime-port` (with `port` and `evalToken`).
6. Connect a client using that port and token.

## Security

The runtime server applies the same protections as the editor server (see [Connectivity](connectivity.md)):

- It binds the **IPv4 loopback** (`http://127.0.0.1:<port>/`) and nothing else, so it is reachable only from the same machine and never exposed on a routable interface. Clients must dial `127.0.0.1`: a request sending `Host: localhost` is answered `400`. See [Loopback-only binding](connectivity.md#loopback-only-binding).
- Every request must present `Authorization: Bearer <evalToken>`; the token is generated at startup and published in the runtime descriptor.

Because the server exposes code evaluation and method reload, only enable it in development/QA builds — never in a production build without additional safeguards.

## See also

- [Connectivity](connectivity.md) — ports, descriptor file, and auth in detail.
- [Method reload](method-reload.md) — applying live code changes to a running Player.

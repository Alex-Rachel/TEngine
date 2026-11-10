# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.10.0-exp.1] - 2026-11-02

### Compatibility

- Set the package minimum Unity version to 2022.3. Unity 6-only commands remain guarded by version checks and are omitted or report `feature_unavailable` on older editors.
- Use the legacy Editor analytics API before Unity 2023.2 and the matching physics material type on Unity 2022.
- Repaint Game views for screen captures on Unity 2022 when the newer internal rendering property is unavailable.
- Support the Unity 2022 Console and Test Framework APIs, and preserve inherited material render queues when reading properties.
- Use the Unity 2022-compatible PluginImporter v2 format for the five bundled compiler DLLs, preserving their platform settings and constraints.

### Added

- [AUTHAPI-104] The Pipeline Runtime settings page warns that the runtime workflow is experimental and currently targets desktop Player builds only.
- [EAD-2430] An importer that produces PNG previews of assets on demand, up to 512×512.
- The Editor advertises the Pipeline protocol transport in its capabilities.
- [MTT-16535] Add a top-level networking tag with a netcode subtag, and guidance on shipping commands from another package.
- Command-reference.md.
- [AUTHAPI-55] `editor status` reports the domain reload count and the Auto Refresh setting.
- [UUM-159061] Window > Pipeline menu items to open the Pipeline Editor and Runtime settings pages.
- [UUM-159061] A main toolbar toggle to start and stop the Pipeline Server, on Unity 6.3 and later.
- Commands to record Profiler captures and take memory snapshots from the Editor or a connected Player.
- [UUM-153627] `capture game_view` opens a Game view window when none is showing.
- [UUM-153627] Camera captures include Screen Space - Overlay UI on URP and HDRP, controlled by a new `overlay` argument.
- `capture editor_window` captures any open Editor window as drawn, found by title or type name.
- `capture scene_view` takes a `source`: `camera` (default) renders the Scene view camera, `screen` captures the Scene window as drawn.
- [AUTHAPI-112] A command's output can be formatted by the package and streamed to the `unity` CLI while the command runs, picked with `--format human|tsv|json`.
- [AUTHAPI-101] new analytics to get more data about eval usage.
- [AUTHAPI-101] diagnostics recent_analytics lists the last analytics events the Editor sent.
- [MTT-15607] `multiplayer.role` commands to read and set multiplayer roles for the Editor, objects, build profiles and component types on Unity 6000.6+.

### Changed

- The Pipeline protocol's server exit codes now start at 100, leaving 0–99 to commands.
- [AUTHAPI-74] Improve Async Eval.
- [UUM-159061] Moved the Window > Pipeline menu into the main tools section of the Window menu.
- [AUTHAPI-76] The Pipeline protocol transport returns the unity CLI's exit codes.
- [UUM-157059] A relative capture `save_path` resolves from the project root instead of `Assets/`, an absolute path is accepted, and `savedPath` is absolute.
- [AUTHAPI-112] `pipeline list_commands` and a bare group name list commands as the `unity` CLI lays them out, when the client asks for formatted output.
- [AUTHAPI-112] `component get_fields` takes `detail=value` in place of `format=value`.
- [AUTHAPI-112] `script run` lists a script's compile errors in its failure message.
- [AUTHAPI-112] A command can no longer name a parameter format, json, fields, verbose or omit-nulls; the executor takes those options from a command line.
- [DOCATT-11460] updating the samples page to make sure it matches the content model.
- [AUTHAPI-119] The editor server's settings are now stored per user in UserSettings/, no longer in version-controlled ProjectSettings/; an existing settings file is moved there.
- [AUTHAPI-119] An override from editor.playmode set_run_in_background now ends when the Editor quits, as documented.

### Deprecated

- `capture screenshot`; use `capture game_view` or `capture scene_view` with `save_path`.

### Removed

- [DOCATT-11505] removing the command index section from the index page.

### Fixed

- [UUM-157583] Fix editor closing in automated.
- [UUM-153626] Fix editor closing in batchmode.
- [UUM-157051] Fix editor closing in batchmode.
- [UUM-156858] The Editor server no longer leaves a stale listener behind after a script reload, and keeps the same port.
- [MTT-16498]`recompile` no longer reports success while a compile is still running.
- [MTT-16496] Clones added with `editor.playmode add_player` now launch on Unity 6000.5 and newer, and a fourth clone is refused with an error.
- [MTT-16497] `editor.playmode list_players` and `editor.playmode get_scenario_status` now list a scenario's players on Unity 6000.5 and newer.
- [UUM-151576] The Pipeline Server now starts in Multiplayer Play Mode virtual players when a fixed port is set.
- [UUM-159191] Fix warning when using Pipeline in a project with unity 6.7 or 7.0.
- [UUM-150763] Test result callbacks no longer accumulate across test runs when domain reload is disabled.
- [UUM-150763] Async test results now include tests that finished before a domain reload.
- [UUM-150763] An async test run no longer gets a duplicate result collector on the first run after a recompile.
- [UUM-153627] `capture game_view` works in batchmode: it opens a Game view on demand, and falls back to a camera capture, saying so in the result's message, when that view has no frame yet.
- [AUTHAPI-119] Overriding Run In Background no longer writes the override into ProjectSettings.asset when the project is saved or the Editor quits.
- [AUTHAPI-116] A quoted command name such as "test run" now runs the command instead of failing as not found.

## [0.9.1-exp.1] - 2026-10-22

### Changed

- `[CliCommand(name, description)]` reads a dotted name as group and name (`"animation.skeleton.set_bone"`), and is no longer obsolete.
- `[CliCommand(description)]` infers the group from the declaring type's namespace and class, and the name from the method.

### Fixed

- UAL0010/UAL0013 AutoStaticsCleanup analyzer warnings when opening a project with Unity 7.0.
- Building for Android, iOS or any other non-Standalone platform no longer throws `ArgumentException: Subtarget property is not available for the platform` in the Pipeline's pre-build step, which skipped baking the runtime settings into the build.

## [0.9.0-exp.1] - 2026-09-22

### Added

- [AUTHAPI-82] A `list_build_platforms` command that lists the platforms a Build Profile can target.
- [AUTHAPI-82] A `create_build_profile` command that creates a Build Profile asset for a platform.
- [AUTHAPI-82] An `edit_build_profile` command that changes a Build Profile's scene list and scripting defines.
- [AUTHAPI-84] Added new analytics data allowing cross reference between CLI events and Pipeline events.
- [CLI-1432] `editor_set_run_in_background` keeps the game running while the Editor is unfocused; builds keep the project's own setting.
- [CLI-1432] An "Ensure editor runs in background in PlayMode" setting under Project Settings > Pipeline > Editor.
- [CLI-1432] `editor_status` and `/api/status` report Run In Background and whether the player loop is advancing while in Play mode.
- [AUTHAPI-48] `/api/commands` can filter, group and index commands by group.
- [AUTHAPI-75] The instance descriptor lists every way to connect to the instance.
- [AUTHAPI-77] Every command accepts `--verbose` and `--omit-nulls` on the command line, shaping its result as the `verbose` and `omitNulls` request flags do.
- [AUTHAPI-48] `[CliCommand(group, name, description)]` and `[CliGroupCommand(group, description)]` declare a command in a group.
- The Editor accepts commands over the Pipeline protocol, on a loopback port it lists in its instance descriptor, as well as over HTTP.
- `pipeline progress` and `pipeline list_commands` commands, which report the running command's progress and list the registered commands.
- Project Settings > Pipeline > Editor > HTTP Service settings to turn off the Editor's HTTP server or pin its port.

### Changed

- AUTHAPI-81 simplify how a user can link with Pipeline for a Build. A single options available in the Project Settings take care of enabling pipeline for Dev build or for Release build.
- Code Reload is now Method Reload. Attributes, namespaces, doc updated.
- [AUTHAPI-48] Commands are grouped: run them as `unity command <group> <name>`; the old names no longer work.
- [AUTHAPI-48] The first argument of `[CliCommand]` is now the command's group, and the name comes from the method.
- [AUTHAPI-48] `CommandRegistry.RegisterCommand` takes the group and the name first.
- [AUTHAPI-48] When two commands share a name, the package's own command is kept and the other is refused.
- [AUTHAPI-75] The instance descriptor refreshes its heartbeat every 15 seconds and is replaced in one step, so it is never read half-written.
- [AUTHAPI-75] In Play Mode, the runtime server is listed in the Editor's descriptor instead of writing its own file.
- [AUTHAPI-58] Ensure timeouts are per command and that any command supporting timeout properly uses the --timeout switch to allow timeout overrides.
- Moved the unity-pipeline agent skill out of the package to the repository root.
- [AUTHAPI-77] The editor_status and test-status endpoints no longer wait for the running command to finish; editor_status gives up after 60 seconds if the main thread stays blocked.
- [AUTHAPI-77] The limit of 100 waiting or running commands now counts synchronous `/api/exec` requests as well as jobs, and a request over it returns 429.
- [AUTHAPI-77] A detached job with a missing or invalid argument is refused when it is submitted, instead of failing after it starts.
- [AUTHAPI-48] A bare command name runs the `global` command of that name, including one named like a group.
- `editor status` and `test status` answer at once while another command is running.

### Deprecated

- [AUTHAPI-48] `[CliCommand(name, description)]` registers the command in the `global` group; it still runs by its name alone.

### Removed

- [AUTHAPI-48] `CliCommand.RuntimeOnly`; tag the command `runtime` instead.
- [AUTHAPI-77] The `GetBusyReason` and `OnCommandDone` hooks of `BasePipelineServer`, and the `CommandExecutionInfo` type they reported with.
- [AUTHAPI-77] `/api/exec` replies no longer include `dialogsDuringExecution`; `/api/dialog` and `/api/status` still report open dialogs.

### Fixed

- `create_asset` and `find_assets` resolve short type names such as "Material" correctly on Unity 7.
- [UUM-154017] An unknown command name suggests the closest match, or the commands starting with it, instead of listing every registered command.
- [UUM-154657] Allow browser connection from localhost.
- Paths starting with "assets/" or "packages/" in any casing are treated as project paths instead of being nested under the authoring root.
- [UUM-154806] Entering Play Mode no longer logs errors when the Test Framework assembly cannot be loaded.
- Add proper guidance if an old naming convention command is invoked.
- [AUTHAPI-106] Update license agreement.
- [SCP-2254] Unity pipeline package no longer fails the build when including development version of it into runtime builds when building development player with the release managed code variant. It will now only be included in debug, checked or instrumented managed code variants.

### Security

- The server refuses requests whose Host header is not its own 127.0.0.1 address, blocking DNS rebinding from a web page.

## [0.8.0-exp.1] - 2026-09-22

### Added

- IL interpreter: delegates naming an interpreted class now run. Single-target only.
- IL interpreter: `new string(char[])` and the `BitConverter` bit-casts are now callable.
- `/api/commands?detail=tags` returns one row per tag with its command count instead of the commands; combining it with `group_by` or `sort=package` is refused.

### Changed

- [UUM-152774] `capture_game_view` now defaults to `source=screen` and works in Edit Mode; pass `source=camera` for the previous behaviour.
- [UUM-153151] `[CliCommand]` and `[CliArg]` moved to the unconstrained `Unity.Pipeline.Attributes` assembly; reference it instead of `Unity.Pipeline` to declare commands.
- [UUM-153151] `CommandRegistry` is now public, allowing dynamic registration. Discovery and caching stay internal.
- The Player command surface is identified by the `runtime` tag instead of being hidden, so an Editor server lists those commands; `batch` still refuses them and no public signature changed.
- Development player builds no longer get a generated code-reload `link.xml`; the project's own stripping level applies, and a push warns which referenced members are missing.
- The bundled Roslyn compiler ships only to the Editor and desktop players; `reload_file` reports the platform as unsupported elsewhere.
- The Editor and Runtime servers answer only requests addressed to `127.0.0.1`; dial the port by IP, not `localhost`.
- [AUTHAPI-79] Samples moved to the separate `com.unity.pipeline.samples` package; add it to get them.
- [UUM-152382] `simulate_key` and `simulate_pointer` now report whether the Input System is missing or excluded by Active Input Handling.
- [AUTHAPI-66] The Editor server is configured from Project Settings > Pipeline > Editor, not an asset; the old menu item is gone and `EditorPipelineManager.asset` is not migrated.
- A runtime settings file replaced by another process is picked up within 500 ms rather than on the next frame.
- [AUTHAPI-43] The package is now made available under the Unity Terms of Service instead of the Unity Package Distribution License.

### Fixed

- [UUM-153645] Editing the Project Settings > Pipeline pages no longer marks the open scene as unsaved.
- `RuntimePipelineDriver.Update` no longer allocates every frame in the Editor and Play Mode. Player builds were unaffected.
- IL interpreter: correctness fixes to 64-bit values, struct copies, virtual dispatch, casts, `ToString`, delegates, parameter typing and malformed assemblies.
- [UUM-152380] The package assembly no longer fails to compile with CS0234 on Unity 6000.3.0a1 through 6000.3.0a4.
- [UUM-152630] The nine `*_status` commands return their payload as real JSON instead of a JSON string; the fields are unchanged, so a client no longer parses the result twice.
- [UUM-152630] `build_status` reports a real `elapsedMs` while a build runs; it was always `0` outside UTC.
- `get_lighting_settings` / `set_lighting_settings` now compile against Unity 6000.7+, where `LightingSettings.realtimeGI` became obsolete.
- The occlusion-culling bake commands now compile against Unity 6000.7+, where `StaticOcclusionCulling` became obsolete. They keep working.
- The runtime dev-server code is gated on `ENABLE_PROFILER` instead of the deprecated `DEVELOPMENT_BUILD` (UAC0009), so an instrumented non-development player build now includes it.
- Code reload now binds targets applied from Edit Mode instead of reporting them all as missing.
- A code-reloaded method with no dispatch target now names the cause instead of always blaming stale compiled code.
- `codereload_status` and `cleanup_codereload` are now available on the Editor server, not only with a runtime driver.
- [UUM-152953] Entering Play Mode no longer scans every loaded assembly with reflection to find `[CodeReload]` methods in the Editor; discovery now uses `TypeCache.GetMethodsWithAttribute`.

### Security

- The Editor and Runtime servers now bind to `127.0.0.1` instead of every interface; the port was previously reachable from the local network, though every request still required a loopback origin and the bearer token.

## [0.7.0-exp.1] - 2026-09-10

### Changed

- [AUTHAPI-71] The Pipeline runtime assemblies and the bundled Roslyn DLLs are no longer included in non-development Player builds. Enabling the runtime server for a release build now has no effect instead of warning. Add the `ENABLE_RUNTIME_PIPELINE` scripting define symbol to include them in a build that is not a Development Build.
- [AUTHAPI-71] The code reload attributes moved to their own `Unity.Pipeline.Attributes` assembly, so code tagged with them still compiles for a release build, where they do nothing. An assembly definition that referenced `Unity.Pipeline` to get the attributes must add `Unity.Pipeline.Attributes` to its references; scripts that are not in an assembly definition need no change.
- [UUM-151473] `console` is now robust across domain reloads, Editor restarts and play-mode transitions. It reports the capture session alongside its cursor and refuses a cursor it cannot honor rather than returning an empty result; it backfills from the Editor console's own store, so compile errors logged before capture started are visible; and it re-arms itself when Unity drops the log callback. Every response also carries the Editor's own console counts and compile-failure flag, so a client can tell in one call when the buffer and the Console window disagree.
- [UUM-151473] `clear_console` moved to the runtime assembly beside `console` and `console_status`, so all three console commands live in one file and `clear_console` now works in player builds too (where it clears the captured buffer; there is no Editor console to clear).

### Added

- [AUTHAPI-25] Added server-side condition waiting: a wait command that polls an editor/object state until it holds (or times out), synchronously or in the background with status polling and cancelation.
- [AUTHAPI-51] `CommandRegistry.RegisterCommand`/`UnregisterCommand`: register commands at runtime without a `[CliCommand]` attribute, for command sets that depend on runtime state. A dynamic registration behaves exactly like an attribute-declared one — same `/api/commands` metadata, same `[CliArg]` parameter discovery on the handler, same `/api/exec` execution — and additionally accepts an instance-bound delegate, so a command can run against a specific object. Registrations survive a discovery-mechanism swap and last until unregistered or the domain reloads — one made while in Play Mode is dropped when Play Mode exits, so a handler registered from `Awake` re-registers cleanly on the next entry even with domain reload disabled. A name already in use throws rather than shadowing the existing command. See `Documentation~/creating-commands.md`.
- [UUM-151473] `console` responses carry `counts` (entries the buffer retains per severity) and `groundTruth` (the Editor's own `compilationFailed`, `compiling`, console entry counts, whether the buffer was seeded, and the sample's `ageMs`), so a client can spot the buffer disagreeing with the Console window in one round trip.
- [UUM-151473] New `console_status` command: the same counters and compile-failure flag without the entries, cheap enough to poll while a compile runs.
- [UUM-151473] `console` entries carry `logType` (Unity's exact `LogType` name, keeping the Exception/Assert distinction that `level` collapses) and `seeded` (read from the Editor console store rather than captured live, so the timestamp is the read time).
- [UUM-151473] `recompile_status` reports `compilationFailed`.

### Fixed

- [UUM-151473] Fixed `console` returning an empty result that was indistinguishable from "nothing new" when the `since` cursor came from an earlier Editor session. Sequence numbers restart with the process (the buffer is persisted under `Temp/`, which Unity deletes at every launch), so such a cursor filtered out every entry held. Responses now carry a `session`, `console` accepts `since_session`, and a cursor that cannot be honored returns the tail with `reset=true` and `dropped=true`.
- [UUM-151473] Fixed `ConsoleLogBuffer.Load` discarding entries logged since the snapshot it restores, and lowering the sequence counter when the snapshot was stale. It now merges by sequence number and never rewinds a cursor already handed out.
- [UUM-151473] Fixed `console` never reporting Editor compile errors logged before capture started. Those are logged as sticky entries that survive every console clear until the next compile completes, so they can stand in the Console window indefinitely; the buffer is now backfilled once per session from the Editor console's own store.
- [UUM-151473] Fixed console capture staying dead after Unity unregistered the native log callback with no observable symptom. The Editor now samples the console's own counts and re-arms capture when they move while the buffer's cursor does not.
- [UUM-151473] Fixed `recompile` reporting `up_to_date` while compile errors were still standing. A failed editor compile suppresses the domain reload and leaves the errors in place, so a repeat call with nothing changed compiled nothing and erased the recorded failure. Both `recompile` and `recompile_status` now consult Unity's native compile-failure flag, which also covers the status file being deleted with `Temp/` at every Editor launch.
- [UUM-151473] Fixed `clear_console` clearing only one of the package's two console buffers, so `console` kept returning entries that had been cleared.
- [UUM-151968] The runtime instance descriptor now writes to `Application.persistentDataPath` on Android instead of beside `Application.dataPath`, which is read-only there.
- [AUTHAPI-49] `Pipeline_SessionStopped` now fires on `EditorApplication.wantsToQuit` instead of `EditorApplication.quitting`, since the Analytics service is already shutting down by the time `quitting` runs and the event was never actually sent.

### Removed

- [UUM-151473] Removed `get_console_logs`, which duplicated `console` over a second buffer of its own — every log line was captured twice, and `clear_console` cleared only one of them. Use `console`; its new `logType` field carries the exact log type `get_console_logs` reported.

## [0.6.0-exp.1] - 2026-08-31

### Changed

- Hot reload is now called **code reload** throughout. The attributes are `[CodeReload]`, `[OnCodeReload]` and `[CodeReloadOverrideMethod]` in namespace `Unity.Pipeline.CodeReload`; the registry is `CodeReloadRegistry`; the commands `hotreload_status` and `cleanup_hotreload` are now `codereload_status` and `cleanup_codereload` (tag `scripts/codereload`); the sample lives under `Samples~/CodeReload`; the documentation page is `code-reload.md`. The watcher and the folder push now pick up `.cs` files that mention `CodeReload` instead of `HotReload`. The `[HotReload]` family no longer exists, so user code must be updated.
- [AUTHAPI-49] `BasePipelineServer.OnTransactionProcessed(requestJson, responseJson)` is replaced by `OnCommandDone(in CommandExecutionInfo)`, which also carries the command that ran, whether it succeeded, and how long it took. A detached job now reports when it actually completes, not only when its job handle was returned.
- [UUM-149580] Reorganized the Pipeline Runtime settings page (Project Settings > Pipeline > Runtime) into "Server" and "Runtime Behavior" groups, moved the status/security HelpBox to the top, and added an always-visible feature description when there's nothing to warn about. All fields except Max Work Items Per Frame are now read-only while in Play Mode, since edits to them would silently not apply until the next Play session or build; Max Work Items Per Frame stays live-editable — `RuntimePipelineDriver` polls it fresh every frame (cheaply, gated on the settings file's timestamp), matching the pre-redesign component's behavior for that field.
- [UUM-149580] Every field except Enable In Builds itself is now greyed out on the Pipeline Runtime settings page while Enable In Builds is off, instead of staying editable for a server that will not run.
- `reload_file` result contract, stated: a reload that compiles but applies NO methods now fails with the per-method reasons (previously a silent success); a file whose `[HotReload]` methods all match the compiled baseline reports an explicit "up to date" success — naming any stale overrides it reverted — instead of "hot reload successful with 0 methods".
- [AUTHAPI-26] A non-positive `timeout`/`timeout_ms` on `eval`, `eval_file`, or `run_script` now returns a clear `400 Bad Request` instead of an opaque dispatcher timeout.
- Reformatted CHANGELOG.md to more accurately track [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
- [AUTHAPI-59] Test fixtures and test-only helper classes under `Tests/` are now `internal` instead of `public` — they were never part of the package's public API surface.
- [AUTHAPI-59] Command handler classes and their result/DTO types under `Editor/Commands/` and `Runtime/Commands/` are now `internal` instead of `public`. Command registration and dispatch work via reflection regardless of accessibility (see `Documentation~/creating-commands.md`), so this was never functionally required.
- [AUTHAPI-59] `EditorPipelineManager`, `EditorPipelineManagerEditor`, `PipelineServerStartup`, `EditorPipelineServer`, `PipelineRuntimeBuildProcessor`, `HotReloadInPlaceILPostProcessor`, and `SecurityTokenManager` are now `internal` instead of `public`. `[CustomEditor]`, `[InitializeOnLoad]`, the build pipeline, and `ILPostProcessor` discovery all work via reflection regardless of accessibility, and none of these types were referenced outside the package.
- [AUTHAPI-59] `RuntimePipelineConfigEditor`, `RuntimePipelineSettingsProvider`, `PushEnvelope`, `PushVerifier`, `PushVerifyStatus`, `RotatingFileBackup`, and the `Unity.Pipeline.Telemetry` eval-usage helper types are now `internal` instead of `public` — same rationale: `[CustomEditor]`/`[SettingsProvider]` discovery is reflection-based, and none of these are referenced outside the package.

### Removed

- The separate-override-file hot reload workflow: the `reload_file_override` command, the `[HotReloadWithOverrides]` attribute, `HotReloadHelper`, and the `HotReload_WithHelpers` sample. Tag methods `[HotReload]` and edit them in place with `reload_file` instead (see `Documentation~/hot-reload.md`). The unused `[HotReloadableComponent]`, `[HotReloadComponent]` and `[HotReloadTarget]` attributes are gone too.

### Fixed

- Code reload: a `var` local whose inferred type is nested in the reloaded class (`var inp = new BufferedInput { ... }`) no longer fails to compile with "The type name 'var' does not exist in the type ..."; a `using` alias for such a type is left alone too.
- Code reload: when a reload fails to compile, each diagnostic now names the user's file and line, the compiler error code, and the generated line that failed, and the full generated source is written to `Temp/CodeReload/<assembly>.failed.cs` for inspection.
- Code reload in the Editor no longer depends on the runtime Pipeline server being enabled. `[CodeReload]` targets were only discovered by the runtime driver, which is not created when `enableInBuilds` is off (the default), so an in-editor reload compiled but bound 0 methods with a misleading "added after the running build" reason. Entering Play Mode now registers the targets either way, and an empty registry is reported as such.
- Interpreter backend: `new S { x = 1 }` and `default(S)` on a host struct bound boxed (the auto-bind default, e.g. a `struct` nested in the reloaded MonoBehaviour) failed at runtime with "Non-static field requires a target". The default box is now created, both for `initobj` and for the initobj-free form Roslyn emits when every field is assigned (`S s; s.x = 1; s.y = 2;`), so object initializers, field-by-field construction and nullable-struct fields work in reloaded bodies. Reading or writing a host field on a null object now reports the field name and source line instead of the bare reflection error, and an exception escaping a reloaded body is logged with its script location before it propagates.
- `build`: a scripted build with `Development` in `options` no longer trips the "enableInBuilds without a Development Build" security dialog (the guard only looked at the Build Settings toggle, so a `build` command from the CLI stalled on a modal, or was cancelled).
- `reload_file_player_interpreter`: a push made before the player's signing handshake has arrived now waits up to 10 s for it instead of 2 s; the first push to a freshly connected IL2CPP player was dropped at 2 s.
- [UUM-149991] AutoStaticsCleanup: Fixed usage-analysis findings.
- [UUM-149991] Fixed `CommandRegistry` staying on reflection-based command discovery after exiting Play Mode without a domain reload; `PipelineServerStartup` now restores TypeCache discovery on `EnteredEditMode`.
- [UUM-149977] Removed the "Editor is not in automated mode" console warning. Now surfaced as `info` on the instance descriptor instead.
- [AUTHAPI-36] Fixed `TestResultCollector.RunFinished` throwing `InvalidOperationException` (double-complete) when a run had already been completed by `SetError`/`Cancel` (e.g. a timed-out or errored run): a late `RunFinished` is now a no-op and the original error/cancellation is preserved.
- [UUM-149580] The Pipeline server no longer permanently enables Player Settings "Run In Background" on start; the runtime driver now saves and restores the project's original value around start/stop.
- [UUM-149580] Transient `RuntimePipelineConfig`/`RuntimePipelineBuildInfo` assets left behind by an interrupted build are now purged at the start of every build, so a disabled build can no longer ship the runtime Pipeline server.
- [UUM-149580] Reading Runtime settings (Project Settings page, `get_runtime_pipeline_settings`, or a refused/dry-run `set_runtime_pipeline_settings`) no longer writes the settings file.
- [UUM-149580] `set_runtime_pipeline_settings` now rejects a `port`, `requestTimeoutMs`, or `maxWorkItemsPerFrame` outside the same bounds the Project Settings page enforces, instead of silently persisting an invalid value (a `maxWorkItemsPerFrame` of 0 permanently starved the dispatcher's work queue).

### Added

- [AUTHAPI-68] `POST /api/exec` accepts an unparsed command line as `commandLine` (a string the server tokenizes) or `argv` (pre-split tokens), binding positional and `--flag` arguments against the command's declared parameters server-side. Exactly one of `command`, `commandLine` or `argv` may be present, and `parameters` cannot accompany the raw forms. Argument failures return `400` with `errorCode: "INVALID_COMMAND_ARGS"`, a machine-readable `argProblems` array, and the command's `commandSchema`; successful raw-form replies echo the bound `parameters`. Servers advertise `exec.argv` / `exec.commandLine` in `capabilities` on `/api/status` and in the port descriptor. `command` is no longer a JSON-required property, so a body carrying none of the three forms is now rejected by request validation as `400 {"error":"Invalid Request","errorDetails":"Command name is required"}` instead of by deserialization as `400 {"error":"Invalid JSON"}`.
- [AUTHAPI-64] Added a new endpoint reporting currently-open modal dialogs, and command execution now reports any dialogs shown while it ran. Main-thread commands are rejected with a busy status while a dialog is open, and status polling reports that a dialog is blocking it. Requires a recent enough trunk build.
- [AUTHAPI-29] Instrument `eval`/`eval_file` usage: each call logs a JSONL record (timing, success, statement/expression shape, and an API fingerprint of top-level member accesses) to `<project>/Library/Pipeline/eval-usage.jsonl`, editor-only and local-first — raw source isn't stored unless `Store Eval Source` is enabled. Toggle via `Eval Telemetry Enabled`.
- [AUTHAPI-29] New `report_evals` command: ranks logged eval fingerprints by frequency and cross-references the command catalog to surface patterns not yet covered by an existing command.
- [AUTHAPI-49] Editor analytics: `Pipeline_SessionStarted` on the first command a client executes, `Pipeline_CommandExecuted` per command executed, and `Pipeline_SessionStopped` when the Editor quits. `isEvalWithExistingCommandAvailable` reuses the AUTHAPI-29 eval fingerprints and coverage map, so it agrees with `report_evals` by construction. Only commands shipped in the package report their name and tags; a project-declared command reports `<customUserCommand>` and no tags. See `Documentation~/analytics.md`.
- [UUM-149580] `runtime_status` now reports `pipelineDriver` (isServerRunning, actualPort, and the enableInBuilds/requestTimeoutMs/enableAuditLogging/autoStart/maxWorkItemsPerFrame values actually governing the running driver, not just what Project Settings currently shows) — `null` if no driver was ever bootstrapped.
- [UUM-149580] New `get_runtime_pipeline_settings`/`set_runtime_pipeline_settings` commands: structured, `confirm`/`dry_run`-gated read/write access to Pipeline Runtime settings (Project Settings > Pipeline > Runtime), matching the existing Audio/Graphics/Input/Physics/Player/Quality/Time settings command family. Refused while in Play Mode.
- [AUTHAPI-27] New `batch` command: run up to 200 ordered operations in one request, with later ops referencing earlier results. Transactional by default (single Undo step, rolled back on failure); supports `on_error`, `dry_run`, `time_budget_ms`, and detached `job` execution. See `Documentation~/commands/batch.md` for details.
- [AUTHAPI-26] New `run_script` command: compile a single C# file in memory (no domain reload) and execute a named static entry point. Supports `ephemeral` (compile→run→discard) and `hotpatch` modes, JSON-coerced args, custom references/defines, and `dry_run` compilation. Returns `{ result, diagnostics, compileMs, executeMs, assemblyName }`; runtime errors include source `file:line`.
- [AUTHAPI-21] Leaner `/api/exec` responses by default: compact JSON, no null envelope keys, no always-on metadata — a minimal success is just `{"success":true,"result":...}`. Use `"verbose": true` to restore the full envelope, or `"omitNulls": true` to also drop nulls inside `result`. Added `format="value"` to `get_serialized_fields` to return raw values instead of a full descriptor.
- [AUTHAPI-21] New `warnings` array on responses for non-fatal corrective guidance (e.g. an ignored/invalid query parameter).
- [AUTHAPI-21] `/api/job`, `/api/job/cancel`, and `/api/progress` now include null keys explicitly; pass `omit_nulls=true` to drop them.
- [AUTHAPI-21] `POST /api/exec` now rejects a literal JSON `null` body with a structured 400 instead of an internal error.
- Add an IL interpreter (IlInterpreter) that works in IL2CPP players and the editor. Opt in with the `reload_file_editor_interpreter` command or by pushing to a connected device with `reload_file_player_interpreter`; the hot-reload watch always uses it. A shipped Roslyn analyzer (MS002–MS023) marks the supported C# subset in `[HotReload]` method bodies.
- Add an `[OnHotReload]` attribute to react to code being hot-reloaded.
- Add a `.cs` change watcher, enabled from the Pipeline settings, that hot-reloads changed code without a domain reload — in editor play mode, or in a player connected via the profiler connection.

## [0.5.0-exp.1] - 2026-08-10

### Added

- [CLI-488] New `GET /api/progress` endpoint reporting the currently executing command's progress, served off the main thread so it responds even during a blocking command. Command authors report via `CliProgress.Report(...)` or `CliEditorProgress.DisplayProgressBar`; `UnityEditor.Progress` items are mirrored automatically. Powers live progress bars in the `unity` CLI. (with CLI-335)
- [CLI-335] Detached command jobs: `POST /api/exec` with `"job": true` runs in the background and returns a job id immediately; poll `GET /api/job?id=…` for state/progress/result. `POST /api/job/cancel` cancels a queued job or requests cooperative cancellation of a running one via the new `PipelineCancellation` API. Results are retained for 1 hour / last 100 jobs.
- [CLI-488] New `audit`/`audit_status` commands: run a Project Auditor static-analysis scan and poll it, producing an issue CSV. Works via reflection so the package still runs without Project Auditor installed; reports `unavailable` with install guidance when analysis rules are missing.
- [AUTHAPI-35] Report `settling` on `/api/status` and reject main-thread commands with `503 Server Busy` while the Editor is still importing/compiling after a cold start. Background commands (`recompile_status`, `package_status`, `console`, `editor_status`) stay servable throughout.

### Changed

- [CLI-335] Raised the `eval` timeout cap from 30 seconds to 24 hours.
- [CLI-335] The HTTP server now processes requests concurrently — `/api/status` and `/api/progress` answer immediately even during a long-running command. Command execution itself remains strictly serialized.
- [CLI-335] Hardening: a command's own timeout (not a fixed 60s) governs `/api/exec`'s wait; `editor_status`/`test_status` queue behind the exec gate; progress/job state is scoped per server instance; detached jobs are capped at 100 concurrent (further submissions get `429`).

### Fixed

- Fixed `executedAt` returning a zero-value instead of the actual execution time for `eval`/`eval_file`, `hot-reload`, and `run_tests`/`test_status`.
- [UUM-148802] Fixed `get_console_logs`/`clear_console`/`console` losing console capture after exiting Play Mode by re-subscribing to Unity's log callback on every play-mode transition.
- [UUM-148605] `set_autotick`'s enabled/interval state now persists across domain reloads via SessionState. Added a `persist` flag (default `true`) to opt out.
- [UUM-149016] Fixed `InvalidOperationException`s after repeated `run_tests` runs caused by a stale `TestResultCollector` still registered with Unity's TestRunnerApi.

## [0.4.0-exp.1] - 2026-07-23

- Persist the pipeline server auth token across editor domain reloads so long-lived clients (MCP/IDE) no longer get `401` after a recompile. (CLI-412)
- `capture_game_view` gains a `source` parameter: `camera` (default, unchanged) or `screen`. `source=screen` captures the composited game view backbuffer so Screen Space - Overlay UI (HUDs, menus) is included — the camera path never sees overlay canvases. Screen capture requires Play Mode; in Edit Mode it returns a clear error. (AUTHAPI-10)
- `capture_game_view`/`capture_scene_view` with `save_path` now return a path-only result (no base64) so agent tool results stay small; pass `include_inline_image=true` for the old behavior. Add `max_resolution` to cap the inline image size. (AUTHAPI-8)
- Object-reference string handles now accept authoring-root-relative asset paths (e.g. `Materials/Floor.mat`, not just `Assets/Materials/Floor.mat`): a relative string with a file extension is treated as an asset path and normalized under the authoring root, and a failed lookup now reports every strategy tried instead of a misleading hierarchy-path-only error. (AUTHAPI-9)

## [0.3.1-exp.1] - 2026-07-16

- Update docs

## [0.3.0-exp.1] - 2026-07-13

- Improve security by ensuring token usage and enforcing read control on the token.
- Fix all upm-pvp warnings.
- Fix Samples installation.
- All server works if the App is minimized (in the RunInBackground).
- Rework all docs.
- Improve connectivity regaridng IPv4 vs IPv6 support.
- Add `eval_file` command: evaluate C# code read from a `.cs` file on disk, as a file-based alternative to `eval` (which takes inline `code`). Both commands run the source through the same evaluation path.
- Add a large set of Editor automation commands for agentic content-pipeline control:
  - **Assets & files:** `create_asset`, `import_asset`, `move_asset`, `copy_asset`, `rename_asset`, `delete_asset`, `find_assets`, `create_folder`, `get_import_settings`, `set_import_settings`, `read_text_file`, `write_text_file`.
  - **Scenes:** `create_scene`, `open_scene`, `save_scene`, `save_all`, `list_open_scenes`, `set_active_scene`, `get_scene_hierarchy`, `add_scene_to_build`, `remove_scene_from_build`.
  - **GameObjects & components:** `create_gameobject`, `create_gameobjects`, `delete_gameobject`, `find_gameobjects`, `rename_gameobject`, `set_parent`, `set_transform`, `set_active`, `set_tag`, `set_layer`, `add_component`, `remove_component`, `get_component_properties`, `set_component_properties`.
  - **Prefabs:** `create_prefab`, `create_prefab_variant`, `instantiate_prefab`, `apply_prefab_overrides`, `revert_prefab_overrides`, `unpack_prefab`, `save_prefab_contents`.
  - **Scripts & serialized fields:** `create_script`, `attach_script`, `get_serialized_fields`, `set_serialized_field`.
  - **Selection & search:** `get_selection`, `set_selection`, `search`.
  - **Capture & screenshots:** `screenshot`, `capture_game_view`, `capture_scene_view`, `capture_editor_element`, `capture_runtime_element`.
  - **Build:** `build`, `build_status`.
  - **Console & diagnostics:** `console`, `clear_console`, `get_console_logs`, `get_performance_stats`.
  - **Editor menus & authoring root:** `menu`, `get_authoring_root`, `set_authoring_root`.
  - **Materials & shaders:** `get_material_properties`, `set_material_properties`, `get_shader_properties`, `list_shaders`.
  - **Animation & Animator:** `create_animation_clip`, `get_animation_clip`, `set_animation_curve`, `remove_animation_curve`, `create_animator_controller`, `get_animator_controller`, `add_animator_layer`, `add_animator_parameter`, `add_animator_state`, `add_animator_transition`.
  - **Timeline:** `create_timeline`, `get_timeline`, `add_timeline_track`, `add_timeline_clip`.
  - **Lighting:** `bake_lighting`, `cancel_lighting_bake`, `lighting_bake_status`, `clear_baked_lighting`, `get_lighting_settings`, `set_lighting_settings`.
  - **NavMesh:** `bake_navmesh`, `bake_navmesh_surfaces`, `cancel_navmesh_bake`, `navmesh_bake_status`, `clear_navmesh`, `get_navmesh_settings`, `set_navmesh_settings`.
  - **Occlusion culling:** `bake_occlusion_culling`, `cancel_occlusion_bake`, `occlusion_bake_status`, `clear_occlusion_culling`.
- Update Wrench
- Warn user if Unity Editor is started in non-automated mode.

## [0.2.0-exp.2] - 2026-06-24

- Fix security audit flaws
- First official published version

## [0.1.0-exp.1] - 2026-06-09

### This is the first release of _Unity Pipeline_.
### Fixed

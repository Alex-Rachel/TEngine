# Build, compilation & test commands

These commands drive the build/compile/test dev loop, plus build configuration: triggering Player builds and reading the resulting BuildReport, switching the active build target, enumerating targets, reading/writing EditorUserBuildSettings, and listing, creating and editing Build Profiles. Several commands are asynchronous: trigger the operation, then poll the matching status command (`build status`, `build.target switch_status`, `script recompile_status`, `test status`) until it reports completion. The client must tolerate connection errors during a domain reload (recompile, target switch) or a long blocking build.

### `build run`
Trigger an async Player build and report the full BuildReport. Returns immediately (queued); poll `build status` until status is 'completed'. DetailedBuildReport is included by default unless 'options' is supplied. Use dry-run to validate without building.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | no | `–` | BuildTarget name (e.g. StandaloneWindows64). Defaults to the active target. Must be installed. |
| `output-path` | no | `–` | Output path (absolute, or relative to the project root). Defaults to the last/auto path. |
| `profile-name` | no | `–` | Build Profile name to activate before building (Unity 6 only; ignored otherwise). |
| `options` | no | `–` | BuildOptions names. Omit to get just DetailedBuildReport; supplying any disables that default. |
| `scenes` | no | `–` | Scene asset paths to build (e.g. Assets/Scenes/Main.unity). Defaults to EditorBuildSettings. |
| `confirm` | no | `false` | Acknowledge and run the build; without it the call is refused. Use `dry-run` to validate only. |
| `dry-run` | no | `false` | Validate target/output-path/scenes without building. |

Supported `options` values (case-insensitive): `Development`, `AllowDebugging`, `ConnectWithProfiler`, `EnableHeadlessMode`, `SymlinkSources`, `BuildAdditionalStreamedScenes`, `CleanBuildCache`, `DetailedBuildReport`. Any unrecognized value is a validation error.

**Returns:** `object` (transient payloads: `queued` / `busy` / `dry_run` / `error`; the finished report is read via `build status`).
**Notes:** `MainThreadRequired = false`. Async — validates + queues, returns `queued` immediately, then an editor tick runs the (blocking) build; poll `build status` until status is `completed`. Only one build at a time (returns `busy` otherwise). Mutating: gated by the `confirm`/`dry-run` convention — pass `confirm=true` for a real build, or `dry-run=true` to validate target/outputPath/scenes without building (nothing is queued on a dry run or on validation failure).

### `build status`
Status of the current/most recent build: idle | queued | building | completed, with the full BuildReport (files, packedAssets, buildSteps, errors, warnings) once completed. Retained until the next build.

No parameters.

**Returns:** `object`. `idle` when no build has run; `queued`/`building` (the latter with live `elapsedMs`) while in progress; a full `BuildReportResult` once `completed`.

`BuildReportResult` fields (returned when `status` is `completed`):

| Field | Description |
|-------|-------------|
| `status` | Always `completed` here. |
| `buildId` | Id returned by the triggering `build run` call. |
| `result` | `Succeeded` \| `Failed` \| `Cancelled` \| `Unknown`. |
| `platform` | Build target the report is for. |
| `outputPath` | Final output location. |
| `totalSizeBytes` | Total build size in bytes. |
| `buildTimeMs` | Total build duration in milliseconds. |
| `buildStartedAt` / `buildEndedAt` | ISO-8601 timestamps (omitted when unset). |
| `totalWarnings` / `totalErrors` | Aggregate counts from the report summary. |
| `files` | Output files (path, role, sizeBytes). Present only on success. |
| `packedAssets` | Packed bundles with per-asset breakdown. Present only on success and requires DetailedBuildReport. |
| `buildSteps` | Per-step name, durationMs, depth, and messages. |
| `errors` / `warnings` | Build issues, each with `message` and best-effort `file`. |

**Notes:** `MainThreadRequired = false`. Reads a Temp status file off-thread, so polling keeps working while a build holds the main thread. The file also survives the domain reload a build can incur, so the last report is retained until the next build.

### `build.target switch`
Switch the active build target (destructive, long-running: triggers a full reimport + domain reload). Requires confirm=true. Returns immediately; poll `build.target switch_status`.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | `–` | BuildTarget name to switch to (must be installed; see `build.target list`). |
| `confirm` | no | `false` | Apply the switch. Without it the call is refused. |

**Returns:** `object` (`switching` / `busy` / `completed` / `error`). Returns `completed` immediately if already on the requested target.
**Notes:** `MainThreadRequired = false`. Async — validates + queues, returns `switching` immediately, then an editor tick performs the (blocking) switch; poll `build.target switch_status` until `completed`. Only one switch at a time (returns `busy` otherwise). Mutating and confirm-gated: without `confirm=true` the call is refused. The status file survives the domain reload the switch causes, and is reconciled against the active target on the next load.

### `build.target switch_status`
Status of the last target switch: idle | switching | completed (with success + activeBuildTarget).

No parameters.

**Returns:** `object`. `idle`, `switching`, or `completed` (with `success` and `activeBuildTarget`, or `errors` on failure).
**Notes:** `MainThreadRequired = false`. Reads a Temp status file off-thread, so it keeps answering while the switch holds the main thread.

### `build.target list`
List the known BuildTarget values with their group and whether build support is installed.

No parameters.

**Returns:** `object` (list of `BuildTargetInfo`: `name`, `displayName`, `targetGroup`, `isInstalled`), sorted by group then name. Obsolete and sentinel targets are excluded.
**Notes:** `MainThreadRequired = true`.

### `build get_settings`
Read the current build configuration from EditorUserBuildSettings / EditorBuildSettings.

No parameters.

**Returns:** `object` (`BuildSettingsResult`).

`BuildSettingsResult` fields:

| Field | Description |
|-------|-------------|
| `active-build-target` | Active BuildTarget name. |
| `active-build-target-group` | Active BuildTargetGroup name. |
| `development-build` | Whether a Development Player is configured. |
| `allow-debugging` | Whether script debugging is allowed. |
| `connect-with-profiler` | Whether the Profiler auto-connects. |
| `build-scripts-only` | Whether only scripts are built (skip data). |
| `symlink-sources` | Whether runtime/plugin sources are symlinked. |
| `il2cpp-code-generation` | `OptimizeSpeed` \| `OptimizeSize` for the active target. |
| `scenes` | Build Settings scene list (each: `path`, `guid`, `enabled`). |

**Notes:** `MainThreadRequired = true`.

### `build set_settings`
Set mutable EditorUserBuildSettings fields. Does NOT manage scenes (use `scene add_to_build` / `scene remove_from_build`) or switch target (use `build.target switch`). Use dry-run to preview.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `settings` | no | `–` | Fields to change; omitted fields are left unchanged. |
| `confirm` | no | `false` | Apply the changes. Without it the call is refused. |
| `dry-run` | no | `false` | Preview the change without applying it. |

`settings` fields (a structured input DTO; every field is optional — only supplied fields change):

| Field | Description |
|-------|-------------|
| `development-build` | Build a Development Player (enables the debugger/profiler). |
| `allow-debugging` | Allow script debugging (only effective with development-build=true). |
| `connect-with-profiler` | Auto-connect the Profiler (only effective with development-build=true). |
| `build-scripts-only` | Build only the scripts (skip data) for faster iteration. |
| `symlink-sources` | Symlink runtime/plugin sources instead of copying (where supported). |
| `il2cpp-code-generation` | IL2CPP code generation for the active target: OptimizeSpeed \| OptimizeSize. |

**Returns:** `object` (`SetBuildSettingsResult`: `success`, `dryRun`, `applied` map of fields that changed, `skipped` map of supplied fields that already matched, `message`).
**Notes:** `MainThreadRequired = true`. Mutating: refused unless `confirm=true` (or `dry-run=true` to preview). Fields already at the requested value are reported under `skipped` rather than re-applied. Fails if no `settings` object is provided.

### `build list_profiles`
List Build Profile assets in the project (Unity 6 only). Returns feature_unavailable on earlier versions.

No parameters.

**Returns:** `object` (list of `BuildProfileInfo`: `name`, `guid`, `platform`, `isActive`), or `{ error, code = "feature_unavailable" }` on editors older than Unity 6.
**Notes:** `MainThreadRequired = true`.

### `build list_platforms`
List the platforms a Build Profile can target, with their platform id (Unity 6 only). Use the `displayName` or `id` with `build create_profile`.

| Parameter | Type | Description |
|-----------|------|-------------|
| `show-all` | `bool` | Include platforms whose build support module is not installed. Defaults to `false`. |

**Returns:** `object` (list of `BuildPlatformInfo`: `displayName`, `id`, `buildTarget`, `subtarget`, `isInstalled`, `isDerived`), or `{ error, code = "feature_unavailable" }` on editors older than Unity 6.
**Notes:** `MainThreadRequired = true`. By default only platforms with build support installed are listed, since those are the only ones `build create_profile` will accept; pass `show-all=true` to see the rest. A Build Profile platform is not the same thing as a `BuildTarget` — Meta Quest, Android XR and Android all report `buildTarget: "Android"`, with the first two flagged `isDerived`. Use `id` to select one unambiguously.

### `build create_profile`
Create a Build Profile asset for a platform (Unity 6 only). Call `build list_platforms` first to get a platform id.

| Parameter | Type | Description |
|-----------|------|-------------|
| `name` | `string` | **Required.** Profile name (e.g. `"Meta Quest"`). A Build Profile is named after its file, so this is also the file name and the name `build run --profile-name` matches. |
| `platform` | `string` | **Required.** Platform id, display name (`"Meta Quest"`) or `BuildTarget` name (`"Android"`). |
| `subtarget` | `string` | Standalone only: `Player` \| `Server`. Disambiguates desktop platforms; ignored for platforms that have no Player/Server split. |
| `folder` | `string` | Folder to create the profile in. Defaults to `Assets/Settings/Build Profiles`. The file is always `<folder>/<name>.asset`. |
| `activate` | `bool` | Make the new profile the active Build Profile after creating it. Defaults to `false`. Only allowed when the profile's platform uses the editor's active build target: activating another target would switch it, so switch first with `build.target switch`. |
| `confirm` | `bool` | Required (true) only when overwriting an existing profile of the same name in the folder. |
| `dry-run` | `bool` | Validate the name, platform and folder and report what would be created, without writing anything. |

**Returns:** `object` (`CreateBuildProfileResult`: `success`, `dryRun`, `assetPath`, `platform`, `profile` (omitted on a dry run), `activated`, `message`), or `{ error, code = "feature_unavailable" }` on editors older than Unity 6.
**Notes:** `MainThreadRequired = true`. `platform` is resolved as id, then exact display name, then `BuildTarget` name — the last of which only matches a non-derived platform, so `"Android"` never selects Meta Quest. Refuses a platform whose build support module is not installed, since the resulting profile would be unusable. A `name` the command cannot use is refused rather than rewritten: one containing `/` or `\` (on every host, since either would put the profile in a subfolder), one containing a character this host cannot put in a file name, and a location Unity does not import (a name starting with a dot, or a folder inside the project starting with a dot or ending in `~`). Whether a name also works on the team's other operating systems — Windows reserved names such as `CON`, or characters only Windows refuses — is the caller's choice, as it is in the Build Profiles window. A name already used by a profile in another folder is refused, because `build --profile-name` matches by name. With `confirm=true`, an existing profile is overwritten in place: its settings are replaced but it keeps its GUID, so references to it — including the active-profile setting — stay valid. Overwriting the active profile with a different platform is refused, since the in-place copy would skip the build-target switch; a non-profile asset at the path is never overwritten, nor is a file there that Unity cannot load (corrupt, or not imported yet) — it is refused rather than created around under another name. On a Unity 6 editor whose internal Build Profile API has changed, `build list_platforms` and `build create_profile` return `feature_unavailable` naming the missing members, rather than an empty or wrong platform list. `activate=true` for a profile on another build target (or another standalone subtarget) is refused, because activating it would perform a full reimport and domain reload inside the create; switch with `build.target switch` first (confirm-gated, polled with `build.target switch_status`), then create with `activate=true`. A derived platform on the active target, such as Meta Quest on an Android project, activates without a switch. If activation itself fails after the profile is written, the command still succeeds and reports `activated: false` with the reason. Missing parent folders are created. To build with the new profile without activating it, pass `build --profile-name`.

### `build set_profile`
Edit an existing Build Profile's scene list and scripting defines (Unity 6 only). Every setting is optional; only the ones supplied change. The platform cannot be changed here: recreate the profile with `build create_profile` and `confirm=true`.

| Parameter | Type | Description |
|-----------|------|-------------|
| `name` | `string` | **Required.** Name of the profile to edit (see `build list_profiles`). |
| `folder` | `string` | Folder holding the profile. Only needed when several profiles share the name. |
| `override-global-scenes` | `bool?` | Build with the profile's own scene list instead of the global Build Settings list. Omit to leave unchanged. |
| `scenes` | `string[]` | Scene asset paths in build order, replacing the profile's scene list; all are enabled. An empty list clears it. Omit to leave unchanged. |
| `scripting-defines` | `string[]` | Scripting define symbols replacing the profile's own. An empty list clears them. Omit to leave unchanged. |
| `confirm` | `bool` | Accepted for parity; not required, since the edit is undoable. |
| `dry-run` | `bool` | Validate the changes and report what would change, without writing anything. |

From the CLI, pass lists as JSON: `unity command build set_profile --name "Meta Quest" --scripting-defines '["QUEST","DEBUG_OVERLAY"]' --override-global-scenes true`.

**Returns:** `object` (`EditBuildProfileResult`: `success`, `dryRun`, `assetPath`, `profile`, `applied` map of settings that changed, `skipped` map of supplied settings that already matched, `message`), or `{ error, code = "feature_unavailable" }` on editors older than Unity 6.
**Notes:** `MainThreadRequired = true`. Every supplied value is validated before anything is written, so one bad value leaves the profile untouched: scene paths must be scene assets anywhere in the project, `Packages/` included, like `build --scenes` — a scene in the list is only referenced, so the authoring root does not confine it (bare paths still resolve against the root, and the `.unity` extension is optional) — and a rejected path names its reason, and defines must be valid C# preprocessor symbols. A name shared by several profiles is refused until `folder` picks one. The profile itself is what the command writes, so it must lie under the authoring root, however it was found. The edit is saved to the asset and reverts as a single Undo step; undo and redo are saved too — including an undo of a `batch` that edited several profiles in one step — so the file on disk always matches what the editor shows. In a transactional `batch` the command is refused, since its disk write is outside Undo. Known limitation: `build --profile-name` does not use the profile's scene list yet — it builds the global Build Settings list even when `override-global-scenes` is on (AUTHAPI-91). Until that is fixed, pass the scenes to `build --scenes` explicitly. The message points out a scene list that builds will ignore because `override-global-scenes` is off, and defines changed on the active profile, which alter script compilation. Platform settings and the player-settings override are not editable: Unity keeps them internal.

### `script recompile`
Force a script recompile (works while unfocused/minimized). Poll `script recompile_status` for completion.

No parameters.

**Returns:** `object`
**Notes:** `MainThreadRequired = true`. Async — poll `script recompile_status` until status is `completed` or `up_to_date`. A successful compile triggers a domain reload, so the triggering request cannot stay open.

### `script recompile_status`
Get the status of the last recompile: idle | triggered | compiling | completed | up_to_date.

No parameters.

**Returns:** `object` — `status`, `failed`, `errors`, `compilationFailed`.
**Notes:** `MainThreadRequired = false`.

### `test list`
List all available tests (EditMode and/or PlayMode) without running them.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `mode` | no | `all` | Test mode: all, editor, playmode (default: all) |

**Returns:** `TestListResponse`
**Notes:** `MainThreadRequired = true`.

### `test run`
Execute Unity tests with filtering options.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `mode` | no | `all` | Test mode: all, editor, playmode (default: all) |
| `filter` | no | `–` | Test name filter pattern (case-insensitive partial match) |
| `filter-type` | no | `testName` | Filter type: testName, assembly, category (default: testName) |
| `include-explicit` | no | `false` | Include tests marked with [Explicit] attribute |
| `async-tests` | no | `false` | Run asynchronously - return immediately, poll /test-status for results |
| `timeout` | no | `600` | Test execution timeout in seconds |

**Returns:** `TestExecutionResponse`
**Notes:** `MainThreadRequired = true`. Synchronous by default; with `async-tests=true` it returns immediately — poll `test status` for results.

### `test status`
Get status of running async test execution.

No parameters.

**Returns:** `object` — the run's status payload, or `{"status":"no_tests"}` when no run is in progress.
**Notes:** `MainThreadRequired = false`. Immediate: it answers at once while another command runs.

### `test cancel`
Cancel running test execution.

No parameters.

**Returns:** `object`
**Notes:** `MainThreadRequired = true`.

See [Creating commands](../creating-commands.md) and [Connectivity](../connectivity.md).

# Editor lifecycle & observability commands

Commands to control Editor play mode, focus, and menus, and to observe the Editor's state, console, and performance.

### `editor.playmode start`
Enter Unity Editor play mode.

No parameters.

**Returns:** `string`
**Notes:** An Editor driven from a terminal is usually unfocused. Without `Application.runInBackground`, its player loop stops at the first frame while `editor status` still reports `"playing"`. `editor.playmode start` no longer changes `Application.runInBackground` itself — use [`editor.playmode set_run_in_background`](#editorplaymode-set_run_in_background) or the "Ensure editor runs in background in PlayMode" project setting instead.

### `editor.playmode set_run_in_background`
Override the project's Run In Background Player Setting so the Play-mode player loop keeps running while the Editor is unfocused.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `enabled` | no | `true` | Turn the override on (`true`), recording the project's own value so it can be restored later — or stop the override and restore the project's own value (`false`), unless the "Ensure editor runs in background in PlayMode" setting is still forcing it on right now. |

**Returns:** `string`
**Notes:** The override lasts until reverted or until the Editor quits. Its record lives in the same `UserSettings/Packages/com.unity.pipeline/EditorPipelineConfig.json` file as the rest of the editor server's settings, which is per-user and not version-controlled. If you turn Run In Background off yourself while the override is active, your value wins and the override ends; run the command again to bring it back. An Editor restart reloads the project's own value, which reads the same way, so an override from the command does not survive a restart. It stacks with, and survives independently of, the "Ensure editor runs in background in PlayMode" project setting (Project Settings > Pipeline > Editor): either one alone is enough to force Run In Background on, and it comes back off only once neither still calls for it. In the Editor, Run In Background is the project's Player Settings value — a build always uses the project's own value regardless of either override, and saving the project or quitting the Editor writes the project's own value to `ProjectSettings/ProjectSettings.asset`, so neither override can leak into a shipped build or into version control.

### `editor.playmode stop`
Exit Unity Editor play mode.

No parameters.

**Returns:** `string`

### `editor.playmode pause`
Toggle pause state of Unity Editor play mode (calling it again while paused resumes play mode).

No parameters.

**Returns:** `string`

### `editor status`
Get detailed Unity Editor status and state information.

No parameters.

**Returns:** `StatusResponse`
**Notes:** Immediate: it doesn't wait for the commands before it, only for the main thread. In Play mode the response also carries `frameCount` (`Time.frameCount`) and `playerLoopTicking` (whether the frame count advanced in the last second, `false` while paused). Both keys are absent in Edit Mode. The response always carries `runInBackground` (`Application.runInBackground`, the setting; `playerLoopTicking` is whether frames actually advance). `/api/status` carries the same three keys in Play mode only; see [Play mode and the player loop](../connectivity.md#play-mode-and-the-player-loop).

`editor status` also always carries:

- `codeLoadCount`: how many times scripts have loaded in this Editor session. It is 1 after the Editor starts and goes up by one each time scripts reload. If it changes between two reads, scripts reloaded in between. It resets when the Editor restarts.
- `autoRefresh`: the Editor's Auto Refresh preference, `"enabled"`, `"disabled"`, or `"enabled_outside_play_mode"`. When it isn't `"enabled"`, Unity doesn't pick up new or changed scripts on its own, so they don't compile until the Asset Database refreshes; run `script recompile` to refresh it.

### `editor focus`
Bring the Unity Editor window to the foreground.

No parameters.

**Returns:** `string`
**Notes:** `MainThreadRequired = true`.

### `editor invoke_menu`
Execute an Editor menu item by path, or list available items when no path is given.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `path` | no | `–` | Menu item path to execute, e.g. "Assets/Reimport All". Omit to list available menu items. |

**Returns:** `MenuResponse`
**Notes:** `MainThreadRequired = true`. `File/Exit` is refused; use `editor quit`.

### `editor quit`
Quit the Editor without a save prompt. Fails if scenes or a prefab have unsaved changes, unless `--save` or `--discard` is passed.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `save` | no | `false` | Save every dirty open scene and the open prefab before quitting. Fails if a dirty scene has never been saved. |
| `discard` | no | `false` | Quit without saving unsaved scenes or prefab changes. |
| `exit-code` | no | `0` | Exit code for the Editor process |

```bash
unity command editor quit             # fails and lists the unsaved scenes, if any
unity command editor quit --save      # saves them, then quits
unity command editor quit --discard   # drops them, then quits
```

**Returns:** `EditorQuitResult` (`quitting`, `exitCode`, `saved`, `discarded`)
**Notes:** `MainThreadRequired = true`. The Editor exits shortly after the reply is sent. In Play Mode, `--save` is refused; stop Play Mode first or pass `--discard`. Not batchable.

### `capture screenshot`
Deprecated: use [`capture game_view`](capture.md#capture-game_view) or [`capture scene_view`](capture.md#capture-scene_view)
with `output-path`. They write the file with the same path rules, and the game view capture also includes
Screen Space - Overlay UI, which this command misses. Every result carries a deprecation warning in `warnings`.

```bash
unity command capture game_view --output-path Screenshots/x.png   # instead of: capture screenshot --view game --output Screenshots/x.png
```

Capture the Scene or Game view as a PNG and return its file path.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `view` | no | `game` | Which view to capture: 'game' (default) or 'scene' |
| `output-path` | no | `–` | Output PNG path (absolute, or relative to the project root). Defaults to a timestamped file under <project>/Temp/pipeline-screenshots/. |
| `width` | no | `0` | Output width in pixels. 0 (default) uses the view camera's current width. |
| `height` | no | `0` | Output height in pixels. 0 (default) uses the view camera's current height. |

**Returns:** `ScreenshotResponse`
**Notes:** `MainThreadRequired = true`.

### `editor set_auto_tick`
Keep the editor ticking while unfocused by forcing EditorApplication.SignalTick at a throttled rate.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `enabled` | no | `true` | Turn auto-tick mode on (true) or off (false) |
| `interval-ms` | no | `16` | Minimum milliseconds between forced ticks. 0 = every update (max rate, pegs a CPU core). Default 16 (~60Hz). |
| `persist` | no | `true` | Persist this choice to SessionState so it survives a domain reload. Set `false` for a one-off/expensive setting (e.g. `interval-ms=0`) that should revert to the last persisted choice (or the default) after the next recompile instead of sticking for the rest of the session. |

**Returns:** `string`
**Notes:** `MainThreadRequired = true`. Persisted in SessionState by default: your enabled/interval choice survives a domain reload (dies with the editor process/session).

Use `persist=false` for a change you only want in effect until the next unrelated recompile — e.g. a short profiling run at `interval-ms=0` (pegs a CPU core) or a one-off test/CI script — so it can't outlive its purpose and silently burn CPU for the rest of the session. Leave `persist` at its default `true` for a setting you want to remain in effect across recompiles, such as the normal always-on 16ms tick.

### `diagnostics get_performance`
Read render, memory, and frame-timing stats (structured, read-only).

No parameters.

**Returns:** `PerformanceStats`

### `diagnostics run_audit`
Run a Project Auditor static-analysis scan. Returns immediately; poll `diagnostics audit_status` until status is `completed`, then read the CSV.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `categories` | no | `–` | Comma-separated issue categories to scan (e.g. `Code,ProjectSetting,Texture`). Default: all categories. An unknown name is rejected with an error listing the valid values. |
| `output-path` | no | `–` | CSV output path (absolute, or relative to the project root). Defaults to `<project>/Temp/pipeline-audit/<scanId>.csv`. |

**Returns:** `object` — `{ status, scanId, csvPath }` on start, or `{ status: "unavailable" | "error" | "busy", message }`.
**Notes:** Requires Project Auditor in the Editor. Only one scan runs at a time (a second call returns `busy`). Not cancellable: stop polling to abandon a scan.

### `diagnostics audit_status`
Get the status of the last audit: `idle` | `scanning` | `completed` | `failed` | `interrupted` | `unavailable`.

No parameters.

**Returns:** `AuditStatus` (JSON) — `issueCount` and `csvPath` are populated when `completed`; `error` when `failed`; `message` when `unavailable`.
**Notes:** `MainThreadRequired = false`, so it answers while a scan holds the main thread (Code analysis compiles assemblies). `interrupted` means a domain reload killed an in-flight scan — re-run `diagnostics run_audit`.

#### Project Auditor prerequisites

`diagnostics run_audit` needs both Project Auditor **and** its rules:

- **Project Auditor absent** — `diagnostics run_audit` returns `unavailable` immediately.
- **Rules absent** — when Project Auditor ships as a built-in editor module, its rules (descriptors,
  API/obsolete databases, Roslyn analyzers) come from the separate `com.unity.project-auditor-rules`
  package. Without it Project Auditor registers no analysis modules and cannot analyze anything, so
  `diagnostics run_audit` returns `scanning` and the first `diagnostics audit_status` poll reports `unavailable` with a message
  naming the package to install. It never reports an empty `completed` scan, which would read as a
  clean project.

The CSV columns are `Category, Severity, Areas, Description, RelativePath, Line, DescriptorId, Recommendation`;
only diagnostics (things to fix) are emitted, not raw inventory rows.

### `diagnostics report_evals`
Aggregate the local eval-usage telemetry into a ranked report: API fingerprint frequency, one-liner percentage, error rate, and command-coverage suggestions (read-only). See [Eval usage telemetry](#eval-usage-telemetry) below.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `top` | no | `50` | Maximum entries in each ranked list: the fingerprint ranking and the uncovered patterns suggested as commands. Accepted range: 0 or greater; negative values fall back to the default. |

**Returns:** `EvalUsageReport`
**Notes:** `MainThreadRequired = false` (stays servable while the Editor is settling). Reads `<project>/Library/Pipeline/eval-usage.jsonl` merged with the rotated `eval-usage.old.jsonl` (oldest first), so the report covers the full retained history — bounded to roughly 2× the size cap. Returns a zeroed report if no evals have been recorded. The report's `distinctFingerprints` field carries the uncapped total of distinct fingerprints so truncation of the ranked list is always visible; coverage suggestions are computed from the full ranking, not the capped list.

## Eval usage telemetry

The `script eval` and `script eval_file` commands are the workhorse of autonomous agent sessions, but they are opaque: raw C# through the protocol, unauditable, and hard to displace with typed commands without knowing what agents actually run. This telemetry closes that loop by recording, **locally**, what each eval does — so the [eval-displacement epic (AUTHAPI-24)](https://jira.unity3d.com/browse/AUTHAPI-24) can be triaged from data instead of anecdotes.

### What is recorded, and where

Every `script eval` or `script eval_file` invocation appends one JSON object (one line, JSONL) to `<project>/Library/Pipeline/eval-usage.jsonl`:

| Field | Meaning |
|-------|---------|
| `time` | ISO-8601 UTC timestamp. |
| `command` | The command that ran: `script eval` or `script eval_file`. |
| `success` / `error` | Whether it succeeded; short error category on failure. |
| `payloadLength` / `lineCount` | Size/shape of the eval body. |
| `executionTimeMs` | How long it took. |
| `classification` | `single-expression` (a one-liner: one invoke / read / assignment) or `statements` (multi-statement bodies, e.g. polling loops, bulk setup). |
| `fingerprints` | The top-level member-access paths from the syntax tree — e.g. `AssetDatabase.Refresh`, `PlayerSettings.SetScriptingBackend`, `Object.FindFirstObjectByType<T>`, `MatchManager.Instance.State`. Rendering is a strict whitelist: the only source text that can appear in a fingerprint is an identifier, type name, or member name. Literals, interpolated strings, and any other expression form are replaced by the neutral `<expr>` placeholder (`"…".Length` records as `<expr>.Length`); object creations record as `new TypeName` (constructor arguments never appear); casts unwrap to the inner expression; generic type arguments normalise to `<T>`. A fingerprint that would be nothing but the placeholder is dropped. |
| `source` | The raw eval source — **absent by default**; present only when the opt-in below is enabled, and truncated at 64 K characters with an explicit `…[truncated]` marker (`payloadLength` still reports the full length). |

**Local-first, privacy-first, editor-only.** Nothing is transmitted off the machine, and nothing is recorded outside the Editor: player builds — including development players, where eval itself is available — never write telemetry (recording compiles to a no-op there). The fingerprint is derived from the C# syntax tree, so the log captures *which APIs* agents reach for without capturing *what data* they operate on. Conditional-access chains (`a?.B.C`) are fingerprinted with `?.` normalised to `.` so they rank with their unconditional form. The fingerprint deliberately undercounts a few forms (arguments nested inside a chain's receiver call, bare object creations) — see the `EvalUsageFingerprinter` class doc for the full blind-spot list before treating absence as proof of non-use.

**Zero eval latency.** Recording is fire-and-forget: when an eval completes, the source and outcome plus the current settings are snapshotted, and the parse → fingerprint → append runs on a background thread from that snapshot — the eval response is never delayed by telemetry. The JSONL is bounded by a size cap and rotates to `eval-usage.old.jsonl`; the rotated file is merged back into `diagnostics report_evals`, so retained history is bounded to roughly 2× the cap.

### Settings

Configured in **Project Settings > Pipeline > Editor**, which **Window > Pipeline > Editor Settings...** opens directly. Edits apply live, and the stored values are re-applied on every server start:

- **Eval Telemetry Enabled** (default on) — master switch. When off, nothing is recorded (and, as always, nothing is transmitted).
- **Store Eval Source** (default off) — the explicit opt-in to also persist the raw eval `source` in each record, for local debugging only.

### The feedback loop: report → backlog triage

Run `diagnostics report_evals` to aggregate the log into an `EvalUsageReport`:

- **Ranked fingerprints** with counts — what agents actually eval, most-used first.
- **One-liner percentage** and **error rate** — the shape of eval usage (the evidence base for AUTHAPI-24 is that ~80% of eval calls are one-liners).
- **Coverage suggestions** — each fingerprint is cross-referenced against the live command catalog:
  - `covered`: patterns already served by an existing command (e.g. `PlayerSettings.SetScriptingBackend` → `settings.player set`) — these are habit, and should move off eval.
  - `topUncovered`: the highest-frequency patterns with no covering command (e.g. `AssetDatabase.Refresh` → proposed `refresh_assets`) — these are the next typed commands to build ([AUTHAPI-17](https://jira.unity3d.com/browse/AUTHAPI-17)).

This turns backlog triage into a lookup: "12× `AssetDatabase.Refresh`, no command → build it; 60× state-read loops → `job wait`". Re-run across sessions to demonstrate eval's shrinking share as typed commands land.

### `authoring get_root`
Get the base folder (under Assets/) that bare authoring paths resolve against.

No parameters.

**Returns:** `object`

### `authoring set_root`
Set the base folder (under Assets/) that bare authoring paths resolve against and are confined to. Use 'Assets' for full project access.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `root` | yes | `–` | Project-relative folder under Assets/, e.g. Assets/AgentWork. Use 'Assets' to allow the whole project. |

**Returns:** `object`

See [Creating commands](../creating-commands.md) and [Connectivity](../connectivity.md).

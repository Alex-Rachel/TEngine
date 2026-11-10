# Connectivity

How a client reaches a running Unity instance: the loopback-only HTTP servers, their port ranges, the port descriptor file used for discovery, and the bearer-token authentication every request must carry.

The rest of this page covers the implementation. If you just want to connect, start with the two sections below.

## Connecting to a running Editor

Connections go through the `unity` CLI. Run `unity command` with no group or command name to connect to a Unity instance and list its command groups:

```bash
# Auto-discover a Unity instance from the current directory and list its command groups
unity command

# Connect to a specific project
unity command --project-path /path/to/your/unity/project
```

Once connected, run a command by its group and name, e.g. `unity command editor status`. `unity command <group>` lists the commands in a group.

### Starting and stopping the Editor server

The Editor server starts with the Editor unless **Auto Start** is off in **Project Settings > Pipeline > Editor**. To start or stop it by hand:

- **Window > Pipeline > Start Server** and **Stop Server**. Only the one that applies is enabled.
- The main toolbar's **Pipeline Server** toggle (Unity 6.3 and later). A green dot means the server is running and a red dot means it is stopped; the tooltip shows the port. Click it to start or stop the server. If it isn't on the toolbar, add it from the toolbar's element picker, where it is listed as **Pipeline/Server**.

The toggle and the menu items always agree, including after the settings page's **Start**, **Stop** or **Restart** buttons.

**Window > Pipeline > Editor Settings...** and **Runtime Settings...** open the matching **Project Settings > Pipeline** page.

## Connecting to a running Player (game)

To target a running development Player instead of the Editor, use `--runtime` (by process name) or `--runtime-path` (by the location of the runtime port file). These options go **after** `command` and **before** the group:

```bash
# By Player process/executable name
unity command --runtime MyGame.exe runtime status

# By the folder/bundle where the runtime port file lives
unity command --runtime-path "C:\Builds\MyGame" runtime status             # Windows: next to the .exe
unity command --runtime-path "/Users/me/Builds/MyGame.app" runtime status  # macOS: the .app bundle
```

> The Runtime server only runs in a **development Player build** with the runtime manager enabled — see [Runtime connection & setup](runtime-setup.md).

## Finding the port in the Unity logs

The [port descriptor file](#port-descriptor-file) is the primary discovery channel, but the server also reports itself to the Unity log when it starts and stops — useful when you can't read the descriptor file directly. Look for `Pipeline`-prefixed lines.

- **Runtime (Player)** — logs its port **and** the descriptor file location on start, and a line on stop:

  ```
  Pipeline: Runtime server started successfully on port 7901
  Pipeline: Descriptor written to /path/to/MyGame/.unity-pipeline-runtime-port
  Pipeline: Runtime server stopped
  ```

- **Editor** — logs its port when the server is (re)started from the **Window ▸ Pipeline ▸ Start Server** menu, the main toolbar's **Pipeline Server** toggle (Unity 6.3 and later; add it from the toolbar's element picker if it is not shown) or re-opened by its watchdog (`Pipeline Server started on port 7801`). Its descriptor always lives at the fixed `Library/Pipeline/.unity-pipeline-port` path.

### Where the logs live

**Editor log** (`Editor.log`):

| OS | Path |
|----|------|
| Windows | `C:\Users\<user>\AppData\Local\Unity\Editor\Editor.log` |
| macOS | `~/Library/Logs/Unity/Editor.log` |

**Player log** (`Player.log`):

| OS | Path |
|----|------|
| Windows | `C:\Users\<user>\AppData\LocalLow\<company>\<product>\Player.log` |
| macOS | `~/Library/Logs/<company>/<product>/Player.log` |

(`<company>` and `<product>` are the project's **Company Name** and **Product Name** from Player Settings.)

## Loopback-only binding

Both the Editor and Runtime servers register a single listener prefix, the **IPv4 loopback literal**:

```csharp
m_HttpListener.Prefixes.Add($"http://127.0.0.1:{m_Port}/");
```

The socket is therefore bound to `127.0.0.1` and is not reachable from another machine. This matters
more than it would for most servers, because `/api/exec` runs arbitrary code.

The prefix host is what chooses the socket address, and only an IP literal is safe. Unity's Mono ships
`HttpListener` as managed code over a plain socket, with no http.sys and therefore no URL ACL to fall
back on: `+` and `*` both resolve to `IPAddress.Any` — a bind on **every** interface — and a
hostname such as `localhost` is resolved through DNS, falling back to `IPAddress.Any` when resolution
fails.

As a second line of defence the server also rejects any request whose remote endpoint is not a loopback
address, before authentication and before routing. With the socket bound to `127.0.0.1` that check
should never fire; it covers a widened prefix and a hop that re-sources a connection as loopback.

In addition, the server refuses any request that carries an `Origin` header (legitimate CLI/CI clients never send one), which blocks a browser page from reaching the local server and short-circuits CORS preflights. The Editor server can opt into the single exception: enabling **Allow Browser Clients** in **Project Settings > Pipeline > Editor** (applies on next start) admits `Origin: null`, the opaque origin a sandboxed browser frame reports, and answers that client's CORS preflight. An ordinary page sends its real origin and is still refused, and a sandboxed one still needs the bearer token.

### Clients must dial `127.0.0.1`

Mono matches a request's `Host` header **literally** against the registered prefix, so `127.0.0.1` is
the only spelling the server answers. A request carrying `Host: localhost` is answered `400`, even
though it arrived on the loopback.

Compose the base URL as `http://127.0.0.1:<port>/` from the descriptor's `port` — never from
`localhost`, whichever way the host happens to resolve it.

With **Allow Browser Clients** enabled, a sandboxed browser frame (`Origin: null`) may also dial
`http://localhost:<port>/`. On Mono, and on CoreCLR outside Windows, the Editor server then registers
a `localhost` prefix too, but only when `localhost` resolves to loopback addresses alone, so the socket
is never widened. On Windows under CoreCLR no extra prefix is needed. Every other client
must still use `127.0.0.1`. Mono serves `localhost` only on the first address it resolves to (often
`::1`), so a browser that connects over `127.0.0.1` instead is answered `400`; if a browser client
fails to connect to `localhost`, have it dial `http://127.0.0.1:<port>/`.

IPv6 plays no part: Mono mis-parses a bracketed `[::1]` host and cannot serve the IPv6 loopback
whichever prefix is registered. Since `localhost` resolves to `::1` or `127.0.0.1` non-deterministically
(notably on Windows with Node's default DNS order), dialing it was already a source of intermittent
connection failures — `127.0.0.1` avoids the IPv6 path entirely.

## Port ranges

Each server type picks the first free port in its range (or you can pin one explicitly):

| Server | Production range | Test range |
|--------|------------------|------------|
| **Editor** | `7800`–`7849` | `7850`–`7899` |
| **Runtime** (Player) | `7900`–`7949` | `7950`–`7999` |

If no port in the range is free, startup throws (`No available ports in range …`).

Within an Editor session, an auto-assigned Editor server tries the port it was given earlier in the session first, so it keeps that port across script reloads and restarts as long as the port is still free.

The Editor's HTTP server is configured under **Project Settings > Pipeline > Editor > HTTP Service**: **Port** pins it (`0` auto-assigns), and turning off **Enable** stops it serving HTTP at all, leaving only the `pipeline` endpoint in the descriptor. Both apply on next start.

## Port Descriptor File

Each Editor and Player publishes a small JSON **instance descriptor** so clients can discover it and the ways to connect to it. This is the only discovery channel — there is no broadcast. Servers register their endpoint in-process (`PipelineEndpoints`), and `PipelineDescriptorPublisher` is the only writer of the file.

| Server | Descriptor path |
|--------|-----------------|
| **Editor** | `<projectPath>/Library/Pipeline/.unity-pipeline-port` |
| **Runtime** | `<workingDirectory>/.unity-pipeline-runtime-port` |

The Editor descriptor lives under the git-ignored `Library/` folder. The file exists while at least one endpoint is registered: it is written when the first server starts, rewritten whenever the set of endpoints changes and every 15 seconds to refresh `lastHeartbeat`, and deleted when the last server stops, before a domain reload, and on quit. The file is rewritten in place and is readable only by the current user (it carries the auth token). An instance never deletes a descriptor whose `pid` is another process that is still running. A file left by an instance that is gone is replaced when the next one starts, or removed if that one has nothing to advertise.

The Runtime descriptor is written next to the Player, at `{Application.dataPath}/..`:

- **Windows / Linux** — beside the executable, e.g. `C:\Builds\MyGame\.unity-pipeline-runtime-port`.
- **macOS** — `Application.dataPath` is `<Game>.app/Contents`, so the descriptor lands at the **`.app` bundle root**: `MyGame.app/.unity-pipeline-runtime-port`. (The file is inside the `.app` bundle.) This is why `unity command --runtime-path` takes the `.app` bundle path on macOS — see [Runtime connection & setup](runtime-setup.md).
- **Android** — `Application.dataPath` points inside the read-only APK install directory, so the descriptor is written to `Application.persistentDataPath` instead: `<persistentDataPath>/.unity-pipeline-runtime-port`.

> Test servers do **not** register an endpoint (they override `AdvertisesEndpoint => false`) — the test already knows its port, so it never shows up in the live server's file. See [Tests architecture](testing.md).

### Editor descriptor fields

```json
{
  "pid": 12345,
  "port": 7800,
  "projectPath": "/path/to/Project",
  "projectName": "Project",
  "unityVersion": "6000.x.y",
  "mode": "editor",
  "startedAt": "2026-06-25T10:00:00Z",
  "lastHeartbeat": "2026-06-25T10:05:00Z",
  "evalToken": "<base64 token>",
  "capabilities": ["exec.argv", "exec.commandLine", "exec.commandTimeouts", "commands.obsoleteNames", "transport.pipelineProtocol", "pipeline.terminal"],
  "info": "This editor has not been opened in automated mode and therefore could get stuck on modal dialogs. Human intervention may be required.",
  "endpoints": [
    { "type": "http", "port": "7800" }
  ]
}
```

| Field | Meaning |
|-------|---------|
| `pid` | Process id of the Unity Editor. |
| `port` | Port of the first `http` endpoint, for clients that predate `endpoints`; `0` when there is none. |
| `projectPath` | Absolute path to the Unity project. |
| `projectName` | Project folder name. |
| `unityVersion` | Editor version string. |
| `mode` | `"editor"` or `"batchmode"`. |
| `startedAt` | When the instance started (UTC). |
| `lastHeartbeat` | Last heartbeat (UTC), refreshed every 15 seconds while the instance runs. Clients treat one older than 60 seconds as an instance that is gone. |
| `evalToken` | Bearer token for authenticating requests. |
| `capabilities` | Optional wire features this server understands (see [Capability negotiation](#capability-negotiation)). **An absent key means the server predates the field** — treat it as supporting none of them. |
| `info` | Corrective guidance for the client, omitted when there is nothing to say. Currently only set when the Editor is interactive and wasn't launched with `-automated`: a blocking modal dialog can stall the Editor's main-thread update loop (and therefore all pipeline command processing) until a human dismisses it. Read this before issuing commands — it's the one field every client sees, since reading the descriptor is a mandatory first step just to connect. |
| `endpoints` | The ways to connect: each entry has a `type` (e.g. `http`) and that type's fields beside it, all strings. Absent in descriptors from versions that predate it — fall back to `port`. |

### Runtime descriptor fields

The runtime descriptor shares `pid`, `port`, `unityVersion`, `startedAt`, `lastHeartbeat`, `evalToken`, and `endpoints`, and adds:

| Field | Meaning |
|-------|---------|
| `platform` | Unity runtime platform (e.g. `WindowsPlayer`). |
| `buildGuid` | Unique build identifier (`Application.buildGUID`). |
| `workingDirectory` | Directory the Player is running from. |

The runtime descriptor also carries `capabilities`, with the same meaning and the same absent-key rule. A Player never advertises `commands.obsoleteNames`.

(The runtime descriptor carries `platform`/`buildGuid`/`workingDirectory` in place of the editor's `projectPath`/`projectName`/`mode`.)

## Authentication

Every request must authenticate with a bearer token:

```
Authorization: Bearer <evalToken>
```

- The server **generates the token at startup** (`SecurityTokenManager.GetOrCreateToken()` — 256 bits of CSPRNG output, base64-encoded). In the Editor the token is persisted in `SessionState`, so it **survives domain reloads** within an editor session (recompiles, entering play mode) — long-lived clients (MCP sessions, IDE integrations) keep working instead of getting `401` after every reload. It is regenerated only when the Editor **restarts**, or on an explicit rotation (`SecurityTokenManager.ClearCache()` / `RotateToken()`). Player builds use a per-process token.
- The token is published in the descriptor's `evalToken` field. The descriptor is **re-advertised with the live token on every heartbeat** (every 15 seconds), so after a rotation the port file catches up within one heartbeat.
- The server validates the bearer token on **every** request (before routing) using a constant-time comparison. A missing or wrong token returns `401 Unauthorized`.

## Discovering and calling an instance

A client connects by:

1. Reading the descriptor file (editor: `Library/Pipeline/.unity-pipeline-port`).
2. Taking the `port` and `evalToken` from it.
3. Sending requests to `http://127.0.0.1:<port>/...` with `Authorization: Bearer <evalToken>`. The host must be `127.0.0.1`.

Endpoints exposed by the server include `/api/status`, `/api/editor_status`, `/api/dialog`, `/api/commands` (lists available commands), `/api/exec` (POST — runs a command), `/api/test-status`, and `/api/progress`.

### Command progress (`GET /api/progress`)

While a command is executing over `/api/exec`, clients can poll `GET /api/progress` for the
task's live progress — the `unity` CLI uses this to render terminal progress bars, mirroring
`EditorUtility.DisplayProgressBar`. The endpoint is served off the Editor main thread, so it
answers even while a long synchronous command has the Editor blocked. Response
(`pct` is 0–1; all `progress` fields optional; `progress` is omitted when nothing is reported):

```json
{
  "active": true,
  "progress": {
    "title": "Generating World",
    "info": "Processing 42/100",
    "current": 42,
    "total": 100,
    "pct": 0.42
  }
}
```

Progress sources, in order of precedence:

1. `CliProgress.Report(title, info, current, total, progress)` — explicit reporting from
   command code; thread-safe and works from a blocked main thread.
2. Running `UnityEditor.Progress` items, mirrored automatically.

### Detached jobs (`/api/exec` with `"job": true`, `/api/job`, `/api/job/cancel`)

A long command can outlive the client's HTTP timeout. Submitting it as a **detached job**
returns a job id immediately; the command runs in the background (still one at a time, in
arrival order) and the client polls for the result — reattaching at any point:

1. `POST /api/exec` with body `{"command": "…", "parameters": {…}, "job": true}` → the
   standard exec envelope, with the job handle as its `result`:
   `{"success": true, "result": {"jobId": "…", "state": "queued"}}` (lean by default —
   add `"verbose": true` to get the `command`/`executedAt` metadata back, see AUTHAPI-21).
2. `GET /api/job?id=<jobId>` → `{"jobId", "command" (the full name), "state":
   "queued|running|completed|failed|canceled", "progress": {…}, "result", "error", …}`.
   `progress` mirrors `/api/progress` while the job runs; `result` is retained after
   completion (last 100 terminal jobs, 1 hour) so it can be fetched repeatedly.
3. `POST /api/job/cancel` with body `{"id": "<jobId>"}` — a queued job is canceled before it
   starts; a running job gets a cooperative cancellation flag that command or `script eval` code can
   observe via `Unity.Pipeline.PipelineCancellation.ThrowIfCancellationRequested()` (arbitrary
   synchronous code cannot be aborted from outside).

At most 100 commands may be waiting or running at once, jobs and synchronous `/api/exec` requests
together (commands execute strictly serially, so a deeper backlog is pure waiting with no
benefit) — a request beyond that, job or not, returns `429`.

Jobs live in Editor memory: they do not survive domain reloads (script recompilation).

Editor code that already calls `EditorUtility.DisplayProgressBar` can switch to the drop-in
`CliEditorProgress.DisplayProgressBar` / `ClearProgressBar` wrappers to keep the Editor dialog
and gain CLI visibility. See [Creating commands](creating-commands.md).

### Readiness and the settle window

On a **cold project import** the editor server comes up (and its descriptor is written) while the Editor is still importing assets and compiling scripts, so the Editor is not yet able to service commands. Until the Editor is first seen idle after server start:

- `/api/status` reports `"status": "settling"` instead of `"ready"`. Wait for `ready` before issuing commands.
- `/api/exec` rejects **main-thread** commands with **HTTP 503** and a structured, retryable envelope — distinguishable from a genuine command failure. The gate applies before execution *and* before a detached job (`"job": true`) is created, so a job can't run into the half-ready Editor in the background either. The busy reply is a standard exec envelope and follows the request's reply-shape flags: lean (the default, shown here) drops the envelope metadata; `"verbose": true` restores the `command` echo and `executedAt`:

  ```json
  {
    "success": false,
    "error": "Server Busy",
    "errorDetails": "The Editor is still settling after startup (importing assets / compiling scripts), so main-thread commands are not serviceable yet. Retry once it is ready.",
    "status": "busy",
    "busyReason": "settling",
    "retryable": true
  }
  ```

  `busyReason` is the specific cause, matching `/api/status`'s own `status` values for the same conditions (`"settling"`, `"blocked_by_dialog"`) — branch on it directly rather than inferring the cause from whether `dialogs` happens to be populated.

- Background commands (`script recompile_status`, `package status`, `console get`, ...) and `editor status` stay servable throughout, so progress remains observable.

The settle gate is one-way and scoped to the **editor session**: once the Editor has been idle once after startup, the server reports `ready` and the gate never arms again for that session — including for server instances recreated by domain reloads and for servers started while a mid-session compile/import happens to be in flight. Warm starts settle immediately; only the cold-import window gates.

### Play mode and the player loop

`"playMode": "playing"` means the Editor is in Play mode, not that the game is running. An unfocused Editor without `Application.runInBackground` stays in Play mode with its player loop stopped, and commands that render on demand, such as `capture game_view`, still return a frame. `editor.playmode start` itself never changes `Application.runInBackground` — call [`editor.playmode set_run_in_background`](commands/editor-lifecycle-and-observability.md#editorplaymode-set_run_in_background) before entering Play mode, or enable "Ensure editor runs in background in PlayMode" in Project Settings > Pipeline > Editor, so the setting isn't silently changed on your behalf. Either one is recorded so it can be undone, and neither one ever reaches a build — a Player build always uses the project's own Run In Background value.

In Play mode, `/api/status` and `editor status` add three keys. `/api/status` omits all three in Edit Mode, so the common response keeps its shape. `editor status` omits `frameCount` and `playerLoopTicking` in Edit Mode, but always reports `runInBackground`:

| Key | Meaning |
|-----|---------|
| `frameCount` | `Time.frameCount` as last sampled on the main thread. Two reads within one frame can match on a healthy loop, so compare reads at least a second apart, or use `playerLoopTicking`. |
| `playerLoopTicking` | `true` when the frame count advanced within the last second. `false` while paused, while the loop is frozen, and before the first frame advance of a session. This is about frames actually advancing, so it also covers pause and a stalled loop. |
| `runInBackground` | `Application.runInBackground` as last sampled: the setting, not the outcome. It can be `true` while `playerLoopTicking` is `false` (paused, stalled), and `false` means an unfocused Editor stops the loop. |

If you turn Run In Background off yourself while an override from `editor.playmode set_run_in_background` or the Play-mode setting is active, your value wins: the override ends, and it comes back only when you ask again (the command, or the next Play entry with the setting on).

A server that predates these keys never sends them. Treat an absent key in Play mode as "unknown", not as "not ticking".

### Modal dialogs and the dialog-busy gate

A native modal dialog (e.g. "Save changes before quitting?") or an `EditorWindow` modal blocks the Editor's main thread inside a nested OS message loop, so no main-thread command can run until it's dismissed. The server mirrors these dialogs from `UnityEditor.EditorDialogEvents`:

- `GET /api/dialog` reports the currently open dialog(s): `{"active": bool, "dialogs": [...]}`, each entry shaped `{id, source, title, message, level, buttons, openedAt, dismissedAt}`.
- `/api/status` reports `"status": "blocked_by_dialog"` (instead of `"ready"`) with a `"dialog"` key carrying that same shape for the first open dialog — so the cheapest, most commonly polled probe doesn't claim readiness while every main-thread command is actually gated.
- `/api/exec` rejects any **main-thread** command with **HTTP 503**, the same busy envelope shape as the settle gate's but with `"busyReason": "blocked_by_dialog"` and a `"dialogs"` array attached.

  **Exception: `editor status`.** Unlike every other main-thread command, `editor status` still returns a normal **200** while a dialog is open, served from a snapshot taken the instant before the dialog blocked the thread — nothing it reports (compiling, domain reload, play mode) can change while the thread that would update it is stuck. The player-loop keys are the exception: they are read live, and `playerLoopTicking` turns `false` while the dialog blocks the loop. The response carries `"status": "blocked_by_dialog"` and the same `"dialog"` payload as `/api/status`, so a caller can still tell it apart from a genuinely idle Editor.

Coverage is mechanism-based: native message boxes and `EditorWindow` modals are covered; OS file/folder pickers and other dialog mechanisms are not — a caller still needs a fallback signal (e.g. a command timeout) for uncovered cases.

`/api/commands` accepts optional query parameters for discovery:

- `detail` — `full` (the **default**) returns the complete command metadata including `parameters` and the generated `schema`; `compact` returns a lightweight index per command (`command`, `name`, `group`, `description`, `tags`, `package`); `groups` and `tags` return a **group index** or a **tag index** instead of commands (see below). The recommended discovery flow is to start from the group index, list one group's commands with `group=`, then request full detail only for the few commands you intend to invoke. The tag index and `tag=` do the same across groups. On the Editor, `obsolete_names` returns the **obsolete-name index** instead (see below).
- `query` — case-insensitive substring match on a command's full name, description, or any tag.
- `group` — scope results to a group and its subgroups via segment-aware prefix match (`group=build` matches `build` and `build.target`, not `buildx`).
- `include_tests` — `true` also lists the commands declared by the test assemblies, which are hidden by default. They stay executable either way.
- `tag` — scope results to a tag subtree via segment-aware prefix match (`tag=assets` matches `assets` and `assets/import`, not `assetsx`).
- `group_by` — `flat` (the default) returns a `commands` array; `package`, `tag` or `group` return a `groups` array instead (`tag` is a nested tree mirroring the tag/subtag hierarchy, with untagged commands in a node with an empty tag; `group` is a nested tree mirroring `group.subgroup`). Combining it with `detail=tags` or `detail=groups` is rejected with `400`: those indexes are already one row per tag or group.
- `sort` — `name` (the default, by full name) or `package` (originating package, with full name as tiebreak). Combining `package` with `detail=tags` or `detail=groups` is rejected with `400`: a tag or a group is not owned by a package.
- `order` — `asc` (the default) or `desc`. Applies to the chosen `sort`; sorting happens on the flat list before pagination and grouping.
- `offset` / `limit` — paginate the filtered, sorted list. Both apply to the flat list *before* grouping, so pages stay deterministic. `offset` skips that many matches (default `0`); `limit` caps how many are returned (default: no cap). A client has seen everything once `offset + count` reaches `total`.

Filters combine with AND; a filter that matches nothing returns an empty result, not an error. An invalid `detail`, `group_by`, `sort`, `order`, `offset`, or `limit` value is rejected with `400` naming the accepted values.

### `/api/commands` response shape

Every response carries the pagination counters plus a `server` block. The command payload is either a flat `commands` array or — under `group_by` — a `groups` array in its place:

| Field | Meaning |
|---|---|
| `commands` | The page of commands. Present only when `group_by=flat` (the default) and `detail` is not `tags` or `groups`. |
| `groups` | Present *instead of* `commands` when `group_by=package`, `group_by=tag` or `group_by=group`. Also present under `detail=groups`, as one `{group, count}` row per group. |
| `tags` | Present *instead of* `commands` when `detail=tags`: one `{tag, count}` row per distinct tag. |
| `obsoleteNames` | Present *instead of* `commands` when `detail=obsolete_names`: one `{oldName, command}` row per old command name. |
| `count` | How many rows this page actually returned — commands, or tags or groups under `detail=tags` or `detail=groups`. |
| `total` | How many rows matched the filters, **before** `offset`/`limit` were applied — matching commands, or distinct tags or groups under `detail=tags` or `detail=groups`. |
| `offset` | Echo of the requested `offset` (`0` when not supplied). |
| `limit` | Echo of the requested `limit` (`null` when not supplied — no cap). |
| `server` | The responding server's `version`, `port`, and `startTime`. |

`GET /api/commands?detail=compact&tag=baking/lighting&limit=2` — `count` is the 2 returned here, while `total` is all 6 commands under `baking/lighting`, so the next page is `offset=2`. Commands are sorted by full name; `command` is that full name, `<group> <name>`, the same field name the exec envelope uses:

```json
{
  "commands": [
    {
      "command": "lighting bake",
      "name": "bake",
      "group": "lighting",
      "description": "Trigger an async lightmap bake of the open scene(s) via Lightmapping.BakeAsync(). Returns immediately; poll lighting bake_status until completed.",
      "tags": ["baking/lighting"],
      "package": "Unity.Pipeline.Editor"
    },
    {
      "command": "lighting bake_status",
      "name": "bake_status",
      "group": "lighting",
      "description": "Get the status of the last lighting bake: idle | baking | completed.",
      "tags": ["baking/lighting"],
      "package": "Unity.Pipeline.Editor"
    }
  ],
  "count": 2,
  "total": 6,
  "offset": 0,
  "limit": 2,
  "server": {
    "version": "0.0.1",
    "port": 54321,
    "startTime": "2026-07-29T09:14:22.113Z"
  }
}
```

Under `group_by=tag` the same envelope carries a nested `groups` tree instead. Two things to note. A node's `commands` holds only the commands tagged *exactly* at that node — so `baking`, whose commands all live in subtags, reports an empty array — while its `count` covers the node's whole **subtree**. And because pagination happens before grouping, group counts describe the returned page rather than every match: `GET /api/commands?detail=compact&tag=baking&group_by=tag&limit=2` groups only the 2 commands on this page, while `total` still reports all 17 matches under `baking`:

```json
{
  "groups": [
    {
      "tag": "baking",
      "count": 2,
      "commands": [],
      "children": [
        {
          "tag": "baking/lighting",
          "count": 1,
          "commands": [
            {
              "command": "lighting bake",
              "name": "bake",
              "group": "lighting",
              "description": "Trigger an async lightmap bake of the open scene(s) via Lightmapping.BakeAsync(). Returns immediately; poll lighting bake_status until completed.",
              "tags": ["baking/lighting"],
              "package": "Unity.Pipeline.Editor"
            }
          ],
          "children": []
        },
        {
          "tag": "baking/navmesh",
          "count": 1,
          "commands": [
            {
              "command": "navmesh bake",
              "name": "bake",
              "group": "navmesh",
              "description": "Trigger an async legacy NavMesh bake of the open scene(s) via UnityEditor.AI.NavMeshBuilder. Returns immediately; poll navmesh bake_status until completed.",
              "tags": ["baking/navmesh"],
              "package": "Unity.Pipeline.Editor"
            }
          ],
          "children": []
        }
      ]
    }
  ],
  "count": 2,
  "total": 17,
  "offset": 0,
  "limit": 2,
  "server": {
    "version": "0.0.1",
    "port": 54321,
    "startTime": "2026-07-29T09:14:22.113Z"
  }
}
```

`group_by=group` carries the same kind of tree, mirroring `group.subgroup`. Each node is `{ "group": "animation.clip", "count": …, "commands": [ … ], "children": [ … ] }`, where `group` is the full group name. A command has exactly one group, so it appears once, in the node of its group. `commands` holds only the commands declared in exactly that group, `count` covers the node's whole subtree, and pagination happens before grouping, as with tags.

`group_by=package` uses the same envelope with flatter nodes — `{ "package": "Unity.Pipeline.Editor", "count": 2, "commands": [ ... ] }`, with no `children`.

`detail=tags` answers "what is there to browse" in one small response. Each row is a distinct tag
path and its **subtree** count, so a row's `count` is exactly what a follow-up `tag=<that tag>`
request returns. The rows are ordinal-sorted (`order=desc` reverses them) and `offset`/`limit`
page them; the other filters compose, so `detail=tags&query=prefab` answers "which tags have
prefab-ish commands". `GET /api/commands?detail=tags`:

```json
{
  "tags": [
    { "tag": "assets", "count": 12 },
    { "tag": "assets/import", "count": 3 },
    { "tag": "assets/text", "count": 2 }
  ],
  "count": 50,
  "total": 50,
  "offset": 0,
  "limit": null,
  "server": { "version": "0.7.0", "port": 7801, "startTime": "..." }
}
```

A command carrying several tags is counted under each of them, so the counts do not sum to the
catalog size.

`detail=groups` is the same kind of index for groups. It has one row per group, including a parent
that only holds subgroups (`animation` for `animation.clip`), and a row's `count` is the number of
commands in the group's **subtree**, so it is exactly what a follow-up `group=<that group>` request
returns. Under a `group=` filter only that subtree's groups are listed. Rows are ordinal-sorted
(`order=desc` reverses them), `offset`/`limit` page them, and the other filters compose.
`GET /api/commands?detail=groups`:

```json
{
  "groups": [
    { "group": "animation", "count": 14 },
    { "group": "animation.animator", "count": 6 },
    { "group": "animation.clip", "count": 4 },
    { "group": "animation.timeline", "count": 4 },
    { "group": "asset", "count": 12 }
  ],
  "count": 37,
  "total": 37,
  "offset": 0,
  "limit": null,
  "server": { "version": "0.7.0", "port": 7801, "startTime": "..." }
}
```

`detail=obsolete_names` maps each flat name a command had before commands were grouped to its
full name now. Only the Editor serves it, and it advertises the `commands.obsoleteNames`
capability; a Player or an older package answers `400 Unknown detail value`. Rows are sorted by
old name, `query` matches either name, and `order`, `offset` and `limit` apply. `tag`, `group`,
`group_by` and `sort=package` are rejected with `400`: an old name is not a command.
`GET /api/commands?detail=obsolete_names&query=reload_file`:

```json
{
  "obsoleteNames": [
    { "oldName": "reload_file", "command": "script.methodreload reload" },
    { "oldName": "reload_file_editor_interpreter", "command": "script.methodreload reload_editor" },
    { "oldName": "reload_file_player_interpreter", "command": "script.methodreload reload_player" }
  ],
  "count": 3,
  "total": 3,
  "offset": 0,
  "limit": null,
  "server": { "version": "0.7.0", "port": 7801, "startTime": "..." }
}
```

The Editor also answers an old name sent to `/api/exec`, or used in a `job batch` operation, with
Command Not Found naming its replacement:
`No command named 'reload_file' is available. It was renamed: run 'unity command script.methodreload reload'.`

## Executing a command (`POST /api/exec`)

A request carries the command in **exactly one** of three forms:

```jsonc
{"command": "script.methodreload reload", "parameters": {"filename": "Assets/A.cs"}}  // structured: takes the full name
{"commandLine": "script.methodreload reload Assets/A.cs"}                         // server tokenizes, then binds
{"argv": ["script", "eval", "return 2+2;"]}                                     // server binds directly
```

The structured `command` field takes the command's full name, `<group> <name>` (`script.methodreload reload`). For the two raw forms the leading tokens name
the command the way `unity command <group> <name> [args]` does, and the server binds the rest
against the command's declared parameters. This is what lets a client execute a command in a
single request without knowing its schema — the structured form needs `GET /api/commands` first,
the raw forms need nothing.

Every key of a structured `parameters` object must name one of the command's parameters, spelled
exactly as its schema does. A key that names none of them, whether misspelled, differently cased
or a parameter's former name, is refused with `INVALID_COMMAND_ARGS` and an `unknownName`
problem instead of being dropped: dropping it would run the command on that parameter's default.
The same holds inside a structured argument such as `settings` or `on-met`: a key the object does
not declare, at any depth, refuses the request with an error that names the key and, when one is
close, the key it meant (`unknown key 'fixedDeltaTime'. Did you mean 'fixed-delta-time'?`).

One key is exempt: `token`, which the `unity` CLI adds to some commands' parameters, among them its MCP
tools for `script eval`, `test run` and `script recompile`. No command declares it, so it is dropped without
effect; authenticate with the `Authorization` header.

**Deprecated, removed in 1.0.0:** three names from before parameters were kebab-case are still
accepted, as structured keys or as `--name` flags, because released `unity` CLIs send them.
`build run` takes `outputPath` as `output-path`, `console get` takes `since_session` as
`since-session`, and `pipeline list_commands` takes `group_by` as `group-by`. This holds for those
three commands only, and not when the request also sends the new name. Send the new names.

**Prefer `argv` whenever you already have split tokens.** On POSIX a CLI never holds the original
command *string* — the shell already split and unquoted argv — so re-quoting those tokens for the
server to re-split is a lossy round-trip across two independently-versioned quoting dialects. Use
`commandLine` only when you genuinely hold raw text (a chat message, an MCP argument, a pasted
line).

Supplying more than one form is an error, and so is sending `parameters` alongside `commandLine`
or `argv`. The latter is **rejected rather than ignored**: silently dropping a payload makes "you
sent it and we discarded it" indistinguishable from "you never sent it".

`argv` and `commandLine` are exec-only. `job batch` operations keep `command` (a full name, `<group> <name>`) + `params`.

### How a raw request names its command

The server reads the leading tokens of `argv` or `commandLine` in this order:

1. **The first token is a group and the second a command in it.** That command runs and the rest of the tokens are bound as its arguments.
2. **Otherwise the first token is the name of a command in the `global` group**, which holds the commands declared with `[CliCommand(name, description)]`. `unity command bake_lighting --scene Main` runs `global bake_lighting`. A bare name is accepted in a structured `command` and in batch too.
3. **Otherwise the first token is the command's one-word form**, the group and the name joined by a dot (`<group>.<name>`), and the rest are bound as its arguments. It is accepted in a structured `command` too, and is never shown in a reply, so write `<group> <name>`.
4. **Otherwise the first token is the name of exactly one command**, in any group, and is not itself a group. The rest are bound as its arguments: `unity command eval "return 2+2;"` runs `script eval`. It is accepted in a structured `command` too. A name that several commands share is Command Not Found, and the message lists them. Adding a command can make a name ambiguous, so scripts should name commands as `<group> <name>`.
5. **A group with nothing after it** is listed (see below). A group followed by anything else is Command Not Found, with suggestions drawn from that group.
6. **Anything else is Command Not Found.**

A global command named like a group runs instead of the group's listing: with a `global build` command, `unity command build` runs it, while `unity command build run` still runs `build run`.

Groups are checked first: a first token that is a group names a command with the second token. A group is **one token**, dots included: `animation animator add_state` is not found, while `animation.animator add_state` runs. The response's `command` field carries the resolved command as `<group> <name>` (`build run`, `animation.animator add_state`), whichever form the request used. A reply to an `argv`/`commandLine` request always carries it, in every reply kind (success, argument error, validation or execution failure, busy, listing, Command Not Found) and without `verbose`: that is how a caller learns which command its tokens named. For a Command Not Found reply it is what was typed, in that form, and for a group listing it is the group. A structured request keeps the lean envelope: `command` only with `verbose`, then also as `<group> <name>`.

A group with nothing after it returns a success envelope listing it. The reply carries `listedGroup` and an empty `parameters`, and `result` holds the group's direct subgroups (full names) and direct commands, sorted; a command entry is `{ "command": "<group> <name>", "name", "description" }`. `{"argv": ["animation"]}`:

```json
{
  "success": true,
  "result": {
    "group": "animation",
    "subgroups": ["animation.animator", "animation.clip", "animation.timeline"],
    "commands": []
  },
  "listedGroup": "animation",
  "parameters": {}
}
```

### Timeouts

Every request has one timeout, in seconds. It is resolved in this order:

1. The request's top-level `timeout`, **in milliseconds** (the `unity` CLI sends its
   `--timeout <seconds>` here).
2. The command's own `timeout` parameter, if the request sets it.
3. The command's default: the default of its `timeout` parameter (for example 600 s for
   `test run`, 30 s for `script eval`), or 60 s for a command that declares none.

```jsonc
{"command": "test run", "parameters": {"mode": "editor"}, "timeout": 900000}  // 900 s
{"argv": ["test", "run", "--timeout", "900"]}                                // same, as a parameter
```

Time spent queued behind another command counts. When the timeout runs out the server answers
`504` with `errorCode: "TIMEOUT"`; the command may still be running, since it cannot be stopped
from outside. A request that runs out while queued is answered the same way and never runs.
Commands that report their own timeout (`job wait`, `job batch`, `test run`, `script run`) stop
slightly early, by `min(2 s, 10%)`, so their structured result arrives first. Detached jobs
(`"job": true`) have no timeout.

### Tokenizer dialect (`commandLine` only)

This is a **versioned contract**, not an implementation detail. It is the pipeline's own dialect —
identical on every OS, and deliberately *not* the caller's shell — so the same string tokenizes
the same way from cmd.exe, bash, an MCP client, or a chat box.

| Rule | Example | Tokens |
|---|---|---|
| Whitespace separates tokens; runs collapse | `a   b` | `a`, `b` |
| `"…"` groups, `\` escapes the next character | `"a b"`, `"a\"b"` | `a b`, `a"b` |
| `'…'` groups, with **no** escapes at all | `'a\b'` | `a\b` |
| `\` escapes outside quotes | `a\ b` | `a b` |
| Adjacent spans concatenate | `a"b c"d` | `ab cd` |
| `""` yields one **empty** token | `--message ""` | `--message`, *(empty)* |
| Unbalanced quote or dangling `\` | `"oops` | **error** |

There is **no expansion of any kind** — no `$VAR`, globbing, `~`, comments, or operators. `&&`,
`|` and `>` are ordinary characters.

Two consequences worth stating explicitly:

- **Single quotes are load-bearing.** They pass double quotes through untouched, which is the only
  practical way to send `script eval 'obj.name = "x";'` or `job batch --operations '[{…}]'`.
- **Windows `CommandLineToArgvW` rules do not apply.** `"a""b"` is `ab` here, not `a"b`. The usual
  argument for Windows rules — that `\` is the path separator — does not hold, because every path
  this API accepts is a forward-slashed Unity asset path.

### Argument grammar (both raw forms)

- `--key value`, `--key=value`, or a bare `--key` (which means `true`, and is an error on a
  non-boolean parameter).
- A follower is consumed as the value **unless it starts with `--`**, so `--path -foo` works.
- `--` ends flag parsing; everything after it is a positional value, exempt from the `-foo` and
  `key=value` checks.
- Positionals fill **required parameters in declaration order, then optional parameters in
  declaration order**. A slot already filled by an explicit flag is skipped, not overwritten.
- Not supported, by design: short flags, `-abc` bundling, `--no-x`, repeated-flag-as-array,
  flag-name abbreviation, and `@argfile`.

> **Declaration order is wire API for raw clients.** Reordering a command's parameters — or
> flipping one between required and optional — silently changes what a positional binds to. Append
> new optional parameters at the end. See [Creating commands](creating-commands.md).

### Successful replies echo the bound parameters

```json
{"success": true, "result": "pong", "parameters": {"message": "hi"}}
```

`parameters` is present **only** for `argv`/`commandLine` requests, so structured replies are
unchanged. It reports exactly what the binder stored, so a client that no longer binds locally can
still show the user what the server understood.

### Argument errors

```json
{
  "success": false,
  "error": "Invalid Command Arguments",
  "errorCode": "INVALID_COMMAND_ARGS",
  "errorDetails": "console write has no parameter --mesage. Did you mean --message?",
  "argProblems": [{"kind": "unknownName", "name": "mesage", "suggestion": "message"}],
  "commandSchema": {"command": "console write", "name": "write", "group": "console", "parameters": [{"name": "message", "type": "String", "required": true}]}
}
```

**`errorCode` is the discriminator, not the HTTP status.** Every `/api/exec` failure is already
`400`, so status alone cannot separate "your arguments are wrong" from "the command threw".

| `errorCode` | Meaning |
|---|---|
| `INVALID_COMMAND_ARGS` | The command resolved, but its arguments could not be bound. Nothing executed. |
| *(absent)* | Any other failure — an unknown command, a validation error, or the command itself throwing. |

A malformed *request shape* — two command forms at once, `parameters` beside `argv`, an empty
`argv`, a `commandLine` that does not tokenize (unbalanced quote, dangling backslash), or a body
using none of the three forms — is a `400` with `error: "Invalid Request"` and **no** `errorCode`,
because no command was ever resolved. Such a reply carries neither `argProblems` nor
`commandSchema`: there is no command to describe.

`argProblems` is machine-readable and **accumulated**: every defect is reported, not just the
first, so a user fixing a command line sees all of its problems at once. It is deliberately not
pre-rendered English — the `unity` CLI renders *and localizes* these itself. Use `errorDetails` as
the English fallback, and for any `kind` your client does not recognize.

| `kind` | Meaning |
|---|---|
| `emptyName` | A flag with no name: `--=` or `--=value`. A bare `--` is the end-of-flags separator, never this. |
| `emptyValue` | `--key=` with nothing after it, or a bare `--key` on a non-boolean parameter. |
| `singleDash` | A `-x` token in positional position. |
| `duplicate` | The same `--key` supplied more than once. |
| `unknownName` | No such parameter. Carries `suggestion` when a close match exists. |
| `bareAssignment` | A `key=value` positional whose left side names *the parameter that positional fills* — a misremembered `--key value`. A left side naming some other parameter is a value: `script eval "timeout=5;"` binds as `code`. |
| `excessPositional` | More positionals than there are slots left for them. `capacity` is the slots still free after the flags took theirs and `given` is the positionals supplied; `token` is the first one with no slot. |
| `positionalConflict` | A positional whose own slot a flag already filled. Carries `name` (that parameter) and `token`. |
| `typeMismatch` | The declared type cannot accept the token; carries `expectedType`. |

`commandSchema` is the command's catalog entry, shaped like a `/api/commands` `commands[]`
element (without the generated `schema`). It lets a client print usage without a separate schema
fetch.

### Caller context

Optional fields that describe the calling client. They never affect execution, and a server that
predates them ignores them.

```json
{"argv": ["editor", "status"], "commandId": 4503599627370495, "clientSource": "agent", "clientName": "claude", "caller": "plugin", "skill": "unity-editor"}
```

| Field | Meaning |
|---|---|
| `commandId` | The client invocation's id, reported on `Pipeline_CommandExecuted` so it can be joined with the client's own event. Keep it at or below 2^53 − 1 so JSON consumers don't round it. |
| `clientSource` | Who drove the client: `human`, `agent`, `ci` or `unknown`. |
| `clientName` | Which AI client family drove it (`claude`, `cursor`, …), or `none`. |
| `caller` | What invoked the client, such as `plugin` or `other`. |
| `skill` | The agent skill driving the client, such as `unity-editor`. |

A value of the wrong type, a non-integer or out-of-range `commandId`, and a value longer than 64
characters are all ignored; the command still runs.

### Capability negotiation

Servers advertise optional wire features in `capabilities`, both on `GET /api/status` and in the
**port descriptor**:

```json
"capabilities": ["exec.argv", "exec.commandLine", "exec.commandTimeouts", "commands.obsoleteNames", "transport.pipelineProtocol", "pipeline.terminal"]
```

| Capability | Meaning |
|---|---|
| `exec.argv` | `/api/exec` accepts the `argv` form. |
| `exec.commandLine` | `/api/exec` accepts the `commandLine` form. |
| `exec.commandTimeouts` | A request may omit `timeout` and get the command's own default, and the server always answers (`504 TIMEOUT`) when a timeout runs out (see [Timeouts](#timeouts)). |
| `commands.obsoleteNames` | `/api/commands` accepts `detail=obsolete_names`. Editor only. |
| `transport.pipelineProtocol` | The `pipeline` entry in `endpoints` speaks the Pipeline protocol. |
| `pipeline.terminal` | The Pipeline protocol transport formats and streams a command's result (`--format`, `--fields`) for a client that sends `stdout.tty` (see [Output formats](#output-formats)). Editor only. |

The descriptor is the carrier that matters: clients already read it locally to obtain `evalToken`,
so negotiation costs **zero extra requests**.

The rule for clients:

- Token present → send that form.
- Descriptor present but **no `capabilities` key at all** → the server predates this feature. Do
  not send raw forms; tell the user to update the package.
- Never sniff first. An older server receiving `{"argv":[…]}` fails deserialization (its `command`
  field is required) and answers `400 {"error":"Invalid JSON"}` — which a client would otherwise
  surface as a generic internal failure, making version skew look like a client bug.

Because the descriptor file is writable by anything running as the user, treat a capability claim
as a hint rather than a guarantee: a `400` carrying `error: "Invalid JSON"` or `"Invalid Request"`
with **no** `errorCode`, in response to a request that used `argv`, means the same thing as an
absent key. Degrade, don't crash.

## Output formats

Over the Pipeline protocol, a client that sends `stdout.tty` gets the command's result formatted and
streamed: each record is written as soon as the command produces it. The `unity` CLI does this for an
Editor that advertises `pipeline.terminal`. Every other request gets the result as JSON.

Pick the format with `--format`, anywhere before `--`. The `unity` CLI passes its own `--format` through
as it is, and its `github` and `ndjson` are taken as `human` and `json`:

| Option | Output |
|---|---|
| none or `--format human` | For people and agents. Records as a table, a header row, then one row per record, with column widths taken from the first record; a cell is kept on one line. A single result is one `name: value` line per field, a value's further lines indented under its first. A text result is written as it is. On a terminal, lines are cut to its width. |
| `--format tsv` | For scripts. One line per record, fields separated by tabs, no header. Tabs, newlines and backslashes in a value are escaped (`\t`, `\n`, `\\`), and a missing value is an empty field. No nested values: fields that hold an object or a list are left out, and naming one with `--fields` fails; pass `--format json` for them. |
| `--format json` or `--json` | A single result as one line of JSON; records, or a list, as one JSON value per line (NDJSON). |
| `--fields <a,b,…>` | Only these fields, in this order, in any format. |
| `--omit-nulls` | Leaves out null fields in JSON and in a single result's `name: value` lines. A table keeps the column and leaves the value blank. |

```bash
unity command editor status --format tsv --fields playMode
unity command editor status --format json
```

An empty list writes nothing and exits `0`. Formats may gain fields in any release, so scripts should
name the fields they read with `--fields` rather than rely on column positions.

## Multiple instances (Multiplayer Play Mode)

Multiplayer Play Mode (MPPM) runs additional **virtual player** Editors alongside the main Editor.
Each clone is a **separate Unity project** under `<mainProject>/Library/VP/<vpId>` with **its own
`Library`**, launched with `--virtual-project-clone`. The Pipeline server treats that flag as
"run a server", so every clone runs its own server on its own port and writes its own canonical
descriptor at `<mainProject>/Library/VP/<vpId>/Library/Pipeline/.unity-pipeline-port`.

Because each instance lives at a distinct project path, there is no shared descriptor to clobber:

- `unity command` / `unity status` (keyed off the main project path) resolve to the **main Editor**,
  unchanged.
- A clone is reachable directly with `unity command --project-path <mainProject>/Library/VP/<vpId>`.
- Descriptors carry `role` (`"main"`/`"clone"`), `instanceName` (e.g. `"Player 2"`), and `vpId`.

MPPM launches a clone with `--virtual-project-clone`, a stable `-vpId=<id>` naming its directory
under `Library/VP`, and a `-name` debug name — which is where `role`, `vpId` and `instanceName` in
the descriptor come from.

### Enumerating and driving clones

```bash
# Every running instance, main + clones, with the path to address each by
unity command editor.playmode list_instances
unity pipeline list

# Talk to one directly — the full command surface, not a subset
unity command --project-path <mainProject>/Library/VP/<vpId> editor_status
```

`unity command` with no `--project-path` still resolves to the **main** Editor. The CLI collapses a
project's own clones into it when picking a target automatically, so running a scenario never turns
a working `unity command` into an ambiguity error. A clone whose main Editor isn't reachable is
still offered, since it's then the only way into that project.

### Driving a Play Mode Scenario

Play Mode Scenarios are assets (`OrchestratedScenario`, e.g.
`Assets/Settings/PlayMode/2Players.asset`) describing the main Editor plus virtual players.
These commands require **Unity 6000.3 or newer** and the **`com.unity.multiplayer.playmode`**
package; without both they are not registered at all (see
[Availability](commands/multiplayer-play-mode.md#availability)). Do **not** use `editor_play` for a
scenario — the manager enters Play Mode itself and guards against manual entry.

```bash
unity command editor.playmode list_scenarios   # discover scenario assets
unity command editor.playmode set_active_scenario --path Assets/Settings/PlayMode/2Players.asset
unity command editor.playmode start_scenario  # run the active scenario
unity command editor.playmode get_scenario_status # scenario state + each player's run state/address
unity command editor.playmode stop_scenario   # stop it
```

Authoring (Edit Mode): `editor.playmode create_scenario --name X` (auto-adds the Main Editor player),
`editor.playmode add_player --path <asset> --type clone|local --name <n>`,
`editor.playmode remove_player --id <id> --path <asset> --confirm true` (ids come from `editor.playmode list_players`: a GUID on 6000.5+, the player's name on 6000.3/6000.4). You may only set the active scenario while
state is `Idle`; player editors launch asynchronously, so poll `editor.playmode get_scenario_status` until a
player reports `reachable: true`, then drive it with the `projectPath` it hands back. Each entry
also carries MPPM's `state`, which tells a slow launch from one that died.

## See also

- [Runtime connection & setup](runtime-setup.md) — enabling the server in a Player build.
- [Creating commands](creating-commands.md) — authoring the commands clients call.

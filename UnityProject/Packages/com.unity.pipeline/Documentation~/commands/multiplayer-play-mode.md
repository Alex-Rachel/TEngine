# Multiplayer Play Mode & instance commands

Commands to drive Multiplayer Play Mode (MPPM) **Play Mode Scenarios** — the main Editor plus one
or more virtual player (clone) Editors — and to discover those instances so you can drive each one.

These are distinct from `editor_play` / `editor_stop` / `editor_pause`
(see [Editor lifecycle & observability](editor-lifecycle-and-observability.md)), which control the
**main Editor only** and never launch virtual players.

See [Connectivity → Multiple instances](../connectivity.md#multiple-instances-multiplayer-play-mode)
for the clone topology (each clone is a separate Unity project under `Library/VP/<vpId>` with its
own `Library`, port, and Pipeline server).

## Availability

**The `editor.playmode` scenario commands require Unity 6000.3 or newer *and* the
`com.unity.multiplayer.playmode` package.** Both conditions have to hold:

- On 6000.5 and newer, scenario control runs on the public `Unity.PlayMode.Editor.PlayModeScenarioManager`
  API in `UnityEditor.PlayModeModule`, a built-in editor module that carries no public types before 6000.5.
- On 6000.3 and 6000.4 (MPPM 2.0.x), the same commands, arguments and return shapes are implemented by
  reflection over MPPM's internal scenario types. Two differences: a player's `id` is its name (6000.3
  players have no GUID), and `editor.playmode create_scenario` always writes to `Assets/Settings/PlayMode/`, using
  `path` only for the file name.
- Virtual players come from the MPPM package. Without it there is nothing to orchestrate, so the
  commands are compiled out rather than registered and answering `available: false`. The package
  presence is detected by an `asmdef` version define (`MPPM_AVAILABLE`), so removing MPPM removes the
  commands on the next recompile.

Where either condition fails, these commands are **absent from `/api/commands`** and calling one
fails as an unknown command. Probe `/api/commands` rather than branching on an `available` field.
The package as a whole still supports Unity 2022.3 — only this command group is gated.

`editor.playmode list_instances` is available on **every** supported version and needs no MPPM: it works off the
port descriptor files, so it lists whatever clones happen to be running.

Scenario **authoring** (`editor.playmode create_scenario`, `editor.playmode add_player`,
`editor.playmode remove_player`) still reflects within that gate: the `OrchestratedScenario` asset type,
its `m_Settings` struct, and the instance controller types have no public surface. Those three
report a `"not found. Is Multiplayer Play Mode installed?"` error if the types are missing at
runtime even though the package is installed — an MPPM version whose internals moved.

## Two things to know before you start

**Scenario assets must end in `.asset`.** Unity imports a `PlayModeScenario` only from a `.asset`
file. Give `editor.playmode create_scenario` any other extension and Unity writes the YAML to disk but
imports it as a `DefaultAsset` — the file exists, and every other scenario command reports it
missing. `editor.playmode create_scenario` rejects a non-`.asset` path up front, and verifies the asset
imported before reporting success.

**There is always an "active scenario", even when there isn't one.**
`PlayModeScenarioManager.ActiveScenario` never returns null: with nothing assigned it hands back a
non-persistent `DefaultScenario` that runs the main Editor alone, no clones. The commands report
that case as `active: null` with `isDefaultScenario: true`, and `editor.playmode start_scenario` refuses
to run rather than silently entering ordinary single-Editor Play Mode.

## Scenario control

### `editor.playmode list_scenarios`
List all Play Mode Scenario assets in the project.

No parameters.

**Returns:** `{ count, scenarios: [{ name, path, type }] }`

### `editor.playmode set_active_scenario`
Set the active Play Mode Scenario by asset path.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `path` | yes | `–` | Asset path of the scenario, e.g. `Assets/Settings/PlayMode/2Players.asset` |

**Returns:** `{ active: { name, path }, state }`
**Notes:** Only valid while `state` is `Idle` — stop a running scenario first with
`editor.playmode stop_scenario`. There is no `get_active` counterpart: read the current scenario from
[`editor.playmode get_scenario_status`](#editorplaymode-get_scenario_status)'s `active`.

### `editor.playmode start_scenario`
Start the active Play Mode Scenario (enters Play Mode with its players).

No parameters.

**Returns:** `{ started, active: { name, path }, state, message }`
**Notes:** The scenario manager enters Play Mode itself — do **not** call `editor_play`. Player
editors launch asynchronously; poll `editor.playmode get_scenario_status` or `editor.playmode list_instances`. Fails
if no scenario asset is active — the implicit `DefaultScenario` does not count.

### `editor.playmode stop_scenario`
Stop the active / running Play Mode Scenario.

No parameters.

**Returns:** `{ stopped, state, note }`
**Notes:** State settles asynchronously (`Running` → `Stopping` → `Idle`); poll
`editor.playmode get_scenario_status` for the final state.

### `editor.playmode get_scenario_status`
Live state of the active / running Play Mode Scenario and of the Editor instances it runs.

No parameters.

**Returns:** `{ state, playMode, active: { name, path } | null, isDefaultScenario, players: [{ id, name, type, state, reachable, vpId, port, endpoints, projectPath }], playerError, note }`
**Notes:** `MainThreadRequired = true`. `state` is the scenario manager's state (`Idle` / `Starting`
/ `Running` / `Stopping`); `playMode` is `playing` / `paused` / `stopped`. `active` is `null` and
`isDefaultScenario` is `true` when no scenario asset is assigned.

`players` is **one entry per player the active scenario declares** — never MPPM's fixed four slots,
so a two-player scenario reports two players and no phantom `NotLaunched` rows. Each entry joins
three sources:

| Field | Source | Meaning |
|-------|--------|---------|
| `id`, `name`, `type` | the scenario asset | identity; `type` is `main` / `clone` / `local`. `id` is what `editor.playmode remove_player` takes. |
| `state` | MPPM | `NotLaunched` / `Launching` / `Launched` / `UnexpectedlyStopped`. `null` if MPPM has no slot under that name. |
| `reachable`, `vpId`, `port`, `projectPath` | the player's port descriptor | present once that Editor's Pipeline server is up; `reachable` is `false` and the rest `null` until then. |

`state` and `reachable` answer different questions and you generally want both. A player mid-launch
reads `Launching` with `reachable: false`; one that died reads `UnexpectedlyStopped`, which a
descriptor alone cannot distinguish from never having started. Once `reachable` is `true`, drive
that player directly with `unity command --project-path <its projectPath>`.

Each player is matched to a descriptor in this order: by `vpId` when MPPM reports one (a `vpId` with
no descriptor yet reports nothing rather than matching another player); the main player by `role`;
otherwise a clone by its name, against clone descriptors only. A `local` (built) player has no Editor
descriptor, so it never reports `reachable`. `playerError`
carries a scenario-settings or MPPM reflection failure; the whole command is compiled out when the
package is absent, so an error here never means "not installed".

For the declared roster on its own — including a scenario that is not the active one — call
[`editor.playmode list_players`](#editorplaymode-list_players).

### `editor.playmode list_players`
List the players a Play Mode Scenario asset declares.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `path` | no | active scenario | Scenario asset path. |

**Returns:** `{ path, count, players: [{ id, name, type, controller, runMode }], playerError, note }`
**Notes:** One entry per declared player, with the `id` you pass to `editor.playmode remove_player` and a
`type` of `main` / `clone` / `local`. `players` is empty and `note` explains why when no scenario
asset is active. This is the **declared** roster and cannot change while a scenario runs — it is not
something to poll. Unlike [`editor.playmode get_scenario_status`](#editorplaymode-get_scenario_status) it takes a `path`, so it can
read a scenario that is not the active one; it reports no run state or reachability.

## Scenario authoring (Edit Mode)

### `editor.playmode create_scenario`
Create a new orchestrated Play Mode Scenario asset.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `name` | yes | `–` | Scenario name (also the asset file name). |
| `path` | no | `<authoring root>/Settings/PlayMode/<name>.asset` | Asset path for the new scenario. Must be inside the authoring root (`Assets/` unless changed in Project Settings) and end in `.asset`. On 6000.3/6000.4 the folder is always `Assets/Settings/PlayMode/`. |

**Returns:** `{ created, name, path, active, note }` — `active` is `false` and `note` gives the exact
`editor.playmode set_active_scenario --path <path>` command to run next.
**Notes:** Creating a scenario does not make it active. `editor.playmode add_player` and
`editor.playmode remove_player` without `path`, and `editor.playmode start_scenario`, act on the active
scenario, so run `editor.playmode set_active_scenario` first. The Main Editor player is added
automatically by the scenario itself; only add clones and local players. A `path` outside `Assets/` or with an extension other than `.asset` is rejected
before anything is written, and the command re-loads the asset after creating it so a file Unity
declined to import is reported as a failure instead of a success.

### `editor.playmode add_player`
Add a player to a scenario asset.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `path` | no | active scenario | Scenario asset path. |
| `type` | yes | `–` | Player type: `clone` (virtual player) or `local` (built player). |
| `name` | no | `–` | Name for a local player. Ignored for a clone on 6000.5+ (see Notes). |

**Returns:** `{ added, id, type, name, path, note }` — `path` is the scenario that was actually modified,
`name` is the player's name as Multiplayer Play Mode shows it, and `note` says when `--name` was not used.
**Notes:** The Main Editor is added automatically and cannot be added as a player. On 6000.5+, a
clone takes the next Multiplayer Play Mode player slot and the name that goes with it (`Player 2`,
`Player 3`, `Player 4`), so it launches on the next `editor.playmode start_scenario` without a
reimport. Multiplayer Play Mode has four player slots and slot 0 (`Player 1`) is always the Main
Editor, so a scenario holds at most 3 clones; adding a fourth fails with an error. For more players,
add built players with `--type local`. Adding or removing
a player re-numbers the remaining clones the way Multiplayer Play Mode does when it loads the asset.

### `editor.playmode remove_player`
Remove a scenario player by its id.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `id` | yes | `–` | Player id to remove: a GUID on 6000.5+, the player's name on 6000.3/6000.4. Read it from `editor.playmode list_players`. |
| `path` | no | active scenario | Scenario asset path. |
| `confirm` | no | `false` | Must be `true` to remove the player; without it the command refuses. |
| `dry-run` | no | `false` | Report the player that would be removed without changing the scenario. |

**Returns:** `{ removed, id, path }`, or `{ status: "dry_run", wouldRemove: { id, name, type }, path }` on a dry run.
**Notes:** Destructive: it rewrites the scenario asset and is not undoable via Unity's Undo, so it follows the
`confirm`/`dry-run` convention in [Safety and mutations](../safety-and-mutations.md).

## Reachable instances

### `editor.playmode list_instances`
List running Editor instances reachable over Pipeline (this Editor + its MPPM virtual player clones).

No parameters.

**Returns:** `{ count, instances: [{ name, role, port, endpoints, pid, vpId, projectPath }] }`
**Notes:** `MainThreadRequired = false`. Only instances whose PID is still alive are listed; the
main Editor sorts first. `endpoints` is the instance's advertised connection list, as in its descriptor; `port` is `0` when
its HTTP service is turned off and it is reachable only over its `pipeline` endpoint. `projectPath` is what you pass to `unity command --project-path` to talk to
that instance — see [Driving a clone](#driving-a-clone) below.

## Driving a clone

Talk to a clone directly. Each one runs its own Pipeline server and publishes its own descriptor, so
pointing the CLI at the clone's project directory gives you the whole command surface against it:

```bash
unity command --project-path <mainProject>/Library/VP/<vpId> editor_status
unity command --project-path <mainProject>/Library/VP/<vpId> run_tests --mode playmode
```

Get the path from `editor.playmode list_instances` (its `projectPath`) or from `unity pipeline list`, which
reports clones by player name against the project they are playing.

`unity command` with no `--project-path` still resolves to the **main** Editor: the CLI collapses a
project's own clones into it, so starting a scenario never turns a working `unity command` into an
ambiguity error.

There is no command that relays through the main Editor, deliberately. A direct connection doesn't
queue behind the main Editor's command gate (or stall when a modal dialog blocks it), it honours
`--timeout`, it reports the clone's own status rather than nesting one response envelope inside
another, and it reaches endpoints a relay over `/api/exec` never could — job polling, `/api/status`,
`/api/commands`.

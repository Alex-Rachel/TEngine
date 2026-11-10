# Batch command

The `job batch` command runs several existing pipeline commands in a single request, with cross-operation result references, atomic Undo, and dry-run validation. It exists to collapse the round-trips of a "create → add components → wire fields" job (otherwise 10–90 sequential calls) into one request, and to add what `eval` structurally lacks: **transactionality and rollback**, so a failure mid-way never leaves partial state on a user-owned scene.

**Scope guard:** `job batch` is transactional object CRUD with references. It intentionally has **no** `invoke`/loop/conditional op types — arbitrary logic belongs in code-on-disk (`script run`/`script eval`), not in JSON. This keeps the command surface auditable and allowlistable.

### `job batch`
Run multiple registered commands in one transactional request.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `operations` | yes | – | Ordered list of operations, each `{ id?, command, params }`. Max 200. |
| `transactional` | no | `true` | Group every op into one Editor Undo step and revert all applied ops if any op fails. Forced `false` when `on-error=continue`. Rejects commands whose mutations Undo cannot revert (see [Transactional restrictions](#transactional-restrictions)). |
| `on-error` | no | `abort` | `abort`: stop at the first failing op. `continue`: run every op, collecting per-op errors (forces `transactional=false`). |
| `dry-run` | no | `false` | Validate command names, parameters, reference topology, and excluded/non-batchable commands without mutating anything. |
| `result-fields` | no | – | Optional per-op result projection: a map of op id-or-index → array of result field paths to keep (context economy). |
| `timeout` | no | `60` | Timeout for the whole batch, in seconds. Checked before each op after the first; when it is about to run out, the remaining ops are skipped and a transactional batch rolls back. |

**Returns:** `BatchResult` — `{ results: [{ id?, command, success, result? | error?, skipped?, resultTruncated?, revertible? }], applied, reverted, transactional, onError, error?, undoGroup?, dryRun?, valid? }`. The batch-level `error` is set only for aborts not attributable to a single op ("batch time budget exceeded", "batch canceled").

**Notes:** Runs on the main thread. Each sub-operation goes through the same dispatch path as `/api/exec`, so its result is shaped identically to running that command standalone.

#### Operation shape

Each entry of `operations` is:

```json
{ "id": "createHead", "command": "component add", "params": { "target": "$root.instanceId", "type": "Rigidbody" } }
```

- `id` (optional) — a name for referencing this op's result from a later op. Must be unique within the batch and match `^[A-Za-z_][A-Za-z0-9_-]*$` (purely numeric ids would be ambiguous with index selectors, and `.` collides with the reference path separator).
- `command` — the registered command to run, written as `<group> <name>`, such as `gameobject create` (any command except the non-batchable ones below). It is resolved the way a raw request names a command: a group followed by a name in it (see [Connectivity](../connectivity.md#how-a-raw-request-names-its-command)). A group alone is refused as not a command. Results and errors name the command as `<group> <name>` however it was written.
- `params` — the command's parameters, same shape as `/api/exec` `parameters`. Operations are **always** `command` + `params`; the `argv`/`commandLine` raw forms are exec-only and are not accepted inside a batch operation.

#### Cross-operation references

Any **string** value in a later op's `params` may reference an earlier op's result:

```
"$<id-or-index>.<jsonPath>"
```

- `<id-or-index>` names an earlier op by its `id` or its 0-based index (an `id` match takes precedence).
- `<jsonPath>` is a Newtonsoft SelectToken path into that op's result, e.g. `instanceId`, `components[0].instanceId`.
- `"$$"` escapes a literal `$` (so `"$$5"` is the literal string `"$5"`).
- References are **backward-only** and resolved as **whole values** (the entire string must be the reference). The substituted JSON token keeps its type, so a numeric `instanceId` deserializes into an `ObjectRef` parameter exactly as a literal would. References may appear anywhere in the parameter tree (including inside nested objects/arrays).
- Referencing an op that **failed** or was **skipped** errors explicitly (this can happen under `on-error=continue`); referencing a succeeded op that **returned no result** errors with "returned no result". A reference never silently resolves to `null`.
- References always resolve against the **full** result of the earlier op, even when the reported copy was truncated or projected via `result-fields`.
- **Result size bounds** (both measured in serialized UTF-8 bytes, i.e. wire size): a single op's reported result above **16 KiB** is replaced by a truncation marker (`resultTruncated: true` + a preview), and the **aggregate** of all reported results in one batch is bounded to **256 KiB** — past that, later results are elided the same way (the ops still execute; only the echoed copy is dropped). Use `result-fields` to project results down when a batch reads large objects.
- A malformed `result-fields` path never fails its op (the op has already executed); the bad path is reported inside the projection as `invalidResultFields`.

Example — create a GameObject, add a Rigidbody to it, then set the Rigidbody's mass:

```json
{
  "operations": [
    { "command": "gameobject create", "params": { "name": "Ball" } },
    { "command": "component add", "params": { "target": "$0.instanceId", "type": "Rigidbody" } },
    { "command": "component set_field", "params": { "target": "$1.instanceId", "field": "m_Mass", "value": 2.5 } }
  ]
}
```

#### Atomicity

The whole batch runs inside one `AuthoringUndoScope`, so every op (each of which registers its own Undo operations) collapses into a **single** Editor Undo step. When `transactional` (the default) and the batch aborts — an op fails under `on-error=abort`, the time budget runs out, or the job is canceled — every applied op is reverted with `Undo.RevertAllDownToGroup` (including partial mutations registered by the failing op itself), the scene returns to its pre-batch state, and nothing is left on the Undo stack. `reverted: true` and the failing op's `error` are reported.

#### Transactional restrictions

Rollback is only real for **Undo-tracked** mutations (scene objects, components, serialized fields). Commands that mutate state **outside** the Undo system — asset and file writes, scene saves, Build Settings, project settings, baked data — cannot be rolled back, so a transactional batch **rejects** them with a structured `not_batchable_transactional` error. Run them with `transactional: false`, or call them standalone. Their per-op result reports `revertible: false` so the limitation is visible.

The non-revertible set (v1 — a curated list, pending per-command `Revertible` metadata):

- **Assets & files:** `asset create`, `asset import`, `asset move`, `asset copy`, `asset rename`, `asset delete`, `asset create_folder`, `asset set_import_settings`, `asset write_text`, `script create`
- **Prefab assets:** `prefab create`, `prefab create_variant`, `prefab apply_overrides`, `prefab save_contents` (scene-side prefab ops — `prefab instantiate`, `prefab unpack`, `prefab revert_overrides` — are Undo-tracked and stay fully batchable)
- **Scene persistence & build list:** `scene save`, `editor save_all`, `scene add_to_build`, `scene remove_from_build`, `build set_settings`
- **Settings:** `settings.audio set`, `settings.graphics set`, `settings.input set`, `lighting set_settings`, `navmesh set_settings`, `settings.physics set`, `settings.player set`, `settings.quality set`, `settings.tags_layers set`, `settings.time set`, `settings.multiplayer_roles set` (6000.6+), `authoring set_root`, `editor.playmode set_run_in_background`
- **Animation / Timeline assets:** `animation.clip create`, `animation.clip set_curve`, `animation.clip remove_curve`, `animation.animator create_controller`, `animation.animator add_layer`, `animation.animator add_parameter`, `animation.animator add_state`, `animation.animator add_transition`, `animation.timeline create`, `animation.timeline add_track`, `animation.timeline add_clip`
- **Materials:** `material set_properties` (persists the `.mat` asset to disk)
- **Baking:** `lighting bake`, `lighting clear_bake`, `navmesh bake`, `navmesh bake_surfaces`, `navmesh clear_bake`, `occlusion bake`, `occlusion clear_bake`

`scene open` and `scene create` are **always** rejected inside a batch (even with `transactional: false`): they clear the Editor Undo stack, which would silently destroy the batch's ability to revert anything that ran before them.

#### Timing, budget and cancellation

The batch executes in a **single main-thread turn** — the Editor UI is frozen for its duration, bounded by `timeout` (frame-spanning execution is future work). The elapsed time is checked cooperatively before each op after the first; when the timeout is about to run out, the remaining ops are reported `skipped: true`, the batch-level `error` is set to "batch timeout about to run out", and a transactional batch rolls back. For long batches, submit the request as a detached job (`"job": true` on `/api/exec`), which has no timeout — jobs also make the batch cancelable: cooperative cancellation (`POST /api/job/cancel`) is checked between ops and aborts the batch the same way ("batch canceled"). Per-op progress is reported (`op i/n: <command>`) and visible via `GET /api/progress`.

#### Dry run

`dry-run: true` validates without executing anything: it catches unknown commands, excluded/non-batchable (`not_batchable`) commands, transactional rejections (`not_batchable_transactional`), unknown/missing parameters, and unresolvable reference topology (unknown or forward references). It mutates nothing and reports `valid` plus a per-op `success`/`error`.

#### Excluded commands

The following are rejected with a structured `not_batchable` error:

- **Out of scope:** `build run`, `build.target switch`, every command in the `package` group, `editor.playmode start`/`editor.playmode stop`/`editor.playmode pause`, `script recompile`
- **Arbitrary code:** `script eval`, `script eval_file`, `script run`, and nested `job batch`
- **Modal-dialog hazard:** `editor invoke_menu` (menu items can open modal dialogs that wedge an unattended batch)
- **Cannot make progress:** synchronous `job wait` — the batch holds the main thread for its whole turn, so the editor state a condition watches cannot change; the wait would just burn its timeout inside the batch. Run an async `job wait` before/after the batch instead (`job wait_status`/`job wait_cancel` are quick reads and stay batchable)
- **Undo-stack wipe:** `scene open`, `scene create`
- **Async commands** (`test run`, `test list`, and any command returning `Task`): they are completed by `EditorApplication.update` callbacks, which cannot fire while the batch holds the main thread — dispatching one would freeze the Editor permanently
- **Runtime-only commands** (e.g. `runtime set_time_scale`): they belong to the Player server surface and cannot execute on the editor server

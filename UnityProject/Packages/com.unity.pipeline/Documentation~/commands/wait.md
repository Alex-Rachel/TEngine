# Wait commands

Server-side condition waiting (AUTHAPI-25). `job wait` waits until a runtime/editor member condition holds — evaluated on the Editor's per-frame loop — and can act in the **same frame** the condition first becomes true (e.g. screenshot the moment a match-end screen appears). It replaces the dominant agent anti-pattern of client-side polling loops: read a value via `script eval`, sleep in the client, repeat. That loop is token-expensive and slow, and its client round-trips can miss short-lived states entirely (a 2-second screen is easy to miss at ~1–2 s per poll). A server-side condition is a cheap per-frame delegate that can act atomically the frame it fires.

## Sync vs async: pick the right mode first

`/api/exec` executes **one command at a time** — a synchronous `job wait` holds that queue for its entire duration. The Editor itself never freezes (each poll is marshaled onto the main thread between sleeps), but **every other command queues behind the wait until it resolves**. That includes any command whose effect your condition is waiting for: if the condition needs a mutation you haven't issued yet, the mutation can never run, and the wait self-deadlocks until its timeout.

Use a **synchronous** wait only when BOTH hold:

- the wait is short — comfortably under your client's HTTP timeout (typically **< 30 s**), and
- the condition does **not** depend on any command you still need to issue (it will be satisfied by something already in motion: play-mode gameplay, a running bake, a timer, ...).

For **everything else**, pass `async=true`: you get a `waitId` back immediately, other commands keep executing, and you poll `job wait_status` (and cancel with `job wait_cancel`). This is the default choice for long waits and for any "issue command, then wait for its effect" flow.

A synchronous wait submitted as a detached job (`POST /api/exec` with `"job": true`) remains cancelable: `POST /api/job/cancel` interrupts the sleep loop and resolves the wait as `canceled`.

> Member resolution, coercion, and the read-only security policy are intended to be shared with the invoke/query surface (AUTHAPI-16). Until that shared resolver lands, `job wait` ships a self-contained resolver with the same rules that the two will converge on. Type and member-chain resolution is cached per wait — only the live target instance (an `ObjectRef` target or a `find-type` instance) is re-resolved each poll.

### `job wait`
Wait until a member condition holds, then optionally act in the same frame.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `condition` | yes | `–` | The condition object (see below). |
| `timeout` | no | `30` | Maximum seconds to wait (clamped to 0–600). A synchronous wait reports `timedOut` slightly before it, so the reply arrives first. |
| `poll-interval-ms` | no | `100` | How often to re-check (clamped to 16–5000 ms; NaN/Infinity are rejected). The sleep never exceeds the remaining budget, so a large interval cannot stretch a short timeout. |
| `on-met` | no | `–` | Follow-up run in the same frame the condition first holds: `{ capture: { view, source, output-path, width, height, include-image }, pause }`. `capture.source` (game view only): `camera` (default) renders a camera, with Screen Space - Overlay UI drawn on top on URP/HDRP; `screen` captures what the Game window shows, including overlay canvases (HUDs), opening a Game view when none is showing. See [`capture game_view`](capture.md#capture-game_view). |
| `return-history` | no | `false` | Include the observed `{ value, elapsedMs }` samples. A ring buffer keeps the **last 200** samples, so the tail near the transition is retained (the interesting part), not the first 200 identical observations. |
| `tolerate-missing` | no | `false` | Treat resolution failures (type/member/instance not found) as condition-not-met and retry until timeout, instead of failing fast. Enables "wait until X exists" — e.g. waiting for a spawned object via `find-type`. On timeout the last resolution error is included. |
| `async` | no | `false` | Return a `waitId` immediately and evaluate in the background; poll `job wait_status`, cancel with `job wait_cancel`. **Use for long waits and whenever the condition depends on a command you still need to issue.** |

**The `condition` object**

| Field | Required | Description |
|-------|----------|-------------|
| `member` | yes | Dotted member path. With no `target`/`find-type` it is a fully-qualified **static** path (e.g. `UnityEditor.EditorApplication.isPlaying`); with `target`/`find-type` it walks **instance** members (e.g. `State`, `Health.CurrentHealth`). Read-only fields/properties only — write-only members and methods are rejected. |
| `target` | no | Object handle (`instanceId` / `hierarchyPath` / `guid` / `path` / `globalId`) to read instance members from. Re-resolved every poll, so the referenced object may appear mid-wait (combine with `tolerate-missing`). |
| `find-type` | no | Fully-qualified `UnityEngine.Object` type name; the member walks from a **live** instance. Persistent objects (prefab/asset files on disk) and hidden editor internals (`hideFlags != None`) are excluded, so the wait never silently binds to a prefab asset instead of the scene instance. Scene instances are preferred; if several candidates remain, the lowest instance id is chosen deterministically and a `note` on the result reports the ambiguity — pass `target` to select one explicitly. |
| `op` | no | `equals` (default) \| `notEquals` \| `greaterThan` \| `lessThan` \| `contains` (strings) \| `changed` (met on the first change from the initially observed value; needs no `value`). |
| `value` | conditional | Comparison operand, coerced to the member's type (primitive, enum name, or string). Required for every op except `changed`. When either side is a `float`, `equals`/`notEquals` compare at **float precision** (widening a float to double manufactures noise digits — `0.9f` would otherwise never equal `0.9`). |

**`op: changed` is for value-like members** — primitives, enums, and strings. It compares the member's current value against the first observed value, so content mutations *inside* a reference-typed member (a list gaining an element, a field on a referenced object changing) are **not** detected: the reference itself must change. Likewise, `value`/`initialValue`/`history` snapshots of mutable reference types reflect the object's state at serialization time, not at observation time.

**Returns:** `WaitResult`

```
{
  "state": "met",            // pending | met | timedOut | interrupted | canceled | failed | not_found
  "met": true,
  "timedOut": false,
  "member": "MatchManager.Instance.State",
  "op": "equals",
  "value": "Ended",          // most recently observed value
  "initialValue": "Playing", // first observed value (baseline for op=changed)
  "elapsedMs": 512,          // frozen at the terminal transition
  "framesObserved": 6,
  "note": "…",               // present only for non-fatal resolution notes (e.g. ambiguous find-type)
  "capture": { "savedPath": "<project>/Screenshots/victory.png", "message": "" },  // present only with on-met.capture; message as in capture game_view
  "waitId": "…"              // present only for async submissions / status snapshots
}
```

- A **timeout** returns `met:false, timedOut:true` with the last observed `value` — distinct from a resolution/evaluation error, which returns `state:"failed"` with an `error` message (unless `tolerate-missing` turns resolution errors into retries; the wait then times out with the last resolution error attached).
- If the main thread stalls past the per-poll marshal budget (~60 s: a huge import, a modal bake), the poll is counted as **missed** and the wait keeps going until its own budget expires — a stall does not turn the wait into a generic error.
- The sync result is well under 2 KB without `history`.

### `job wait_status`
Get the status/result of an async wait (`job wait` with `async=true`).

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `wait-id` | yes | `–` | The id returned by `job wait` when `async=true`. |

**Returns:** `WaitResult` (`state:"not_found"` if the id is unknown — async waits do not survive a domain reload; completed waits are retained up to a bounded count and pruned oldest-first the next time a `job wait` is submitted, so a session that never submits another wait keeps its last results queryable).

### `job wait_cancel`
Cancel an async wait.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `wait-id` | yes | `–` | The id to cancel. |

**Returns:** `WaitResult`. If the wait was still active, it resolves to `state:"canceled"`. If it had already reached a terminal state (`met`/`timedOut`/`failed`/`interrupted`) before the cancel request arrived, that actual state is returned unchanged — the cancel had no effect, and reporting a fake `canceled` here would misreport whether an `on-met` follow-up (capture/pause) actually ran.

## Migration: replace a client polling loop with one call

Before — an agent screenshots a match-end screen by polling with `script eval` (dozens of round-trips, and the 2-second screen is easy to miss):

```
# ~60 client round-trips, each ~1–2s:
loop:
  state = script eval "MatchManager.Instance.State"     # read
  if state == "Ended": break
  sleep 1                                          # client-side sleep
capture game_view --source screen --output-path Screenshots/victory.png
```

After — one **async** wait that captures atomically in the frame the condition first holds, while other commands stay available (the match may take minutes, and you may still need to issue commands that drive it):

```
# 1. Arm the wait; returns { waitId } immediately:
job wait --condition '{"member":"MatchManager.Instance.State","op":"equals","value":"Ended"}' \
         --timeout 300 \
         --on-met '{"capture":{"view":"game","source":"screen","output-path":"Screenshots/victory.png"}}' \
         --async true

# 2. Keep working: start the match, drive gameplay, run other commands...

# 3. Collect the result (poll until state != "pending"):
job wait_status --wait-id <waitId>
```

Only use the synchronous form when the wait is short and self-contained — e.g. play mode is already running and the state flips within a few seconds:

```
job wait --condition '{"member":"MatchManager.Instance.State","op":"equals","value":"Ended"}' --timeout 10
```

## Waiting for something to exist (spawn waits)

By default the first failed resolution fails the wait (`state:"failed"`) — that catches typos fast. To wait for an object or member that does not exist *yet*, pass `tolerate-missing:true`:

```
job wait --condition '{"find-type":"MyGame.BossController","member":"IsActivated","op":"equals","value":true}' \
         --tolerate-missing true --timeout 120 --async true
```

Resolution failures are then treated as condition-not-met and retried every poll until the object appears (or the wait times out, with the last resolution error attached).

## Interruption and lifecycle

- Concurrent waits are capped (8); a further submission is rejected with a structured error telling you to `job wait_cancel` one or let one finish.
- A **domain reload** (`beforeAssemblyReload`) or **exiting play mode** resolves every active wait to `state:"interrupted"` rather than hanging or leaking. **Entering play mode does not interrupt**: with domain reload disabled ("Enter Play Mode Options"), a wait armed in edit mode survives into play mode — the natural way to arm a capture before pressing play. (With domain reload enabled, the reload interrupts it anyway.)
- `on-met.pause` pauses play mode on the frame the condition holds (no-op outside play mode).
- If the server is stopping while a synchronous wait is pending, the wait resolves as `interrupted` ("server stopping") instead of evaluating against a shutting-down editor.

## Result contract details

- `error` is set only when the wait itself ends abnormally (`failed`, `interrupted`, `canceled`,
  or the surfaced last resolution error on a tolerated timeout). A failed `on-met` follow-up
  (capture/pause) is reported separately as **`onMetError`** while `state` stays `met` — the
  condition did hold; check `onMetError` before trusting `capture`.
- `condition.target` and `condition.find-type` are **mutually exclusive** (rejected up front).
- Member paths resolve **public** readable fields/properties only.
- A **destroyed** Unity object mid-path fails structurally; as the **leaf** value it is observed
  as `null` (Unity's own `==` semantics), so "wait until destroyed" is
  `{ "op": "equals", "value": null }` — the `value` key must be present and explicitly `null`;
  omitting it is rejected for every op except `changed`.
- `tolerate-missing` retries the evaluator's STRUCTURED failures: resolution failures
  (type/member/instance not found, unless permanently malformed) and evaluation errors
  ("cannot compare yet" — e.g. an ordering op while the member is still null); the last error is
  surfaced if the wait times out. A member getter that THROWS fails the wait immediately,
  tolerated or not.
- A bare type name matching several loaded types binds deterministically (lowest full name) and
  surfaces a `note` — use the fully-qualified name to disambiguate.

## Interaction with `job batch`

`job wait` is rejected inside a `job batch` (`not_batchable`): the batch holds the main thread for its
whole turn, so the editor state a condition watches cannot change while the wait polls — it would
just burn its timeout. Run an async `job wait` before or after the batch and poll `job wait_status`
(both `job wait_status` and `job wait_cancel` are quick reads and stay batchable).

## Interaction with the startup settle gate

`job wait` is a background command (`MainThreadRequired=false`), so it is **exempt from the settling busy-gate** (AUTHAPI-35): it is accepted even while the Editor is still importing/compiling after a cold start. A wait polling during that window observes a half-ready editor — types may not be loaded and objects may not exist yet. Combine with `tolerate-missing:true` for a deliberate wait-until-ready pattern, or gate on `/api/status` reporting `ready` first if you want fully settled reads.

See [Creating commands](../creating-commands.md) and [Connectivity](../connectivity.md).

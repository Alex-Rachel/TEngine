# Profiling & memory capture commands

Record a Profiler capture (`.data`) or take a memory snapshot (`.snap`) from a running Editor or a
connected development Player. Both follow the `diagnostics run_audit` shape: the start command returns
immediately, and you poll a `*_status` command until it reports `completed`, then read the `path` it
gives you.

Both capture from whatever the Editor's Profiler is connected to: the same connection the Profiler
window's target dropdown sets. Choose it with `diagnostics profiler_connect`, or leave it where it is.

```
unity command diagnostics profiler_targets
unity command diagnostics profiler_connect --target editor
unity command diagnostics profiler_record --frames 300
unity command diagnostics profiler_record_status
```

## Targets

### `diagnostics profiler_targets`
List what the Editor's Profiler can connect to: this Editor plus any connected development Players.
`current` marks what `diagnostics profiler_record` and `diagnostics memory_snapshot` capture from.

No parameters.

**Returns:** `object` — `{ status, current, targets: [{ guid, name, isEditor, connectable, current, deepProfilingSupported }] }`.

**Notes:** `isEditor` means **this** Editor process, guid `-1`. A connection to another Editor is an
ordinary remote target. `deepProfilingSupported` is `false` for a Player built without Deep Profiling
Support. A Player attached through Autoconnect Profiler does not appear in Unity's own target list, so
the current connection is always listed even when Unity omits it.

### `diagnostics profiler_connect`
Point the Editor's Profiler connection at a target, as choosing it in the Profiler window does. The
connection stays there afterwards.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | **yes** | `–` | `editor` for this Editor, or a guid or name from `diagnostics profiler_targets`. A name may be a unique part of one. |

**Returns:** `object` — `{ status: "ok", target, guid, switched, message? }`, or `{ status: "error" | "busy", message }`.

**Notes:** Refused with `busy` while a recording or memory snapshot is in progress, because both read
from the connection for their whole run. A switched connection takes about 1.5 seconds to settle. A
recording or snapshot started in that window stays `queued` until it has, so you don't need to wait.

## Profiler recording

### `diagnostics profiler_record`
Record Unity Profiler frames from the connected target and save them to a `.data` file. Returns
immediately. Poll `diagnostics profiler_record_status` until status is `completed`, then read the path.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `frames` | no | `300` | Number of frames to record before stopping and saving. Use `0` to record until `diagnostics profiler_record_stop`. |
| `output-path` | no | `–` | Output `.data` path (absolute, or relative to the project root). Defaults to `<Profiler capture folder>/<productName>_<timestamp>_<captureId>.data`. Always written on the Editor machine, even when recording a Player. |
| `deep-profile` | no | `false` | Whether this is a deep-profiling recording. Must match the Profiler's Deep Profile setting; see [Deep profiling](#deep-profiling). |
| `timeout-seconds` | no | `300` | Stop and save what was recorded after this long. |

**Returns:** `object` — `{ status: "queued", captureId, path, target, message }`, or `{ status: "busy" | "error", message }`.

**Notes:** One recording runs at a time, and not alongside a memory snapshot; a second call returns
`busy`. Recording the **Editor** in Edit mode only advances frames as the Editor ticks, so keep
`editor set_auto_tick` enabled or the recording will time out with very few frames. While it records,
the Editor's Profiler records too, with its history cleared first. When the recording ends, Profiler
recording and the Editor's own profiling switch go back to how they were.

#### Frames recorded vs. frames saved

The Profiler keeps a bounded history — Preferences > Analysis > Profiler > Frame Count, 300 by default —
and the saved file holds what is in it. A recording that runs past that capacity, which includes any
`frames=0` recording left running, therefore loses its earliest frames.

The status reports both numbers: `framesRecorded` is what the recording observed and what `frames` is
measured against, `framesSaved` is what the file holds. When they differ the completed status says so in
`message`. Raise the Profiler frame count to keep more.

#### Deep profiling

Deep Profile is a single Profiler setting, and for the Editor it only takes effect through a script
reload, which would end the recording. This command therefore never changes it. `deep-profile` states
which kind of recording you expect, and the command fails at once if the setting disagrees, rather than
recording one kind and labelling it the other. Turn Deep Profile on or off in the Profiler window first.

A Player target also needs a development build made with Deep Profiling Support. The Editor otherwise
accepts deep profiling for a Player that ignores it, which would label an ordinary recording as deep.
The command checks `deepProfilingSupported` first and fails with a message naming the build option.

### `diagnostics profiler_record_stop`
Stop the recording early and save the frames recorded so far.

No parameters.

**Returns:** `ProfilerRecordStatus` (JSON) — the final status, as `diagnostics profiler_record_status`
would report it, or `{ status: "idle", message }` when nothing is queued or recording.

**Notes:** Saves before it returns. A recording stopped while still `queued`, or with no frames recorded,
reports `failed` rather than writing an empty file.

### `diagnostics profiler_record_status`
Get the status of the last Profiler recording: `idle` | `queued` | `recording` | `completed` | `failed` |
`interrupted`.

No parameters.

**Returns:** `ProfilerRecordStatus` (JSON) — `captureId`, `path`, `target`, `targetIsEditor`,
`framesRequested` and `deepProfile` throughout. `framesRecorded`, `framesSaved` and `elapsedSeconds`
while recording and once completed. `sizeBytes` when `completed`, `error` when `failed`.

**Notes:** `MainThreadRequired = false`, so it answers while the main thread is busy. The status is
refreshed about twice a second while recording. `interrupted` means a domain reload ended the
recording — a script change, or entering Play mode with domain reload enabled — and nothing was saved.
The Profiler's recording and Editor profiling switches are put back to what they were before the
recording started. To record Play mode, enter it
first, then start the recording.

## Memory snapshot

### `diagnostics memory_snapshot`
Capture a memory snapshot (`.snap`) of the connected target. Returns immediately. Poll
`diagnostics memory_snapshot_status` until status is `completed`, then read the path.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `flags` | no | see below | Comma-separated capture flags: `ManagedObjects`, `NativeObjects`, `NativeAllocations`, `NativeAllocationSites`, `NativeStackTraces`. Defaults to the Memory Profiler package's configured flags, else `ManagedObjects,NativeObjects`. |
| `output-path` | no | `–` | Output `.snap` path (absolute, or relative to the project root). Defaults to `<snapshot folder>/<productName>_<timestamp>_<snapshotId>.snap`. Always written on the Editor machine, even for a Player. |
| `collect-garbage` | no | `true` | Run `GC.Collect` before capturing, so entirely empty managed heap pages get unmapped. Editor only. |
| `timeout-seconds` | no | `600` | Give up waiting for the capture after this long. Raise it for slow remote connections. |

**Returns:** `object` — `{ status: "queued", snapshotId, path, target, message }`, or `{ status: "busy" | "error", message }`.

**Notes:** An unknown flag name is rejected with an error listing the valid ones. The callstack flags
capture nothing unless you started the target Editor or Player with `-enable-memoryprofiler-callstacks`.
`collect-garbage` is ignored for a Player, where it would collect the **Editor's** heap instead; the
`capturing` status says so in `message`. Snapshots are large, so expect hundreds of megabytes for a real
project. A snapshot does not run alongside a Profiler recording; the second call returns `busy`.

### `diagnostics memory_snapshot_status`
Get the status of the last memory snapshot: `idle` | `queued` | `capturing` | `completed` | `failed` |
`interrupted`.

No parameters.

**Returns:** `MemorySnapshotStatus` (JSON) — `path` and `sizeBytes` are populated when `completed`,
`error` when `failed`.

**Notes:** `MainThreadRequired = false`. A snapshot that passes `timeout-seconds` reports `failed`, but
because the underlying operation may still be running, further snapshots, Profiler recordings and
`diagnostics profiler_connect` stay refused until it finishes or a domain reload clears it. If it then
finishes, the status changes to its real result. `interrupted` means a domain reload landed while a
snapshot was `queued` or `capturing`.

## When the connection moves

The Profiler window can change the connection at any time, and nothing here can stop that. A snapshot
re-checks the connection immediately before it captures. A recording re-checks it before it starts, on
every poll while it records, and again immediately before the file is written. A connection that moved
fails the command, discarding any frames recorded so far, rather than saving another process's data
under the target that was asked for.

---

See [Creating commands](../creating-commands.md) and [Connectivity](../connectivity.md).

# Package Manager commands

These commands query and modify the project's UPM package set (list, search, add, remove, resolve, status) over `UnityEditor.PackageManager`. Read-only queries block until the registry responds and return synchronously; mutating commands (`package add`/`package remove`) require `confirm=true`, support `dry-run` previews, and are async by default (poll `package status`) with an optional `wait=true` to block until done.

### `package list`
List packages by scope: installed (default) | available (registry) | all (both). Returns the full result synchronously — available/all block until the registry query completes.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `scope` | no | `installed` | Which packages to list: installed (default) | available | all. |
| `include-indirect` | no | `true` | Include indirect (transitive) installed dependencies (applies to scope=installed/all). |
| `offline` | no | `false` | For available/all: query the local cache instead of the registry. |

**Returns:** `object`
**Notes:** `MainThreadRequired = false`. Synchronous — `available`/`all` start a registry query and block until it completes (up to `timeout`, default 60 s); `installed` reads the resolved set inline on the main thread.

### `package search`
Search packages available in the registry. Provide a name (e.g. com.unity.foo) or omit to list all. Returns the full result synchronously (blocks until the registry query completes).

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `query` | no | `–` | Package name to search for. Omit/empty to list all available packages. |
| `offline` | no | `false` | Search the local cache only. |

**Returns:** `object`
**Notes:** `MainThreadRequired = false`. Synchronous — blocks until the registry query completes (up to `timeout`, default 60 s). Each result is flagged with whether it is currently installed.

### `package add`
Add a UPM package by name@version, git URL, or 'file:' local path. Async by default (returns in_progress; poll `package status`); pass wait=true to block until added. A recompile/domain reload follows — poll `script recompile_status`. Requires confirm=true; use dry-run to preview.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `name` | yes | `–` | Package to add: 'com.unity.foo@1.2.3', a git URL, or 'file:../Path'. |
| `confirm` | no | `false` | Apply the change. Without it the call is refused. |
| `dry-run` | no | `false` | Preview the change without applying it. |
| `wait` | no | `false` | Block until the operation completes and return the result (synchronous). Default: return immediately and poll `package status`. |

**Returns:** `object`
**Notes:** `MainThreadRequired = false`. Mutating — requires `confirm=true`; `dry-run=true` returns a preview without applying. Async by default (returns `in_progress`; poll `package status`); with `wait=true` it blocks until completion (up to `timeout`, default 300 s). A successful add triggers a recompile/domain reload — poll `script recompile_status`. Returns `busy` if another package operation is already in progress.

### `package remove`
Remove a UPM package by name. Async by default (returns in_progress; poll `package status`); pass wait=true to block until removed. A recompile/domain reload follows — poll `script recompile_status`. Requires confirm=true; use dry-run to preview.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `name` | yes | `–` | Package name to remove (e.g. com.unity.foo). |
| `confirm` | no | `false` | Apply the change. Without it the call is refused. |
| `dry-run` | no | `false` | Preview the change without applying it. |
| `wait` | no | `false` | Block until the operation completes and return the result (synchronous). Default: return immediately and poll `package status`. |

**Returns:** `object`
**Notes:** `MainThreadRequired = false`. Mutating — requires `confirm=true`; `dry-run=true` returns a preview without applying. Async by default (returns `in_progress`; poll `package status`); with `wait=true` it blocks until completion (up to `timeout`, default 300 s). A successful remove triggers a recompile/domain reload — poll `script recompile_status`; the reload can drop a synchronous reply, so `package status` confirms the outcome. Returns `busy` if another package operation is already in progress.

### `package resolve`
Resolve/refresh packages from the manifest (re-fetch and re-link). May trigger a recompile/domain reload — poll `script recompile_status`. Its outcome is recorded for `package status`.

No parameters.

**Returns:** `object`
**Notes:** `MainThreadRequired = true`. Fire-and-forget — `Client.Resolve()` schedules a resolve on a later tick, so the response returns before any reload. May trigger a recompile/domain reload — poll `script recompile_status`. Its outcome is recorded in the status file, so `package status` validates it.

### `package status`
Status of the last async package operation (add/remove/resolve): idle | in_progress | completed | failed, with the added package, manifest, and any error.

No parameters.

**Returns:** `object`
**Notes:** `MainThreadRequired = false`. Reads a Temp status file that survives the domain reload an add/remove/resolve triggers, so a lost synchronous reply or an interrupted async op is recoverable by polling. Returns `{"status":"idle"}` when no operation has run.

See [Creating commands](../creating-commands.md) and [Connectivity](../connectivity.md).

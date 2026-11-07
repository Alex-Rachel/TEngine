# Project settings commands

Read and change a representative slice of Unity's project-wide settings (Audio, Graphics, Input, Physics, Player, Quality, Runtime Pipeline, Tags & Layers, Time). Each area is a `settings.<area>` group (`settings.audio`, `settings.graphics`, …) with a `get` and a `set` command. `get` reads the current values into a `ProjectSettingsResponse`, under the same kebab-case keys `set` accepts in its `settings` object, so a value read by `get` can be edited and sent back; `set` mutates them through the shared `confirm`/`dry-run` convention, so `confirm=true` is required to apply and `dry-run=true` previews the change without applying it. Every command in these groups is `MainThreadRequired = true`. On a `set`, omitted fields are left unchanged, and the group is re-read after the write so the response reflects the resulting state. Settings writes are not undoable via Ctrl+Z.

### `settings.audio get`
Read project Audio settings (volume, rolloff scale, doppler factor).

No parameters.

**Returns:** `ProjectSettingsResponse`
**Notes:** `MainThreadRequired = true`.

### `settings.audio set`
Change project Audio settings. Requires confirm=true; use dry-run to preview.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `settings` | no | `–` | Fields to change; omitted fields are left unchanged (fields below). |
| `confirm` | no | `false` | Apply the change. Without it the call is refused. |
| `dry-run` | no | `false` | Preview the change without applying it. |

`settings` fields:

| Field | Required | Default | Description |
|-------|----------|---------|-------------|
| `volume` | no | `–` | Global audio volume (0..1). |
| `rolloff-scale` | no | `–` | Global rolloff scale. |
| `doppler-factor` | no | `–` | Global doppler factor. |

**Returns:** `ProjectSettingsResponse`
**Notes:** `MainThreadRequired = true`. `confirm=true` required to apply; supports `dry-run`.

### `settings.graphics get`
Read GraphicsSettings (default render pipeline).

No parameters.

**Returns:** `ProjectSettingsResponse`
**Notes:** `MainThreadRequired = true`.

### `settings.graphics set`
Set the default render pipeline asset. Requires confirm=true; use dry-run to preview.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `settings` | no | `–` | Fields to change; omitted fields are left unchanged (fields below). |
| `confirm` | no | `false` | Apply the change. Without it the call is refused. |
| `dry-run` | no | `false` | Preview the change without applying it. |

`settings` fields:

| Field | Required | Default | Description |
|-------|----------|---------|-------------|
| `render-pipeline-asset` | no | `–` | Reference (path / guid / globalId) to a RenderPipelineAsset to set as the default. Pass an empty reference (`{}`) to select the built-in pipeline; omit to leave unchanged. |

**Returns:** `ProjectSettingsResponse`
**Notes:** `MainThreadRequired = true`. `confirm=true` required to apply; supports `dry-run`.

### `settings.input get`
Read the legacy Input Manager axes (names and count).

No parameters.

**Returns:** `ProjectSettingsResponse`
**Notes:** `MainThreadRequired = true`.

### `settings.input set`
Tune a legacy Input Manager axis (sensitivity/gravity/dead) by name. Requires confirm=true; use dry-run to preview.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `settings` | no | `–` | Axis change. 'axis' selects the axis by name; omitted numeric fields are left unchanged (fields below). |
| `confirm` | no | `false` | Apply the change. Without it the call is refused. |
| `dry-run` | no | `false` | Preview the change without applying it. |

`settings` fields:

| Field | Required | Default | Description |
|-------|----------|---------|-------------|
| `axis` | yes | – | Name of the axis to modify (e.g. 'Horizontal'). |
| `sensitivity` | no | `–` | New sensitivity. |
| `gravity` | no | `–` | New gravity. |
| `dead` | no | `–` | New dead-zone size. |

**Returns:** `ProjectSettingsResponse`
**Notes:** `MainThreadRequired = true`. `confirm=true` required to apply; supports `dry-run`.

### `settings.multiplayer_roles get`
Read Multiplayer Roles settings (enabled, safety-checks, strip-audio, strip-rendering, strip-ui). Unity 6000.6+ only.

No parameters.

**Returns:** `ProjectSettingsResponse`
**Notes:** `MainThreadRequired = true`. The Editor's role and per-object roles are in the
[Multiplayer Roles commands](multiplayer-roles.md).

### `settings.multiplayer_roles set`
Change Multiplayer Roles settings. Requires confirm=true; use dry-run to preview. Unity 6000.6+ only.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `settings` | no | `–` | Fields to change; omitted fields are left unchanged (fields below). |
| `confirm` | no | `false` | Apply the change. Without it the call is refused. |
| `dry-run` | no | `false` | Preview the change without applying it. |

`settings` fields:

| Field | Required | Default | Description |
|-------|----------|---------|-------------|
| `enabled` | no | `–` | Enable Multiplayer Roles for the project. Nothing is stripped for any role until they are enabled. |
| `safety-checks` | no | `–` | Warn on entering Play Mode or building about stripped objects that other objects still reference. |
| `strip-audio` | no | `–` | Strip common audio components from the server. |
| `strip-rendering` | no | `–` | Strip common rendering components from the server. |
| `strip-ui` | no | `–` | Strip common UI components from the server. |

**Returns:** `ProjectSettingsResponse`
**Notes:** `MainThreadRequired = true`. `confirm=true` required to apply; supports `dry-run`. Refused
while in Play Mode, where roles have already been applied.

### `settings.physics get`
Read Physics settings (gravity, solver iterations, bounce threshold).

No parameters.

**Returns:** `ProjectSettingsResponse`
**Notes:** `MainThreadRequired = true`.

### `settings.physics set`
Change Physics settings. Requires confirm=true; use dry-run to preview.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `settings` | no | `–` | Fields to change; omitted fields are left unchanged (fields below). |
| `confirm` | no | `false` | Apply the change. Without it the call is refused. |
| `dry-run` | no | `false` | Preview the change without applying it. |

`settings` fields:

| Field | Required | Default | Description |
|-------|----------|---------|-------------|
| `gravity-x` | no | `–` | Gravity X component. |
| `gravity-y` | no | `–` | Gravity Y component (e.g. -9.81). |
| `gravity-z` | no | `–` | Gravity Z component. |
| `default-solver-iterations` | no | `–` | Default solver iteration count. |
| `bounce-threshold` | no | `–` | Bounce threshold velocity. |

**Returns:** `ProjectSettingsResponse`
**Notes:** `MainThreadRequired = true`. `confirm=true` required to apply; supports `dry-run`.

### `settings.player get`
Read PlayerSettings (company/product/version, scripting backend, API level).

No parameters.

**Returns:** `ProjectSettingsResponse`
**Notes:** `MainThreadRequired = true`.

### `settings.player set`
Change PlayerSettings. Requires confirm=true; use dry-run to preview. Scripting backend / API level changes trigger a domain reload.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `settings` | no | `–` | Fields to change; omitted fields are left unchanged (fields below). |
| `confirm` | no | `false` | Apply the change. Without it the call is refused. |
| `dry-run` | no | `false` | Preview the change without applying it. |

`settings` fields:

| Field | Required | Default | Description |
|-------|----------|---------|-------------|
| `company-name` | no | `–` | Company name. |
| `product-name` | no | `–` | Product name. |
| `bundle-version` | no | `–` | Bundle/application version string. |
| `scripting-backend` | no | `–` | Scripting backend (e.g. Mono2x, IL2CPP). Triggers a domain reload. |
| `api-compatibility-level` | no | `–` | API compatibility level (e.g. NET_Standard_2_0, NET_Unity_4_8). Triggers a domain reload. |

**Returns:** `ProjectSettingsResponse`
**Notes:** `MainThreadRequired = true`. `confirm=true` required to apply; supports `dry-run`. Changing `scripting-backend` or `api-compatibility-level` triggers a domain reload (`requiresDomainReload`); scripting-backend / API-level changes are read and written against the active build target.

### `settings.quality get`
Read QualitySettings (current level, level names, vSync, anti-aliasing).

No parameters.

**Returns:** `ProjectSettingsResponse`
**Notes:** `MainThreadRequired = true`.

### `settings.quality set`
Change QualitySettings. Requires confirm=true; use dry-run to preview.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `settings` | no | `–` | Fields to change; omitted fields are left unchanged (fields below). |
| `confirm` | no | `false` | Apply the change. Without it the call is refused. |
| `dry-run` | no | `false` | Preview the change without applying it. |

`settings` fields:

| Field | Required | Default | Description |
|-------|----------|---------|-------------|
| `level` | no | `–` | Quality level index (see level-names from `settings.quality get`). |
| `vsync-count` | no | `–` | VSync count (0 = off, 1, 2). |
| `anti-aliasing` | no | `–` | MSAA sample count (0, 2, 4, 8). |

**Returns:** `ProjectSettingsResponse`
**Notes:** `MainThreadRequired = true`. `confirm=true` required to apply; supports `dry-run`.

### `settings.runtime_pipeline get`
Read Pipeline Runtime settings (enable-in-development-builds, enable-in-release-builds, port, enable-audit-logging, auto-start, max-work-items-per-frame). Reports the settings in effect, falling back to built-in defaults when none have been authored; never creates the settings file. The two toggles are reported for the active build target.

No parameters.

**Returns:** `ProjectSettingsResponse`
**Notes:** `MainThreadRequired = true`.

### `settings.runtime_pipeline set`
Change Pipeline Runtime settings. Requires confirm=true; use dry-run to preview.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `settings` | no | `–` | Fields to change; omitted fields are left unchanged (fields below). |
| `confirm` | no | `false` | Apply the change. Without it the call is refused. |
| `dry-run` | no | `false` | Preview the change without applying it. |

`settings` fields:

| Field | Required | Default | Description |
|-------|----------|---------|-------------|
| `enable-in-development-builds` | no | `–` | Compile the Pipeline into development builds. Writes a scripting define symbol for every installed build target, so it triggers a recompile. |
| `enable-in-release-builds` | no | `–` | Compile the Pipeline into release builds. SECURITY WARNING: a release build carrying the Pipeline exposes code execution over a local HTTP server. |
| `port` | no | `–` | HTTP port for the Pipeline server. 0 = auto-assign from range 7900-7999. |
| `enable-audit-logging` | no | `–` | Log every remote request for security auditing. |
| `auto-start` | no | `–` | Start the server automatically when the Player boots (or Play Mode is entered). |
| `max-work-items-per-frame` | no | `–` | Maximum work items the dispatcher processes per frame. |

**Returns:** `ProjectSettingsResponse`
**Notes:** `MainThreadRequired = true`. `confirm=true` required to apply; supports `dry-run`. Changing `enable-in-development-builds` or `enable-in-release-builds` triggers a domain reload (`requiresDomainReload`). Refused while in Play Mode — the running driver already loaded its own settings, so a change would silently not apply until the next Play session or build. `port` (0-65535) and `max-work-items-per-frame` (1-50) are rejected outright with a `Fail` response if outside these bounds — the same ones the Project Settings page enforces — rather than being clamped or silently applied.

### `settings.tags_layers get`
Read the project's tags and (named) layers.

No parameters.

**Returns:** `ProjectSettingsResponse`
**Notes:** `MainThreadRequired = true`.

### `settings.tags_layers set`
Add/remove tags and assign user layer names (index 8-31). Requires confirm=true; use dry-run to preview.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `settings` | no | `–` | Tag/layer changes to make (fields below). |
| `confirm` | no | `false` | Apply the change. Without it the call is refused. |
| `dry-run` | no | `false` | Preview the change without applying it. |

`settings` fields:

| Field | Required | Default | Description |
|-------|----------|---------|-------------|
| `add-tags` | no | `–` | Tag names to add. |
| `remove-tags` | no | `–` | Tag names to remove. |
| `set-layers` | no | `–` | User layer assignments (index 8-31); array of layer-assignment objects (fields below). |

`setLayers[]` (layer-assignment) fields:

| Field | Required | Default | Description |
|-------|----------|---------|-------------|
| `index` | yes | – | Layer index (8-31 for user layers). |
| `name` | yes | – | Layer name (empty string clears the slot). |

**Returns:** `ProjectSettingsResponse`
**Notes:** `MainThreadRequired = true`. `confirm=true` required to apply; supports `dry-run`. User layers are indices 8-31 (0-7 are reserved).

### `settings.time get`
Read Time settings (fixed-delta-time, maximum-delta-time, time-scale).

No parameters.

**Returns:** `ProjectSettingsResponse`
**Notes:** `MainThreadRequired = true`.

### `settings.time set`
Change Time settings. Requires confirm=true; use dry-run to preview.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `settings` | no | `–` | Fields to change; omitted fields are left unchanged (fields below). |
| `confirm` | no | `false` | Apply the change. Without it the call is refused. |
| `dry-run` | no | `false` | Preview the change without applying it. |

`settings` fields:

| Field | Required | Default | Description |
|-------|----------|---------|-------------|
| `fixed-delta-time` | no | `–` | Fixed timestep in seconds (e.g. 0.02). |
| `maximum-delta-time` | no | `–` | Maximum allowed timestep in seconds. |
| `time-scale` | no | `–` | Time scale (1 = real-time). |

**Returns:** `ProjectSettingsResponse`
**Notes:** `MainThreadRequired = true`. `confirm=true` required to apply; supports `dry-run`.

See [Creating commands](../creating-commands.md) and [Connectivity](../connectivity.md).

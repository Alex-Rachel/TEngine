# Baking commands

Bake and clear precomputed scene data: lightmaps, NavMesh, and occlusion culling. The three bake families have a group each: `lighting`, `navmesh` and `occlusion`. Each `bake` command runs asynchronously (returns immediately) — poll the matching `bake_status` in the same group until `completed`. The `clear_bake` commands are destructive and require `confirm=true`. The `get_settings` / `set_settings` pairs in `lighting` and `navmesh` read and tune the bake parameters.

## Lighting

### `lighting bake`
Trigger an async lightmap bake of the open scene(s) via Lightmapping.BakeAsync(). Returns immediately; poll `lighting bake_status` until completed.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `confirm` | no | `false` | Recommended (true): a bake overwrites existing lightmap data. Accepted for parity; not required. |
| `dry-run` | no | `false` | If true, validate there is an open bakeable scene and return the current lighting settings without baking. |

**Returns:** `object`

### `lighting bake_status`
Get the status of the last lighting bake: idle | baking | completed.

**Returns:** `object` — the bake's status payload, or `{"status":"idle"}` before the first bake.

### `lighting cancel_bake`
Cancel an in-progress lighting bake (Lightmapping.Cancel()).

**Returns:** `object`

### `lighting clear_bake`
Clear baked lightmap data for the open scene(s). Destructive: requires confirm=true.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `confirm` | no | `false` | Must be true to actually clear (destructive, not undoable via Unity's Undo). |
| `include-disk-cache` | no | `false` | If true, also clear the GI disk cache (Lightmapping.ClearDiskCache()). |
| `dry-run` | no | `false` | If true, report what would be cleared without clearing. |

**Returns:** `object`

### `lighting get_settings`
Read the active LightingSettings (lightmapper, bounces, resolution, directional mode, AO, etc.).

**Returns:** `LightingSettingsResult`

### `lighting set_settings`
Apply a subset of lighting settings to the active LightingSettings. Returns `{ applied[], unknown[] }`.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `settings` | yes | `–` | JSON object with a subset of lighting fields to set (same names/enums as `lighting get_settings`). |
| `dry-run` | no | `false` | If true, validate the keys and report applied/unknown without changing anything. |

**Returns:** `object`

## NavMesh

### `navmesh bake`
Trigger an async legacy NavMesh bake of the open scene(s) via UnityEditor.AI.NavMeshBuilder. Returns immediately; poll `navmesh bake_status` until completed.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `confirm` | no | `false` | Accepted for parity (a bake overwrites the existing NavMesh); not required. |
| `dry-run` | no | `false` | If true, validate there is an open scene and return current NavMesh settings without baking. |

**Returns:** `object`

### `navmesh bake_status`
Get the status of the last NavMesh bake: idle | baking | completed.

**Returns:** `object` — the bake's status payload, or `{"status":"idle"}` before the first bake.

### `navmesh cancel_bake`
Cancel an in-progress NavMesh bake (NavMeshBuilder.Cancel()).

**Returns:** `object`

### `navmesh clear_bake`
Clear the baked NavMesh for the open scene(s). Destructive: requires confirm=true.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `confirm` | no | `false` | Must be true to actually clear (destructive, not undoable via Unity's Undo). |
| `dry-run` | no | `false` | If true, report what would be cleared without clearing. |

**Returns:** `object`

### `navmesh get_settings`
Read the default agent's legacy NavMesh bake settings (agentRadius/Height/Slope/Climb, minRegionArea, voxelSize).

**Returns:** `NavMeshSettingsResult`

### `navmesh set_settings`
Apply a subset of legacy NavMesh bake settings to the default agent. Returns `{ applied[], unknown[] }`.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `settings` | yes | `–` | JSON object with a subset of NavMesh fields to set (same names as `navmesh get_settings`). |
| `dry-run` | no | `false` | If true, validate the keys and report applied/unknown without changing anything. |

**Returns:** `object`

### `navmesh bake_surfaces`
Bake NavMeshSurface components (AI Navigation package). v1 stub: returns package_not_found when the package is absent.

**Returns:** `object`

## Occlusion culling

### `occlusion bake`
Trigger an async occlusion-culling bake of the open scene(s) via StaticOcclusionCulling.GenerateInBackground(). Returns immediately; poll `occlusion bake_status` until completed.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `smallest-occluder` | no | `–` | Smallest object that will occlude others (meters). Defaults to Unity's current value. |
| `smallest-hole` | no | `–` | Smallest gap geometry can have that the view can see through (meters). Defaults to Unity's current value. |
| `backface-threshold` | no | `–` | Backface threshold (1-100); lower trims more backfaces. Defaults to Unity's current value. |
| `confirm` | no | `false` | Accepted for parity (a bake overwrites existing occlusion data); not required. |
| `dry-run` | no | `false` | If true, validate there is an open scene and report the parameters that would be used without baking. |

**Returns:** `object`

### `occlusion bake_status`
Get the status of the last occlusion bake: idle | baking | completed.

**Returns:** `object` — the bake's status payload, or `{"status":"idle"}` before the first bake.

### `occlusion cancel_bake`
Cancel an in-progress occlusion bake (StaticOcclusionCulling.Cancel()).

**Returns:** `object`

### `occlusion clear_bake`
Clear baked occlusion-culling data for the open scene(s). Destructive: requires confirm=true.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `confirm` | no | `false` | Must be true to actually clear (destructive, not undoable via Unity's Undo). |
| `dry-run` | no | `false` | If true, report what would be cleared without clearing. |

**Returns:** `object`

See [Creating commands](../creating-commands.md) and [Connectivity](../connectivity.md).

# Prefab commands

Commands covering the prefab lifecycle an agent needs to build content procedurally: saving a configured GameObject as a prefab asset, instantiating it into a scene, deriving a variant, applying/reverting instance overrides, unpacking, and editing prefab contents through the prefab stage. Asset paths are sandboxed to the authoring root; scene-side mutations are Undo-able, but prefab asset writes are AssetDatabase operations and are not part of Unity's Undo system.

### `prefab create`
Save a GameObject as a prefab asset at a project path; the source becomes a connected instance.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `source` | yes | – | Reference to the source GameObject to save as a prefab (globalId/path/guid/instanceId/hierarchyPath). |
| `path` | yes | – | Prefab asset path relative to the authoring root (the Assets/ prefix is optional and the .prefab extension is added if missing). e.g. Prefabs/Enemy or Prefabs/Enemy.prefab |

**Returns:** `AuthoringResult`
**Notes:** Scene-side effects are Undo-able; the prefab asset write is not undoable.

### `prefab instantiate`
Instantiate a prefab asset into a loaded scene and return the created instance.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `prefab` | yes | – | Reference to the prefab asset to instantiate (path/guid/globalId). |
| `scene-path` | no | `–` | Optional path of a loaded scene to instantiate into; defaults to the active scene. |
| `name` | no | `–` | Optional name for the created instance; defaults to the prefab name. |

**Returns:** `AuthoringResult`
**Notes:** Undo-able.

### `prefab create_variant`
Create a prefab variant asset that inherits from a base prefab.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `base` | yes | – | Reference to the base prefab asset (path/guid/globalId). |
| `path` | yes | – | Variant prefab asset path relative to the authoring root (.prefab added if missing). |

**Returns:** `AuthoringResult`
**Notes:** Prefab asset write is not undoable.

### `prefab apply_overrides`
Apply a prefab instance's overrides back to its source prefab asset.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `instance` | yes | – | Reference to a prefab instance GameObject in a scene (instanceId/hierarchyPath/globalId). |

**Returns:** `AuthoringResult`
**Notes:** Scene-side effects are Undo-able; the asset write is not. The instance's source prefab asset must live under the authoring root.

### `prefab revert_overrides`
Revert a prefab instance's overrides so it matches its source prefab asset.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `instance` | yes | – | Reference to a prefab instance GameObject in a scene (instanceId/hierarchyPath/globalId). |

**Returns:** `AuthoringResult`
**Notes:** Undo-able.

### `prefab unpack`
Unpack a prefab instance into plain GameObjects (outermost level or completely).

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `instance` | yes | – | Reference to a prefab instance GameObject in a scene (instanceId/hierarchyPath/globalId). |
| `completely` | no | `false` | If true, unpack all nested prefab levels (Completely); if false, only the outermost level (OutermostRoot). |

**Returns:** `AuthoringResult`
**Notes:** Undo-able.

### `prefab save_contents`
Open a prefab asset in an isolated prefab stage, apply a declarative edit, and save it back (nested-prefab safe).

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `prefab` | yes | – | Reference to the prefab asset to edit (path/guid/globalId). |
| `rename-child` | no | `–` | Optional child name (relative path under the root, e.g. 'Body/Head') to rename. |
| `new-name` | no | `–` | New name for the child identified by rename-child. |
| `set-active-child` | no | `–` | Optional child name (relative path under the root) whose active state to set. |
| `active` | no | `true` | Active state to apply when set-active-child is provided. |

**Returns:** `AuthoringResult`
**Notes:** Prefab asset write (prefab-stage save) is not part of Unity's Undo system. The prefab asset must live under the authoring root.

See [Creating commands](../creating-commands.md) and [Connectivity](../connectivity.md).

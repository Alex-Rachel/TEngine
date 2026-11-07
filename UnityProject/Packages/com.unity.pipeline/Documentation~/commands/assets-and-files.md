# Asset & file commands

Commands for creating, importing, moving, copying, renaming, deleting and finding project assets, plus folder and text-file operations. Every agent-supplied path is funnelled through the authoring-root sandbox (it rejects `../` and out-of-project writes); destructive or overwriting operations require `confirm=true` and support `dry-run`. AssetDatabase create/move/copy/rename/delete are not part of Unity's Undo system.

### `asset create`
Create a new ScriptableObject (or other UnityEngine.Object) asset of the given type at a path under the authoring root.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `path` | yes | – | Asset path relative to the authoring root, including extension (e.g. Data/Config.asset or Materials/Wall.mat). The Assets/ prefix is optional. |
| `type` | yes | – | Fully-qualified or short type name to instantiate (e.g. UnityEngine.Material, MyGame.GameConfig). Must derive from UnityEngine.Object and be creatable. |
| `shader` | no | `–` | Material-only (ignored otherwise): shader name to assign (e.g. Standard, "Universal Render Pipeline/Lit"). When omitted, defaults to "Universal Render Pipeline/Lit" if a Scriptable Render Pipeline is active, otherwise the built-in "Standard" shader (falling back to "Standard" if URP/Lit is unavailable). |
| `confirm` | no | `false` | Required (true) only when overwriting an existing asset at the path. Ignored when the path is empty. |
| `dry-run` | no | `false` | If true, validate inputs and report what would be created without writing anything. |

**Returns:** `AuthoringResult`
**Notes:** `confirm=true` required to overwrite an existing asset; supports `dry-run`.

### `asset import`
Import an external file (e.g. a texture, model, audio clip) into the project by copying it to a path under the authoring root, then importing it.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `source` | yes | – | Absolute filesystem path to the external file to import. |
| `path` | yes | – | Destination asset path relative to the authoring root, including extension. The Assets/ prefix is optional. |
| `confirm` | no | `false` | Required (true) only when overwriting an existing asset at the destination path. |
| `dry-run` | no | `false` | If true, validate inputs and report what would be imported without writing anything. |

**Returns:** `AuthoringResult`
**Notes:** `confirm=true` required to overwrite an existing asset; supports `dry-run`.

### `asset move`
Move (or rename via a new path) an asset to a new location under the authoring root. Preserves the asset's GUID.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `asset` | yes | – | Reference to the asset to move (path / guid / globalId). |
| `destination` | yes | – | Destination asset path relative to the authoring root, including extension. The Assets/ prefix is optional. |
| `dry-run` | no | `false` | If true, validate the move (via AssetDatabase.ValidateMoveAsset) without performing it. |

**Returns:** `AuthoringResult`
**Notes:** Supports `dry-run`.

### `asset copy`
Copy an asset to a new path under the authoring root. The copy gets a fresh GUID.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `asset` | yes | – | Reference to the asset to copy (path / guid / globalId). |
| `destination` | yes | – | Destination asset path relative to the authoring root, including extension. The Assets/ prefix is optional. |
| `confirm` | no | `false` | Required (true) only when overwriting an existing asset at the destination path. |
| `dry-run` | no | `false` | If true, validate inputs and report what would be copied without writing anything. |

**Returns:** `AuthoringResult`
**Notes:** `confirm=true` required to overwrite an existing asset; supports `dry-run`.

### `asset rename`
Rename an asset in place (keeps it in the same folder, keeps its GUID).

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `asset` | yes | – | Reference to the asset to rename (path / guid / globalId). |
| `new-name` | yes | – | New file name WITHOUT a folder path. The extension is preserved if omitted. |
| `dry-run` | no | `false` | If true, validate the rename without performing it. |

**Returns:** `AuthoringResult`
**Notes:** Supports `dry-run`.

### `asset delete`
Delete an asset from the project. Destructive: requires confirm=true.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `asset` | yes | – | Reference to the asset to delete (path / guid / globalId). |
| `confirm` | no | `false` | Must be true to actually delete. Without it the command refuses (destructive guard). |
| `dry-run` | no | `false` | If true, report the asset that would be deleted without deleting it. |

**Returns:** `AuthoringResult`
**Notes:** Destructive — `confirm=true` required; supports `dry-run`. Not undoable via Unity's Undo.

### `asset find`
Find assets by type and/or name and/or label, returning their path, GUID and type. At least one filter is required.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `type` | no | `–` | Type name to filter by (e.g. Material, GameObject, ScriptableObject, MyGame.GameConfig). Resolved to a System.Type and matched against each asset's actual main type. |
| `name` | no | `–` | Name substring to filter by (AssetDatabase name filter). |
| `label` | no | `–` | Asset label to filter by (AssetDatabase 'l:' filter). |
| `search-in` | no | `–` | Folder to scope the search to, relative to the authoring root (default: the authoring root). |
| `limit` | no | `200` | Maximum number of results to return (default 200). |

**Returns:** `FindAssetsResult`
**Notes:** At least one of `type`, `name`, or `label` is required.

### `asset set_import_settings`
Set import settings on an asset's AssetImporter (e.g. a texture's isReadable) and re-import it.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `asset` | yes | – | Reference to the asset whose importer to edit (path / guid / globalId). |
| `settings` | yes | – | JSON object of importer property/field names to values, e.g. {"isReadable": true, "textureType": "NormalMap"}. |
| `dry-run` | no | `false` | If true, validate which settings would apply (and which are unknown) without writing or re-importing. |

**Returns:** `SetImportSettingsResult`
**Notes:** Supports `dry-run`. Import settings live in the asset's .meta file and are not part of Unity's Undo system.

### `asset get_import_settings`
Read an asset's import settings, structured by importer type (texture/model/audio), including the default-platform fields and (for textures/audio) one platform override block.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `asset` | yes | – | Reference to the asset whose importer to read (path / guid / globalId). |
| `platform` | no | `Default` | Platform whose override to read: Default \| Standalone \| iOS \| Android \| WebGL \| tvOS. |

**Returns:** `GetImportSettingsResult`

### `asset create_folder`
Create a folder under the authoring root (creates intermediate folders).

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `path` | yes | – | Folder path relative to the authoring root (default Assets/); the Assets/ prefix is optional. e.g. Gameplay/Enemies or Assets/Gameplay/Enemies |

**Returns:** `AuthoringResult`

### `asset read_text`
Read a UTF-8 text file under the authoring root and return its contents.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `path` | yes | – | Text file path relative to the authoring root. The Assets/ prefix is optional. |
| `max-bytes` | no | `1048576` | Reject files larger than this many bytes (default 1048576 = 1 MiB) to avoid huge payloads. |

**Returns:** `ReadTextFileResult`
**Notes:** `MainThreadRequired = true`.

### `asset write_text`
Write UTF-8 text to a file under the authoring root, then import it. Overwriting an existing file requires confirm=true.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `path` | yes | – | Text file path relative to the authoring root, including extension. The Assets/ prefix is optional. |
| `contents` | yes | – | The full text content to write (replaces the file). |
| `confirm` | no | `false` | Required (true) only when overwriting an existing file at the path. |
| `dry-run` | no | `false` | If true, validate inputs and report what would be written without writing anything. |

**Returns:** `AuthoringResult`
**Notes:** `confirm=true` required to overwrite an existing file; supports `dry-run`. Filesystem writes are not part of Unity's Undo system.

See [Creating commands](../creating-commands.md) and [Connectivity](../connectivity.md).

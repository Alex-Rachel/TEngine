# Navigation & selection commands

Commands to read and drive the Editor selection, and to run Unity Search queries. All commands are read-only or non-destructive and run on the main thread.

### `gameobject get_selection`
Read the current Editor selection as structured object identities.

No parameters.

**Returns:** `SelectionResult`

### `gameobject set_selection`
Set the Editor selection to the given assets/scene objects.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `instance-ids` | no | `–` | Scene/loaded object instance IDs to select. |
| `paths` | no | `–` | Asset paths to select (e.g. Assets/Foo.prefab). |

**Returns:** `SelectionResult`

### `editor search`
Run a Unity Search query and return structured results.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `query` | yes | `–` | Unity Search query string, e.g. 't:Material', 'p: my asset', 'h: Main Camera'. |
| `limit` | no | `50` | Max results to return (capped 200). |

**Returns:** `SearchResult`

See [Creating commands](../creating-commands.md) and [Connectivity](../connectivity.md).

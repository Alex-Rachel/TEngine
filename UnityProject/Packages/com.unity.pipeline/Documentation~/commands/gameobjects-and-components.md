# GameObject & component commands

Commands for creating GameObjects, querying the scene hierarchy, mutating the core GameObject/Transform surface, and adding/removing components and editing their serialized properties. Scene mutations are wrapped in Unity's Undo system (so a multi-step action reverts as one collapsible step) and are blocked during Play mode. Objects are referenced by an `ObjectRef` handle (globalId/path/guid/instanceId/hierarchyPath), and results come back as `AuthoringResult` identities you can chain into a follow-up call.

### `gameobject create`
Create an empty GameObject or a built-in primitive (cube/sphere/capsule/cylinder/plane/quad) in the active scene.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `name` | no | `–` | Name for the new GameObject. Defaults to 'GameObject' (or the primitive name). |
| `primitive` | no | `–` | Optional primitive type: cube, sphere, capsule, cylinder, plane, quad. Omit for an empty GameObject. |
| `parent` | no | `–` | Optional parent handle (globalId/path/guid/instanceId/hierarchyPath). The new object becomes a child of it. |

**Returns:** `AuthoringResult`
**Notes:** Undo-able; blocked during Play mode.

### `gameobject create_many`
Batch-create N empty GameObjects or primitives in one call. Optional positions/rotations/scales are arrays of [x,y,z] (length must equal count). Returns the created identities.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `name` | no | `–` | Base name. With count>1 and no explicit names, objects are suffixed Name1..NameN. |
| `primitive` | no | `–` | Optional primitive type: cube, sphere, capsule, cylinder, plane, quad. Omit for empty GameObjects. |
| `parent` | no | `–` | Optional parent handle. Every created object becomes a child of it. |
| `count` | no | `1` | How many GameObjects to create. Default 1. |
| `positions` | no | `–` | Local positions, one [x,y,z] per object. Length must equal count when supplied. |
| `rotations` | no | `–` | Local Euler rotations (degrees), one [x,y,z] per object. Length must equal count when supplied. |
| `scales` | no | `–` | Local scales, one [x,y,z] per object. Length must equal count when supplied. |

**Returns:** `CreateGameObjectsResult`
**Notes:** Undo-able (the whole batch reverts as one step); blocked during Play mode.

### `gameobject find`
Find GameObjects in loaded scenes by name, tag, component type, and/or hierarchy path (filters are combined). Returns structured identities.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `name` | no | `–` | Exact name to match. |
| `tag` | no | `–` | Tag to match (e.g. 'Player'). |
| `type` | no | `–` | Component type name to match (e.g. 'Rigidbody', 'UnityEngine.Camera'). |
| `hierarchy-path` | no | `–` | Exact hierarchy path to match (e.g. '/Root/Child'). |
| `include-inactive` | no | `true` | Include inactive GameObjects. Default true. |

**Returns:** `FindGameObjectsResult`

### `gameobject set_transform`
Set a GameObject's local position/rotation(euler)/scale. Omitted channels are left unchanged.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | – | Handle of the GameObject to modify. |
| `position` | no | `–` | Local position as [x,y,z]. |
| `rotation` | no | `–` | Local rotation as Euler angles [x,y,z] in degrees. |
| `scale` | no | `–` | Local scale as [x,y,z]. |

**Returns:** `AuthoringResult`
**Notes:** Undo-able; blocked during Play mode.

### `gameobject set_parent`
Reparent a GameObject under a new parent, or detach it to scene root when no parent is given.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | – | Handle of the GameObject to reparent. |
| `parent` | no | `–` | Handle of the new parent. Omit (or empty) to move the object to the scene root. |
| `world-position-stays` | no | `true` | Keep the object's world position when reparenting. Default true. |

**Returns:** `AuthoringResult`
**Notes:** Undo-able; blocked during Play mode.

### `gameobject set_active`
Set a GameObject's active self-state (activeSelf).

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | – | Handle of the GameObject. |
| `active` | yes | – | Desired active state. |

**Returns:** `AuthoringResult`
**Notes:** Undo-able; blocked during Play mode.

### `gameobject set_tag`
Set a GameObject's tag (the tag must already exist in the project).

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | – | Handle of the GameObject. |
| `tag` | yes | – | Tag to assign (must exist in the Tag Manager). |

**Returns:** `AuthoringResult`
**Notes:** Undo-able; blocked during Play mode.

### `gameobject set_layer`
Set a GameObject's layer by name or numeric index (0-31).

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | – | Handle of the GameObject. |
| `layer` | yes | – | Layer name (e.g. 'UI') or numeric index 0-31. |

**Returns:** `AuthoringResult`
**Notes:** Undo-able; blocked during Play mode.

### `gameobject rename`
Rename a GameObject.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | – | Handle of the GameObject. |
| `name` | yes | – | New name. |

**Returns:** `AuthoringResult`
**Notes:** Undo-able; blocked during Play mode.

### `gameobject delete`
Delete a GameObject from the scene (reversible via Undo).

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | – | Handle of the GameObject to delete. |

**Returns:** `AuthoringResult`
**Notes:** Undo-able; blocked during Play mode. The result describes the object's identity captured before destruction.

### `component add`
Add a component (by type name) to a GameObject.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | – | Handle of the GameObject. |
| `type` | yes | – | Component type name (e.g. 'Rigidbody' or 'UnityEngine.Camera'). |

**Returns:** `AuthoringResult`
**Notes:** Undo-able; blocked during Play mode.

### `component remove`
Remove a component from a GameObject. Provide either a component handle (target) or a GameObject handle (target) plus a type name.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | – | Handle of the component to remove, OR of the GameObject when 'type' is given. |
| `type` | no | `–` | Component type name to remove from the target GameObject (omit when 'target' already points at a component). |

**Returns:** `AuthoringResult`
**Notes:** Undo-able; blocked during Play mode.

### `component get_properties`
Get a component's serialized properties as a JSON map. Address the component by handle, or by GameObject handle + type.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | – | Handle of the component, OR of the GameObject when 'type' is given. |
| `type` | no | `–` | Component type name on the target GameObject (omit when 'target' is a component handle). |

**Returns:** `ComponentPropertiesResult`

### `component set_properties`
Set serialized properties on a component (one Undo step). 'properties' maps property name -> value; object references accept an ObjectRef handle.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | – | Handle of the component, OR of the GameObject when 'type' is given. |
| `properties` | yes | – | Map of serialized property name to value. Vectors/colors are arrays; object refs are handle objects. |
| `type` | no | `–` | Component type name on the target GameObject (omit when 'target' is a component handle). |

**Returns:** `ComponentPropertiesResult`
**Notes:** Undo-able (one Undo step); blocked during Play mode. An unknown property name fails the whole batch (no partial apply).

See [Creating commands](../creating-commands.md) and [Connectivity](../connectivity.md).

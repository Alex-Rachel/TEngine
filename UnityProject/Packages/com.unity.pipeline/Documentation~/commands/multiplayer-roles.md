# Multiplayer Roles commands

Commands to read and change **Multiplayer Roles**: the role the Editor runs as in Play Mode
(`Client`, `Server` or `ClientAndServer`), which roles each GameObject and component is kept for,
the role a build profile builds for, and the project-wide rules that strip components from a role
automatically.

A role decides what is **stripped**: an object kept only for `Client` is removed when the Editor
enters Play Mode as `Server`, and left out of a server build. Nothing is stripped while Multiplayer
Roles are disabled, whatever the roles say.

These commands work on one Editor. To run a client and a server side by side, give each player of a
[Play Mode Scenario](multiplayer-play-mode.md) its own role.

## Availability

**The `multiplayer.role` commands require Unity 6000.6 or newer.** They call the public
`Unity.Multiplayer.Editor.EditorMultiplayerRolesManager` API in the built-in
`UnityEditor.MultiplayerModule`, which is internal to the engine before 6000.6.

On older Editors the commands are **absent from `/api/commands`** and calling one fails as an unknown
command. Probe `/api/commands` rather than branching on an `available` field. The package as a whole
still supports Unity 2022.3; only this command group is gated.

## Roles

Every `role` argument takes one of `client`, `server` or `client_and_server`. Case, whitespace,
`_` and `-` are ignored, so `ClientAndServer` and `client-and-server` work too. Results report the
role as Unity names it: `Client`, `Server` or `ClientAndServer`.

## The Editor's role

Project-wide settings (enabling roles, safety checks, the server's automatic stripping) are project
settings, so they are in [`settings.multiplayer_roles get` / `set`](project-settings.md#settingsmultiplayer_roles-get)
with the other `settings.*` groups.

### `multiplayer.role get_active`
Get the role the Editor runs as in Play Mode, and whether Multiplayer Roles are enabled.

No parameters.

**Returns:** `{ activeRole, enabled, note }`
**Notes:** `note` says when roles are disabled, and how to enable them.

### `multiplayer.role set_active`
Set the role the Editor runs as in Play Mode.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `role` | yes | `–` | Role to run as. |

**Returns:** `{ activeRole, previousRole, note }`
**Notes:** Also updates the Multiplayer Role dropdown in the main toolbar. `note` says when roles are
disabled, in which case the role strips nothing until they are enabled. Refused in Play Mode.

## Objects

### `multiplayer.role get_game_object`
Get the roles a GameObject is kept for.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | `–` | Handle of a GameObject. A component handle is refused; use [`get_component`](#multiplayerrole-get_component). |

**Returns:** `{ target, role }`, where `target` is the object's canonical identity.

### `multiplayer.role set_game_object`
Set the roles a GameObject is kept for. It is stripped from the other roles.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | `–` | Handle of a GameObject. A component handle is refused; use [`set_component`](#multiplayerrole-set_component). |
| `role` | yes | `–` | Roles to keep the GameObject for. |

**Returns:** `{ target, role }`
**Notes:** One Undo step. Marks the object's scene dirty; save the scene to keep the change.

### `multiplayer.role get_component`
Get the roles a component is kept for.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | `–` | Handle of the component (for example its GlobalObjectId), or of its GameObject when `type` is given. |
| `type` | no | `–` | Component type name on the target GameObject. Omit when `target` is a component handle. |

**Returns:** `{ target, role }`, where `target` is the component's canonical identity.

### `multiplayer.role set_component`
Set the roles a component is kept for. It is stripped from the other roles.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `target` | yes | `–` | Handle of the component (for example its GlobalObjectId), or of its GameObject when `type` is given. |
| `role` | yes | `–` | Roles to keep the component for. |
| `type` | no | `–` | Component type name on the target GameObject. Omit when `target` is a component handle. |

**Returns:** `{ target, role }`
**Notes:** One Undo step. Marks the object's scene dirty; save the scene to keep the change. A
component marked `[MultiplayerRoleRestricted]` is never stripped, so setting its role fails with an error.
Addressing follows the [`component`](gameobjects-and-components.md) commands: a GameObject
handle plus `type`, or the component's own handle.

## Builds

### `multiplayer.role get_build`
Get the role a build profile builds for.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `profile` | no | active | Build profile asset path or name, as [`build list_profiles`](build-and-compilation.md) reports it. |

**Returns:** `{ profile, target, role }`. `profile` is the profile's asset path; when no profile is
named and none is active, `profile` is `null` and `target` names the active build target (with its
standalone subtarget) whose role is reported.

### `multiplayer.role set_build`
Set the role a build profile builds for.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `role` | yes | `–` | Role to build for. |
| `profile` | no | active | Build profile asset path or name. |

**Returns:** `{ profile, target, role }`, as `multiplayer.role get_build`.
**Notes:** Saves the profile asset. Unity rejects a role the platform can't build for, such as
`Server` on a platform with no server subtarget.

## Automatic selection

### `multiplayer.role list_component_types`
List the component types the project keeps for specific roles, beyond the server's audio,
rendering and UI stripping.

No parameters.

**Returns:** `{ count, componentTypes: [{ type, role }] }`

### `multiplayer.role set_component_type`
Keep every component of a type for the given roles only, project-wide.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `type` | yes | `–` | Component type name (e.g. `AudioSource` or `UnityEngine.Camera`). |
| `role` | yes | `–` | Roles to keep the component type for. `client_and_server` keeps it everywhere. |

**Returns:** `{ type, role, note }`
**Notes:** Refused in Play Mode.

## Play Mode

Roles are applied when the Editor enters Play Mode, so the commands that change the Editor's role
or the project's settings (`set_active`, `set_component_type`, and `settings.multiplayer_roles set`) refuse to run in
Play Mode rather than report a change that would not apply until the next entry. Exit with
`editor.playmode stop` first.

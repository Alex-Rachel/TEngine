# Material & shader commands

Read and edit material shader properties, and discover/introspect shaders so an agent can pick a valid shader name and property set.

## Materials

### `material get_properties`
Read a material's shader, render queue, enabled keywords, and all shader properties with their current values (Color as [r,g,b,a], Vector as [x,y,z,w], Texture as an object reference).

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `material` | yes | `–` | Reference to the .mat asset (or a loaded material) to read (path / guid / globalId / instanceId). |

**Returns:** `MaterialPropertiesResult`

### `material set_properties`
Set shader properties on a material (Float/Range/Int=number; Color=[r,g,b,a] or "#RRGGBBAA" hex; Vector=[x,y,z,w]; Texture=an object reference or null to clear), optionally reassign the shader, set the render queue, and toggle keywords. Unknown names / type mismatches are reported in `unknown[]`.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `material` | yes | `–` | Reference to the .mat asset (or a loaded material) to edit (path / guid / globalId / instanceId). |
| `shader` | no | `–` | Reassign the material's shader by name (e.g. "Standard", "Universal Render Pipeline/Lit", or a Shader Graph shader name). Applied before properties so new property names resolve against the new shader. |
| `properties` | no | `–` | JSON object of shader property name -> value. Names must include the leading underscore (e.g. _BaseColor). Float/Range/Int=number; Color=[r,g,b,a] or hex string; Vector=[x,y,z,w]; Texture=an object reference {guid/path} or null. |
| `render-queue` | no | `–` | Explicit render queue, or -1 to inherit from the shader. Omit to leave unchanged. |
| `enable-keywords` | no | `–` | Shader keywords to enable (e.g. _NORMALMAP, _EMISSION). |
| `disable-keywords` | no | `–` | Shader keywords to disable. |
| `confirm` | no | `false` | Reserved for parity; editing an existing material is non-destructive and undoable, so it is not required. |
| `dry-run` | no | `false` | If true, validate the shader, resolve property names and texture refs, and report applied[]/unknown[] without writing anything. |

**Returns:** `SetMaterialPropertiesResult`

## Shaders

### `material list_shaders`
Discover available shaders so an agent can pick a valid name for `material set_properties` / `asset create`. Returns `[{ name, assetPath, isBuiltin, isSupported }]` — `assetPath` is omitted for built-in shaders in the default lean reply (pass `verbose: true` on the request for an explicit `assetPath: null`); use `isBuiltin` to classify.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `filter` | no | `–` | Case-insensitive substring matched against the shader name (e.g. "URP", "Lit"). |
| `include-builtin` | no | `true` | Include built-in/engine shaders (those with no project asset path). |
| `limit` | no | `200` | Maximum number of shaders to return. |

**Returns:** `object` (list of `ShaderInfo`)

### `material get_shader_properties`
Introspect a shader's declared property list (name, description, type Color|Vector|Float|Range|TexEnv|Int, range, textureDimension, flags). Provide `shader` (by name) OR `material` (read the shader off that material).

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `shader` | no | `–` | Shader name (e.g. "Universal Render Pipeline/Lit"). Provide this OR 'material'. |
| `material` | no | `–` | Reference to a material to read the shader from instead of naming it. Provide this OR 'shader'. |

**Returns:** `ShaderPropertiesResult`

See [Creating commands](../creating-commands.md) and [Connectivity](../connectivity.md).

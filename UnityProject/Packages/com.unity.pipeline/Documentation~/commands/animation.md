# Animation commands

Author animation assets: AnimationClips (and their float curves), AnimatorControllers (parameters, layers, states, transitions), and Timeline assets. Create commands write under the authoring root and follow the shared `confirm`/`dry-run` convention.

## AnimationClips

### `animation.clip create`
Create an empty .anim AnimationClip asset under the authoring root, with an optional frame rate and loop flag.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `path` | yes | `–` | Asset path ending in .anim, relative to the authoring root. The Assets/ prefix is optional. |
| `frame-rate` | no | `60` | Sampling frame rate of the clip. |
| `loop` | no | `false` | If true, set the clip's loop-time flag in its AnimationClipSettings. |
| `confirm` | no | `false` | Required (true) only when overwriting an existing asset at the path. |
| `dry-run` | no | `false` | If true, validate inputs and report what would be created without writing anything. |

**Returns:** `AuthoringResult`

### `animation.clip set_curve`
Add or replace a single float curve binding on an AnimationClip (via AnimationUtility.SetEditorCurve). Replacing an existing binding overwrites it rather than duplicating.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `clip` | yes | `–` | Reference to the AnimationClip to edit (path / guid / globalId). |
| `path` | no | `""` | GameObject path relative to the animated root the property lives on. Empty string targets the root. |
| `type` | yes | `–` | Component type the property lives on, e.g. "Transform", "UnityEngine.Light". Resolved via the component TypeResolver. |
| `property` | yes | `–` | Curve property name, e.g. "m_LocalPosition.x", "m_LocalScale.y", "localEulerAnglesRaw.z". |
| `keys` | yes | `–` | Keyframes: `[{ time, value, inTangent?, outTangent?, weightedMode?: "None"\|"In"\|"Out"\|"Both" }]`. Omitted tangents default to 0 (flat); this is NOT Unity's Auto tangent mode. |
| `dry-run` | no | `false` | If true, validate type/property/keys without writing the curve. |

**Returns:** `SetAnimationCurveResult`

### `animation.clip get`
Read an AnimationClip's metadata and all float curve bindings (optionally with keyframes).

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `clip` | yes | `–` | Reference to the AnimationClip to read (path / guid / globalId). |
| `include-keys` | no | `false` | If true, include each binding's keyframes. |

**Returns:** `AnimationClipInfo`

### `animation.clip remove_curve`
Remove a float curve binding from an AnimationClip (SetEditorCurve(clip, binding, null)). Destructive: requires confirm=true.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `clip` | yes | `–` | Reference to the AnimationClip to edit (path / guid / globalId). |
| `path` | no | `""` | GameObject path relative to the animated root the binding lives on. Empty string targets the root. |
| `type` | yes | `–` | Component type of the binding to remove, e.g. "Transform". Resolved via the component TypeResolver. |
| `property` | yes | `–` | Curve property name to remove, e.g. "m_LocalPosition.x". |
| `confirm` | no | `false` | Must be true to actually remove the binding (destructive guard). |
| `dry-run` | no | `false` | If true, report the binding that would be removed without removing it. |

**Returns:** `SetAnimationCurveResult`

## AnimatorControllers

### `animation.animator create_controller`
Create an .controller AnimatorController asset (with a default Base Layer) under the authoring root.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `path` | yes | `–` | Asset path ending in .controller, relative to the authoring root. The Assets/ prefix is optional. |
| `confirm` | no | `false` | Required (true) only when overwriting an existing asset at the path. |
| `dry-run` | no | `false` | If true, validate inputs and report what would be created without writing anything. |

**Returns:** `AuthoringResult`

### `animation.animator add_parameter`
Add a parameter (Float | Int | Bool | Trigger) to an AnimatorController. A duplicate name returns code 'duplicate_parameter'.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `controller` | yes | `–` | Reference to the AnimatorController to edit (path / guid / globalId). |
| `name` | yes | `–` | Parameter name. |
| `type` | yes | `–` | Parameter type: Float \| Int \| Bool \| Trigger. |
| `default-value` | no | `–` | Default value for Float/Int/Bool (ignored for Trigger). |
| `dry-run` | no | `false` | If true, validate inputs without writing the parameter. |

**Returns:** `object`

### `animation.animator add_layer`
Add a layer to an AnimatorController.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `controller` | yes | `–` | Reference to the AnimatorController to edit (path / guid / globalId). |
| `name` | yes | `–` | Layer name. |
| `weight` | no | `1` | Layer weight. |
| `blending-mode` | no | `Override` | Blending mode: Override \| Additive. |
| `dry-run` | no | `false` | If true, validate inputs without writing the layer. |

**Returns:** `object`

### `animation.animator add_state`
Add a state to a layer, optionally with a motion (AnimationClip or BlendTree) and as the layer default. A layer name with no match returns code 'layer_not_found'.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `controller` | yes | `–` | Reference to the AnimatorController to edit (path / guid / globalId). |
| `layer` | no | `0` | Layer index (int) or name (string). Default 0 (Base Layer). |
| `name` | yes | `–` | State name. |
| `motion` | no | `–` | Optional AnimationClip or BlendTree asset to assign as the state's motion. |
| `is-default` | no | `false` | If true, set this state as the layer's default state. |
| `position` | no | `–` | Optional [x, y] node position in the graph (cosmetic). |
| `dry-run` | no | `false` | If true, validate inputs without writing the state. |

**Returns:** `object`

### `animation.animator add_transition`
Add a transition between two states (or from AnyState/Entry, to Exit) on a layer, with optional conditions. Validates that the states exist and each condition's parameter exists and its mode matches the parameter type.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `controller` | yes | `–` | Reference to the AnimatorController to edit (path / guid / globalId). |
| `layer` | no | `0` | Layer index (int) or name (string). Default 0 (Base Layer). |
| `from-state` | yes | `–` | Source state name, or the special "AnyState" / "Entry". |
| `to-state` | yes | `–` | Destination state name, or the special "Exit". |
| `conditions` | no | `–` | Optional conditions: `[{ parameter, mode: "If"\|"IfNot"\|"Greater"\|"Less"\|"Equals"\|"NotEqual", threshold? }]`. |
| `has-exit-time` | no | `false` | If true, the transition uses exit time. |
| `exit-time` | no | `0` | Normalized exit time (0..1) when hasExitTime is set. |
| `duration` | no | `0.25` | Transition duration in seconds. |
| `has-fixed-duration` | no | `true` | If true, duration is in seconds; otherwise normalized. |
| `dry-run` | no | `false` | If true, validate everything (states, parameters, mode/type) without writing the transition. |

**Returns:** `object`

### `animation.animator get_controller`
Read an AnimatorController's full structure: parameters, layers, states (with motion / default), and transitions (with conditions).

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `controller` | yes | `–` | Reference to the AnimatorController to read (path / guid / globalId). |

**Returns:** `AnimatorControllerInfo`

## Timeline

The Timeline commands require the `com.unity.timeline` package.

### `animation.timeline create`
Create a .playable TimelineAsset under the authoring root (optional frame rate).

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `path` | yes | `–` | Asset path ending in .playable, relative to the authoring root. The Assets/ prefix is optional. |
| `frame-rate` | no | `60` | Timeline frame rate. |
| `confirm` | no | `false` | Required (true) only when overwriting an existing asset at the path. |
| `dry-run` | no | `false` | If true, validate inputs and report what would be created without writing anything. |

**Returns:** `object`

### `animation.timeline add_track`
Add a track (Animation | Audio | Activation | Control | Playable | Signal | Marker) to a TimelineAsset, optionally nested under a parent group/track.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `timeline` | yes | `–` | Reference to the TimelineAsset to edit (path / guid / globalId). |
| `track-type` | yes | `–` | Track type: Animation \| Audio \| Activation \| Control \| Playable \| Signal \| Marker. |
| `name` | no | `–` | Optional track display name. |
| `parent-track` | no | `–` | Optional name of an existing group/track to nest the new track under. |
| `dry-run` | no | `false` | If true, validate inputs without writing the track. |

**Returns:** `object`

### `animation.timeline add_clip`
Add a clip to a named track on a TimelineAsset. For Animation tracks pass an AnimationClip asset; for Audio tracks an AudioClip.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `timeline` | yes | `–` | Reference to the TimelineAsset to edit (path / guid / globalId). |
| `track` | yes | `–` | Target track name. |
| `start` | yes | `–` | Clip start time in seconds. |
| `duration` | yes | `–` | Clip duration in seconds. |
| `asset` | no | `–` | Source asset: for Animation tracks an AnimationClip; for Audio tracks an AudioClip. Required for those track types. |
| `dry-run` | no | `false` | If true, validate inputs without writing the clip. |

**Returns:** `object`

### `animation.timeline get`
Read a TimelineAsset's structure: frame rate, duration, and its tracks with their clips.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `timeline` | yes | `–` | Reference to the TimelineAsset to read (path / guid / globalId). |

**Returns:** `object`

See [Creating commands](../creating-commands.md) and [Connectivity](../connectivity.md).

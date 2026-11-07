# Capture commands

Commands that render a camera, the Scene View, or a UI Toolkit element to a PNG and return it base64-encoded so an agent can "see" the editor (or a running player) without a display.

### `capture game_view`
Render the game view to a PNG. Returns it inline as base64, unless `output-path` is set — then the result is **path-only** (no base64) so agent tool results stay small; pass `include-inline-image=true` to get both.

`source` selects what is captured. `screen` (default) reads the game view's own render target, so the PNG matches what the Game window shows: at its true pixel resolution, independent of OS display scaling, and with the on-screen color tone preserved. In Play Mode that includes **Screen Space - Overlay UI** (HUDs, menus, damage numbers on overlay canvases); in Edit Mode the capture works the same way and simply contains no overlay UI, because none exists until runtime. `screen` reads the play mode view last in use: the Game view, or the Device Simulator when that is the one in use. When none is open, `screen` opens a Game view, leaves it open so later captures read it directly, and the result's `message` says so. A Game view in a background tab is drawn once and the tab that was in front is put back; keyboard focus never moves. The Game view is repainted before every read, so a capture shows the scene as it is now, in Edit Mode too. This works in batchmode too: a Game view opened there renders the game, overlay UI included, and keeps rendering every frame. In Play Mode a newly opened view may not have rendered a frame yet: an omitted `source` then captures a camera for this call, and an explicit `source=screen` fails; either way, capturing again reads the screen.

`camera` renders a single camera off-screen instead. It works without a Game view window and lets you target a specific camera by name, but overlay canvases composite straight to the screen and never render through a camera. With a Scriptable Render Pipeline (URP or HDRP), the capture draws them on top itself with `ScriptableRenderContext.DrawUIOverlay`. `overlay` controls this: `auto` (default) draws them only for a camera that renders to the screen, never over a camera that feeds a render texture (a minimap, say); `true` draws them for any camera; `false` never. The Built-in Render Pipeline cannot draw them into a camera capture: `overlay=true` then says so in the result's `message`. When the scene has overlay UI to draw, the camera renders at the Game view's size, where that UI is laid out, and the image is then scaled to `width` × `height`, so the UI matches the game; as with `screen`, a different aspect ratio stretches the image. Without overlay UI the camera renders at `width` × `height` directly. Passing `camera=<name>` without a `source` selects `camera` for you, so a named camera is never silently ignored by a screen capture.

Parameter interactions: `include-inline-image` is only meaningful together with `output-path` (without one, the inline image is always returned). `max-resolution` only applies when an inline image is actually returned — i.e. no `output-path`, or `output-path` + `include-inline-image=true`; it has **no effect** when `output-path` is set without `include-inline-image=true`. `camera` is ignored when `source=screen`.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `width` | no | `1280` | Output width in px (default 1280; capped 4096). |
| `height` | no | `720` | Output height in px (default 720; capped 4096). |
| `camera` | no | `–` | Optional camera name (`source=camera` only); defaults to Camera.main, else the first enabled camera. |
| `output-path` | no | `–` | Optional path to write the PNG: absolute (anywhere on the machine), or relative to the project root (e.g. `Screenshots/foo.png` writes `<project>/Screenshots/foo.png`). Only a path under `Assets/` is imported as an asset and gets a `.meta`; `savedPath` in the result is the absolute file. When set, the result omits the inline base64 image unless `include-inline-image=true`. |
| `include-inline-image` | no | `false` | Also return the image inline as base64 when `output-path` is set. |
| `max-resolution` | no | `0` | Cap on the inline image's longest edge (e.g. 512), preserving aspect ratio. No effect when `output-path` is set without `include-inline-image=true`. The `output-path` file keeps the requested resolution; a downscaled inline copy is reported via `inlineWidth`/`inlineHeight`. |
| `source` | no | `screen` | `screen` captures the game view's own render target — what the Game window shows, incl. Screen Space - Overlay UI in Play Mode. `camera` renders a single camera off-screen instead, with overlay canvases drawn on top on a Scriptable Render Pipeline (see `overlay`). A blank value means the default; the default becomes `camera` when `camera` is set, or when a just-opened Game view has not rendered a frame yet, in which case `message` says so. |
| `overlay` | no | `auto` | Camera captures only. `auto` draws Screen Space - Overlay UI on top for a camera that renders to the screen, `true` for any camera, `false` never. Needs a Scriptable Render Pipeline (URP or HDRP). |

**Returns:** `CaptureResult`. Its `message` field is always present: empty when there is nothing to report, otherwise a short explanation, such as a Game view opened for the capture or a fallback from `screen` to `camera`.

### `capture scene_view`
Capture the active Scene View to a PNG. Same result contract and parameter interactions as `capture game_view`: inline base64, or path-only when `output-path` is set (opt back in with `include-inline-image=true`; `max-resolution` has no effect when `output-path` is set without it).

`source` selects what is captured:

- `camera` (default) renders the Scene view camera: the scene itself, without grid, gizmos, icons, handles or toolbars, at `width` × `height`. The camera keeps its vertical field of view, so a size whose aspect ratio differs from the Scene window frames the scene differently.
- `screen` captures the Scene window as it is drawn, with grid, gizmos, icons, handles and its toolbars, at the window's size in pixels, the same as `capture editor_window --window Scene`. `width` and `height` are ignored (the result's `message` says so when they are set), and a Scene window behind another tab is brought to the front while it is read, then the previous tab is put back. Not available in batchmode, where windows exist but their contents are not drawn.

```bash
unity command capture scene_view --output-path Screenshots/scene.png                  # the scene only
unity command capture scene_view --source screen --output-path Screenshots/scene.png  # the Scene window as drawn
```

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `source` | no | `camera` | `camera` renders the Scene view camera (the scene only, at `width` × `height`); `screen` captures the Scene window as drawn, with grid, gizmos and toolbars, at the window's size. |
| `width` | no | `1280` | Output width in px (default 1280; capped 4096). `source=camera` only. |
| `height` | no | `720` | Output height in px (default 720; capped 4096). `source=camera` only. |
| `output-path` | no | `–` | Optional path to write the PNG: absolute (anywhere on the machine), or relative to the project root (e.g. `Screenshots/foo.png` writes `<project>/Screenshots/foo.png`). Only a path under `Assets/` is imported as an asset and gets a `.meta`; `savedPath` in the result is the absolute file. When set, the result omits the inline base64 image unless `include-inline-image=true`. |
| `include-inline-image` | no | `false` | Also return the image inline as base64 when `output-path` is set. |
| `max-resolution` | no | `0` | Cap on the inline image's longest edge (e.g. 512), preserving aspect ratio. No effect when `output-path` is set without `include-inline-image=true`. The `output-path` file keeps the requested resolution; a downscaled inline copy is reported via `inlineWidth`/`inlineHeight`. |

**Returns:** `CaptureResult`

### `capture editor_window`
Capture any open Editor window (Inspector, Console, Project, Hierarchy, a custom window...) as a PNG, exactly as it is drawn, at the window's size in pixels. Same result contract as `capture game_view`: inline base64, or path-only when `output-path` is set.

`window` names the window by its title (`Inspector`) or its type name (`InspectorWindow`), ignoring case. When it matches nothing, the error lists the titles of the open windows. When several windows match, the one in front is captured.

Only the tab in front of a dock area has pixels to read, so a window behind another tab is brought to the front while it is captured, and the tab that was in front is put back afterwards. Keyboard focus does not move. In batchmode windows exist but their contents are not drawn, so the command fails there; `capture game_view` still works there, because the Game view renders the game into a texture of its own.

```bash
unity command capture editor_window --window Inspector --output-path Screenshots/inspector.png
unity command capture editor_window --window ConsoleWindow --max-resolution 512   # inline, longest edge capped
```

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `window` | yes | `–` | The window to capture: its title (e.g. Inspector) or its type name (e.g. InspectorWindow). |
| `output-path` | no | `–` | Optional path to write the PNG: absolute, or relative to the project root, with the same rules as `capture game_view`. When set, the result omits the inline base64 image unless `include-inline-image=true`. |
| `include-inline-image` | no | `false` | Also return the image inline as base64 when `output-path` is set. |
| `max-resolution` | no | `0` | Cap on the inline image's longest edge (e.g. 512), preserving aspect ratio. The `output-path` file keeps the window's resolution. |

**Returns:** `CaptureResult`, with `source` set to `window:<title>`.

### `capture editor_element`
Capture a UI Toolkit VisualElement (by selector) from an EditorWindow to a PNG; returns path + base64.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `window` | yes | `–` | EditorWindow type name (e.g. InspectorWindow) or window title to capture from. |
| `selector` | yes | `–` | Element selector: '#name', '.class', a type name (e.g. Button), descendant (space) / child ('>') chains, optional pseudo-states (:checked,:hover,:focus,:active,:enabled,:disabled,:not(...)). |
| `output-path` | no | `–` | Output PNG path (absolute, or relative to the project root). Defaults to a timestamped file under <project>/Temp/pipeline-screenshots/. |

**Returns:** `CaptureElementResponse`
**Notes:** `MainThreadRequired = true`. Unity 6000.7+ only.

### `capture runtime_element`
Capture a UI Toolkit VisualElement (by selector) from a live runtime panel (UIDocument or PanelRenderer) to a PNG; returns path + base64.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `panel` | no | `–` | Name of the target panel: matches the PanelSettings asset name or the host GameObject name (UIDocument or PanelRenderer). Optional when exactly one panel exists. |
| `selector` | yes | `–` | Element selector: '#name', '.class', a type name (e.g. Button), descendant (space) / child ('>') chains, optional pseudo-states (:checked,:hover,:focus,:active,:enabled,:disabled,:not(...)). |
| `output-path` | no | `–` | Output PNG path (absolute, or relative to Application.persistentDataPath). Defaults to a timestamped file under Application.persistentDataPath. |

**Returns:** `CaptureElementResponse`
**Notes:** `MainThreadRequired = true`, tagged `runtime`. Unity 6000.7+ only.

See [Creating commands](../creating-commands.md) and [Connectivity](../connectivity.md).

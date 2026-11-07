---
uid: index
---

# Pipeline Documentation

`com.unity.pipeline` lets a client — CLI, CI, or an agent — drive a running Unity Editor (or a
development Player) over a local HTTP API by executing registered commands.

## Guides

| Page | What it covers |
|------|----------------|
| [Creating commands](creating-commands.md) | The command-authoring API: `[CliGroupCommand]`/`[CliArg]`, groups and names, the handler + response pattern, the `MainThreadRequired` flag and the `runtime` tag. |
| [Creating authoring commands](authoring-commands.md) | Building content-authoring commands: the authoring root sandbox, `ObjectRef` in / `AuthoringResult` out, and undo grouping — with a worked example for a new content type. |
| [Safety & mutations](safety-and-mutations.md) | Conventions shared by state-changing commands: the `confirm`/`dry-run` gate, Undo grouping via `AuthoringUndoScope`, and the path sandbox. |
| [Connectivity](connectivity.md) | How servers bind the loopback address, the port ranges, the port/descriptor file, and the bearer-token auth workflow. |
| [Runtime connection & setup](runtime-setup.md) | Running the server in a Player via Project Settings → Pipeline → Runtime, and how the build gating works. |
| [Reloading code](method-reload.md) | Updating `[MethodReload]` methods in a running game: the `Assembly.Load` and interpreter backends, supported changes, the watcher, and how the woven dispatch works. |
| [Tests architecture](testing.md) | Writing command tests via `PipelineClient` (over HTTP) and via direct command calls. |
| [Analytics](analytics.md) | The three usage events the Editor server reports, what each field means, and what is deliberately not collected. |
| [Samples](samples.md) | Where the samples live (`com.unity.pipeline.samples`), how to add the package and import a sample, and what each one demonstrates. |

## Command index

Every available command, grouped by area. Each entry is the command as typed on the command line, `unity command <group> <name>`, and links to its full reference (parameters + return type). In a JSON request, a batch operation or an MCP-style call, the command is named the same way, `<group> <name>`.

### Asset & file commands

| Command | Description |
|---------|-------------|
| [`asset create`](commands/assets-and-files.md#asset-create) | Create a ScriptableObject / Object asset at a path. |
| [`asset import`](commands/assets-and-files.md#asset-import) | Import an external file into the project. |
| [`asset move`](commands/assets-and-files.md#asset-move) | Move / rename an asset (keeps its GUID). |
| [`asset copy`](commands/assets-and-files.md#asset-copy) | Copy an asset (fresh GUID). |
| [`asset rename`](commands/assets-and-files.md#asset-rename) | Rename an asset in place. |
| [`asset delete`](commands/assets-and-files.md#asset-delete) | Delete an asset (needs confirm). |
| [`asset find`](commands/assets-and-files.md#asset-find) | Find assets by type / name / label. |
| [`asset set_import_settings`](commands/assets-and-files.md#asset-set_import_settings) | Set an asset's importer settings and re-import. |
| [`asset get_import_settings`](commands/assets-and-files.md#asset-get_import_settings) | Read an asset's importer settings. |
| [`asset create_folder`](commands/assets-and-files.md#asset-create_folder) | Create a folder under the authoring root. |
| [`asset read_text`](commands/assets-and-files.md#asset-read_text) | Read a UTF-8 text file. |
| [`asset write_text`](commands/assets-and-files.md#asset-write_text) | Write a UTF-8 text file, then import it. |

### Scene commands

| Command | Description |
|---------|-------------|
| [`scene create`](commands/scenes.md#scene-create) | Create and save a new scene. |
| [`scene open`](commands/scenes.md#scene-open) | Open an existing scene. |
| [`scene save`](commands/scenes.md#scene-save) | Save an open scene. |
| [`editor save_all`](commands/scenes.md#editor-save_all) | Save all dirty open scenes. |
| [`scene list_open`](commands/scenes.md#scene-list_open) | List open scenes and their state. |
| [`scene set_active`](commands/scenes.md#scene-set_active) | Set which open scene is active. |
| [`scene get_hierarchy`](commands/scenes.md#scene-get_hierarchy) | Return a scene's GameObject tree. |
| [`scene add_to_build`](commands/scenes.md#scene-add_to_build) | Add a scene to Build Settings. |
| [`scene remove_from_build`](commands/scenes.md#scene-remove_from_build) | Remove a scene from Build Settings. |

### GameObject & component commands

| Command | Description |
|---------|-------------|
| [`gameobject create`](commands/gameobjects-and-components.md#gameobject-create) | Create a GameObject or primitive. |
| [`gameobject create_many`](commands/gameobjects-and-components.md#gameobject-create_many) | Batch-create GameObjects / primitives. |
| [`gameobject find`](commands/gameobjects-and-components.md#gameobject-find) | Find GameObjects by name / tag / component / path. |
| [`gameobject set_transform`](commands/gameobjects-and-components.md#gameobject-set_transform) | Set local position / rotation / scale. |
| [`gameobject set_parent`](commands/gameobjects-and-components.md#gameobject-set_parent) | Reparent a GameObject (or detach to root). |
| [`gameobject set_active`](commands/gameobjects-and-components.md#gameobject-set_active) | Set a GameObject's active state. |
| [`gameobject set_tag`](commands/gameobjects-and-components.md#gameobject-set_tag) | Set a GameObject's tag. |
| [`gameobject set_layer`](commands/gameobjects-and-components.md#gameobject-set_layer) | Set a GameObject's layer. |
| [`gameobject rename`](commands/gameobjects-and-components.md#gameobject-rename) | Rename a GameObject. |
| [`gameobject delete`](commands/gameobjects-and-components.md#gameobject-delete) | Delete a GameObject (undoable). |
| [`component add`](commands/gameobjects-and-components.md#component-add) | Add a component by type name. |
| [`component remove`](commands/gameobjects-and-components.md#component-remove) | Remove a component. |
| [`component get_properties`](commands/gameobjects-and-components.md#component-get_properties) | Read a component's serialized properties. |
| [`component set_properties`](commands/gameobjects-and-components.md#component-set_properties) | Set a component's serialized properties. |

### Prefab commands

| Command | Description |
|---------|-------------|
| [`prefab create`](commands/prefabs.md#prefab-create) | Save a GameObject as a prefab. |
| [`prefab instantiate`](commands/prefabs.md#prefab-instantiate) | Instantiate a prefab into a scene. |
| [`prefab create_variant`](commands/prefabs.md#prefab-create_variant) | Create a prefab variant. |
| [`prefab apply_overrides`](commands/prefabs.md#prefab-apply_overrides) | Apply instance overrides to the source. |
| [`prefab revert_overrides`](commands/prefabs.md#prefab-revert_overrides) | Revert instance overrides. |
| [`prefab unpack`](commands/prefabs.md#prefab-unpack) | Unpack a prefab instance. |
| [`prefab save_contents`](commands/prefabs.md#prefab-save_contents) | Edit a prefab in an isolated stage and save. |

### Script commands

| Command | Description |
|---------|-------------|
| [`script create`](commands/scripts.md#script-create) | Create a C# script from a template. |
| [`script attach`](commands/scripts.md#script-attach) | Attach a MonoBehaviour by type / asset. |
| [`component set_field`](commands/scripts.md#component-set_field) | Set a serialized field on a component / asset. |
| [`component get_fields`](commands/scripts.md#component-get_fields) | Read serialized fields. |
| [`script eval`](commands/scripts.md#script-eval) | Compile and run a C# script, inline or from a file, with `await` support. Editor and Player. |
| [`script eval_file`](commands/scripts.md#script-eval_file) | Deprecated: use `script eval --file`. |
| [`script run`](commands/scripts.md#script-run) | Compile a project `.cs` file in memory (no domain reload) and run a named static entry point — the builder-pattern path for bulk construction. |

### Animation commands

| Command | Description |
|---------|-------------|
| [`animation.clip create`](commands/animation.md#animationclip-create) | Create an empty .anim AnimationClip. |
| [`animation.clip set_curve`](commands/animation.md#animationclip-set_curve) | Add / replace a float curve on a clip. |
| [`animation.clip get`](commands/animation.md#animationclip-get) | Read a clip's curves and metadata. |
| [`animation.clip remove_curve`](commands/animation.md#animationclip-remove_curve) | Remove a curve binding (needs confirm). |
| [`animation.animator create_controller`](commands/animation.md#animationanimator-create_controller) | Create an .controller AnimatorController. |
| [`animation.animator add_parameter`](commands/animation.md#animationanimator-add_parameter) | Add a parameter to a controller. |
| [`animation.animator add_layer`](commands/animation.md#animationanimator-add_layer) | Add a layer to a controller. |
| [`animation.animator add_state`](commands/animation.md#animationanimator-add_state) | Add a state to a layer. |
| [`animation.animator add_transition`](commands/animation.md#animationanimator-add_transition) | Add a transition between states. |
| [`animation.animator get_controller`](commands/animation.md#animationanimator-get_controller) | Read a controller's full structure. |
| [`animation.timeline create`](commands/animation.md#animationtimeline-create) | Create a .playable Timeline asset. |
| [`animation.timeline add_track`](commands/animation.md#animationtimeline-add_track) | Add a track to a Timeline. |
| [`animation.timeline add_clip`](commands/animation.md#animationtimeline-add_clip) | Add a clip to a Timeline track. |
| [`animation.timeline get`](commands/animation.md#animationtimeline-get) | Read a Timeline's structure. |

### Material & shader commands

| Command | Description |
|---------|-------------|
| [`material get_properties`](commands/materials.md#material-get_properties) | Read a material's shader and properties. |
| [`material set_properties`](commands/materials.md#material-set_properties) | Set material shader properties / keywords. |
| [`material list_shaders`](commands/materials.md#material-list_shaders) | Discover available shaders. |
| [`material get_shader_properties`](commands/materials.md#material-get_shader_properties) | Introspect a shader's property list. |

### Baking commands

| Command | Description |
|---------|-------------|
| [`lighting bake`](commands/baking.md#lighting-bake) | Async lightmap bake (poll `lighting bake_status`). |
| [`lighting bake_status`](commands/baking.md#lighting-bake_status) | Status of the last lighting bake. |
| [`lighting cancel_bake`](commands/baking.md#lighting-cancel_bake) | Cancel an in-progress lighting bake. |
| [`lighting clear_bake`](commands/baking.md#lighting-clear_bake) | Clear baked lightmaps (needs confirm). |
| [`lighting get_settings`](commands/baking.md#lighting-get_settings) | Read the active LightingSettings. |
| [`lighting set_settings`](commands/baking.md#lighting-set_settings) | Apply a subset of lighting settings. |
| [`navmesh bake`](commands/baking.md#navmesh-bake) | Async legacy NavMesh bake (poll `navmesh bake_status`). |
| [`navmesh bake_status`](commands/baking.md#navmesh-bake_status) | Status of the last NavMesh bake. |
| [`navmesh cancel_bake`](commands/baking.md#navmesh-cancel_bake) | Cancel an in-progress NavMesh bake. |
| [`navmesh clear_bake`](commands/baking.md#navmesh-clear_bake) | Clear the baked NavMesh (needs confirm). |
| [`navmesh get_settings`](commands/baking.md#navmesh-get_settings) | Read legacy NavMesh bake settings. |
| [`navmesh set_settings`](commands/baking.md#navmesh-set_settings) | Apply a subset of NavMesh settings. |
| [`navmesh bake_surfaces`](commands/baking.md#navmesh-bake_surfaces) | Bake NavMeshSurface components (AI Navigation). |
| [`occlusion bake`](commands/baking.md#occlusion-bake) | Async occlusion bake (poll `occlusion bake_status`). |
| [`occlusion bake_status`](commands/baking.md#occlusion-bake_status) | Status of the last occlusion bake. |
| [`occlusion cancel_bake`](commands/baking.md#occlusion-cancel_bake) | Cancel an in-progress occlusion bake. |
| [`occlusion clear_bake`](commands/baking.md#occlusion-clear_bake) | Clear baked occlusion data (needs confirm). |

### Navigation & selection commands

| Command | Description |
|---------|-------------|
| [`gameobject get_selection`](commands/navigation.md#gameobject-get_selection) | Read the current Editor selection. |
| [`gameobject set_selection`](commands/navigation.md#gameobject-set_selection) | Set the Editor selection. |
| [`editor search`](commands/navigation.md#editor-search) | Run a Unity Search query. |

### Capture commands

| Command | Description |
|---------|-------------|
| [`capture game_view`](commands/capture.md#capture-game_view) | Capture the game view to a PNG (base64). |
| [`capture scene_view`](commands/capture.md#capture-scene_view) | Render the Scene View to a PNG (base64). |
| [`capture editor_element`](commands/capture.md#capture-editor_element) | Capture a UI Toolkit element from an EditorWindow (6000.7+). |
| [`capture runtime_element`](commands/capture.md#capture-runtime_element) | Capture a UI Toolkit element from a runtime panel (6000.7+). |

### Build, compilation & test commands

| Command | Description |
|---------|-------------|
| [`build run`](commands/build-and-compilation.md#build-run) | Async Player build (poll `build status`). |
| [`build status`](commands/build-and-compilation.md#build-status) | Status / report of the current build. |
| [`build.target switch`](commands/build-and-compilation.md#buildtarget-switch) | Switch the active build target (needs confirm). |
| [`build.target switch_status`](commands/build-and-compilation.md#buildtarget-switch_status) | Status of the last target switch. |
| [`build.target list`](commands/build-and-compilation.md#buildtarget-list) | List known build targets. |
| [`build get_settings`](commands/build-and-compilation.md#build-get_settings) | Read build configuration. |
| [`build set_settings`](commands/build-and-compilation.md#build-set_settings) | Set build configuration fields. |
| [`build list_profiles`](commands/build-and-compilation.md#build-list_profiles) | List Build Profile assets (Unity 6). |
| [`build list_platforms`](commands/build-and-compilation.md#build-list_platforms) | List platforms a Build Profile can target (Unity 6). |
| [`build create_profile`](commands/build-and-compilation.md#build-create_profile) | Create a Build Profile asset for a platform (Unity 6). |
| [`build set_profile`](commands/build-and-compilation.md#build-set_profile) | Edit a Build Profile's scene list and scripting defines (Unity 6). |
| [`script recompile`](commands/build-and-compilation.md#script-recompile) | Force a script recompile (poll `script recompile_status`). |
| [`script recompile_status`](commands/build-and-compilation.md#script-recompile_status) | Status of the last recompile. |
| [`test list`](commands/build-and-compilation.md#test-list) | List available tests without running. |
| [`test run`](commands/build-and-compilation.md#test-run) | Run Unity tests with filters. |
| [`test status`](commands/build-and-compilation.md#test-status) | Status of async test execution. |
| [`test cancel`](commands/build-and-compilation.md#test-cancel) | Cancel running tests. |

### Project settings commands

| Command | Description |
|---------|-------------|
| [`settings.audio get`](commands/project-settings.md#settingsaudio-get) | Read Audio settings. |
| [`settings.audio set`](commands/project-settings.md#settingsaudio-set) | Change Audio settings (needs confirm). |
| [`settings.graphics get`](commands/project-settings.md#settingsgraphics-get) | Read Graphics settings. |
| [`settings.graphics set`](commands/project-settings.md#settingsgraphics-set) | Set the default render pipeline (needs confirm). |
| [`settings.input get`](commands/project-settings.md#settingsinput-get) | Read legacy Input Manager axes. |
| [`settings.input set`](commands/project-settings.md#settingsinput-set) | Tune a legacy input axis (needs confirm). |
| [`settings.multiplayer_roles get`](commands/project-settings.md#settingsmultiplayer_roles-get) | Read Multiplayer Roles settings (6000.6+). |
| [`settings.multiplayer_roles set`](commands/project-settings.md#settingsmultiplayer_roles-set) | Enable roles, safety checks and server stripping (needs confirm; 6000.6+). |
| [`settings.physics get`](commands/project-settings.md#settingsphysics-get) | Read Physics settings. |
| [`settings.physics set`](commands/project-settings.md#settingsphysics-set) | Change Physics settings (needs confirm). |
| [`settings.player get`](commands/project-settings.md#settingsplayer-get) | Read PlayerSettings. |
| [`settings.player set`](commands/project-settings.md#settingsplayer-set) | Change PlayerSettings (needs confirm). |
| [`settings.quality get`](commands/project-settings.md#settingsquality-get) | Read QualitySettings. |
| [`settings.quality set`](commands/project-settings.md#settingsquality-set) | Change QualitySettings (needs confirm). |
| [`settings.runtime_pipeline get`](commands/project-settings.md#settingsruntime_pipeline-get) | Read Pipeline Runtime settings (enableInDevelopmentBuilds, enableInReleaseBuilds, port, enableAuditLogging, autoStart, maxWorkItemsPerFrame). |
| [`settings.runtime_pipeline set`](commands/project-settings.md#settingsruntime_pipeline-set) | Change Pipeline Runtime settings. |
| [`settings.tags_layers get`](commands/project-settings.md#settingstags_layers-get) | Read tags and layers. |
| [`settings.tags_layers set`](commands/project-settings.md#settingstags_layers-set) | Add / remove tags, name layers (needs confirm). |
| [`settings.time get`](commands/project-settings.md#settingstime-get) | Read Time settings. |
| [`settings.time set`](commands/project-settings.md#settingstime-set) | Change Time settings (needs confirm). |

### Package Manager commands

| Command | Description |
|---------|-------------|
| [`package list`](commands/package-manager.md#package-list) | List packages by scope. |
| [`package search`](commands/package-manager.md#package-search) | Search the registry. |
| [`package add`](commands/package-manager.md#package-add) | Add a UPM package (needs confirm). |
| [`package remove`](commands/package-manager.md#package-remove) | Remove a UPM package (needs confirm). |
| [`package resolve`](commands/package-manager.md#package-resolve) | Resolve / refresh packages. |
| [`package status`](commands/package-manager.md#package-status) | Status of the last package operation. |

### Editor lifecycle & observability commands

| Command | Description |
|---------|-------------|
| [`editor.playmode start`](commands/editor-lifecycle-and-observability.md#editorplaymode-start) | Enter play mode. |
| [`editor.playmode set_run_in_background`](commands/editor-lifecycle-and-observability.md#editorplaymode-set_run_in_background) | Override the project's Run In Background Player Setting. |
| [`editor.playmode stop`](commands/editor-lifecycle-and-observability.md#editorplaymode-stop) | Exit play mode. |
| [`editor.playmode pause`](commands/editor-lifecycle-and-observability.md#editorplaymode-pause) | Toggle play mode pause. |
| [`editor status`](commands/editor-lifecycle-and-observability.md#editor-status) | Detailed Editor status. |
| [`editor focus`](commands/editor-lifecycle-and-observability.md#editor-focus) | Bring the Editor to the foreground. |
| [`editor invoke_menu`](commands/editor-lifecycle-and-observability.md#editor-invoke_menu) | Execute (or list) Editor menu items. |
| [`editor quit`](commands/editor-lifecycle-and-observability.md#editor-quit) | Quit the Editor, saving or discarding unsaved changes. |
| [`capture screenshot`](commands/editor-lifecycle-and-observability.md#capture-screenshot) | Capture Scene / Game view to a PNG file. |
| [`editor set_auto_tick`](commands/editor-lifecycle-and-observability.md#editor-set_auto_tick) | Keep the editor ticking while unfocused. |
| [`diagnostics get_performance`](commands/editor-lifecycle-and-observability.md#diagnostics-get_performance) | Read render / memory / frame stats. |
| [`diagnostics run_audit`](commands/editor-lifecycle-and-observability.md#diagnostics-run_audit) | Run a Project Auditor scan, producing a CSV of issues. |
| [`diagnostics audit_status`](commands/editor-lifecycle-and-observability.md#diagnostics-audit_status) | Poll the audit scan; returns the CSV path + issue count. |
| [`diagnostics report_evals`](commands/editor-lifecycle-and-observability.md#diagnostics-report_evals) | Ranked report of local eval-usage telemetry + command-coverage suggestions. |
| [`authoring get_root`](commands/editor-lifecycle-and-observability.md#authoring-get_root) | Get the authoring-root folder. |
| [`authoring set_root`](commands/editor-lifecycle-and-observability.md#authoring-set_root) | Set the authoring-root folder. |

### Multiplayer Play Mode & instance commands

| Command | Description |
|---------|-------------|
| [`editor.playmode list_scenarios`](commands/multiplayer-play-mode.md#editorplaymode-list_scenarios) | List Play Mode Scenario assets. |
| [`editor.playmode list_players`](commands/multiplayer-play-mode.md#editorplaymode-list_players) | List the players a scenario declares. |
| [`editor.playmode set_active_scenario`](commands/multiplayer-play-mode.md#editorplaymode-set_active_scenario) | Set the active scenario (Idle only). |
| [`editor.playmode start_scenario`](commands/multiplayer-play-mode.md#editorplaymode-start_scenario) | Start the active scenario. |
| [`editor.playmode stop_scenario`](commands/multiplayer-play-mode.md#editorplaymode-stop_scenario) | Stop the running scenario. |
| [`editor.playmode get_scenario_status`](commands/multiplayer-play-mode.md#editorplaymode-get_scenario_status) | Live scenario state + each player's run state and address. |
| [`editor.playmode create_scenario`](commands/multiplayer-play-mode.md#editorplaymode-create_scenario) | Create a scenario asset. |
| [`editor.playmode add_player`](commands/multiplayer-play-mode.md#editorplaymode-add_player) | Add a clone / local player to a scenario. |
| [`editor.playmode remove_player`](commands/multiplayer-play-mode.md#editorplaymode-remove_player) | Remove a scenario player by id. |
| [`editor.playmode list_instances`](commands/multiplayer-play-mode.md#editorplaymode-list_instances) | List running Editor instances (this Editor + its clones). |

### Multiplayer Roles commands

| Command | Description |
|---------|-------------|
| [`multiplayer.role get_active`](commands/multiplayer-roles.md#multiplayerrole-get_active) | Get the Editor's role and whether roles are enabled. |
| [`multiplayer.role set_active`](commands/multiplayer-roles.md#multiplayerrole-set_active) | Set the role the Editor runs as in Play Mode. |
| [`multiplayer.role get_game_object`](commands/multiplayer-roles.md#multiplayerrole-get_game_object) | Get the roles a GameObject is kept for. |
| [`multiplayer.role set_game_object`](commands/multiplayer-roles.md#multiplayerrole-set_game_object) | Set the roles a GameObject is kept for. |
| [`multiplayer.role get_component`](commands/multiplayer-roles.md#multiplayerrole-get_component) | Get the roles a component is kept for. |
| [`multiplayer.role set_component`](commands/multiplayer-roles.md#multiplayerrole-set_component) | Set the roles a component is kept for. |
| [`multiplayer.role get_build`](commands/multiplayer-roles.md#multiplayerrole-get_build) | Get the role a build profile builds for. |
| [`multiplayer.role set_build`](commands/multiplayer-roles.md#multiplayerrole-set_build) | Set the role a build profile builds for. |
| [`multiplayer.role list_component_types`](commands/multiplayer-roles.md#multiplayerrole-list_component_types) | List component types kept for specific roles project-wide. |
| [`multiplayer.role set_component_type`](commands/multiplayer-roles.md#multiplayerrole-set_component_type) | Keep a component type for specific roles project-wide. |

### Runtime commands

| Command | Description |
|---------|-------------|
| [`runtime status`](commands/runtime.md#runtime-status) | Runtime application status. |
| [`runtime quit`](commands/runtime.md#runtime-quit) | Quit the application. |
| [`runtime set_target_frame_rate`](commands/runtime.md#runtime-set_target_frame_rate) | Set the target frame rate. |
| [`runtime set_time_scale`](commands/runtime.md#runtime-set_time_scale) | Set the time scale. |
| [`runtime simulate_key`](commands/runtime.md#runtime-simulate_key) | Simulate a keyboard event (Input System). |
| [`runtime simulate_pointer`](commands/runtime.md#runtime-simulate_pointer) | Simulate a pointer event (Input System). |
| [`console write`](commands/runtime.md#console-write) | Write a message to the Unity console. |
| [`console get`](commands/runtime.md#console-get) | Read captured console output. |
| [`console status`](commands/runtime.md#console-status) | Console counters and compile-failure flag, without the entries. |
| [`console clear`](commands/runtime.md#console-clear) | Clear the console / log buffer. |
| [`script.methodreload reload`](commands/runtime.md#scriptmethodreload-reload) | Apply [MethodReload] edits from a file. |
| [`script.methodreload reload_editor`](commands/runtime.md#scriptmethodreload-reload_editor) | Same, on the IlInterpreter backend (IL2CPP-safe). |
| [`script.methodreload reload_player`](commands/runtime.md#scriptmethodreload-reload_player) | Compile in the Editor and push [MethodReload] edits to connected players (IL2CPP-safe). |
| [`script.methodreload status`](commands/runtime.md#scriptmethodreload-status) | Method reload registry status. |
| [`script.methodreload generate_link_xml`](commands/runtime.md#scriptmethodreload-generate_link_xml) | Generate the method-reload `link.xml` for IL2CPP device builds. |
| [`script.methodreload cleanup`](commands/runtime.md#scriptmethodreload-cleanup) | Clear old method reload DLLs / registry. |

### Pipeline commands

| Command | Description |
|---------|-------------|
| [`pipeline progress`](commands/pipeline.md#pipeline-progress) | Progress of the running command. |
| [`pipeline list_commands`](commands/pipeline.md#pipeline-list_commands) | List, filter and group the registered commands. |

### Batch command

| Command | Description |
|---------|-------------|
| [`job batch`](commands/batch.md#job-batch) | Run multiple registered commands in one transactional request. |

## Additional resources

- [Command reference](command-reference.md)
- [Install a package by name](https://docs.unity3d.com/Manual/upm-ui-quick.html)
- [Import the Pipeline samples](samples.md)
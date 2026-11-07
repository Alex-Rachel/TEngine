---
uid: command-reference
---

# Command reference

Find the commands you can use to control the Unity Editor or a development Player from the command line, a CI job, or an agent.

Unity CLI organizes commands in groups. To run a command, use `unity command <group> <name>`. To list the commands in a group, use `unity command <group>`. JSON requests and batch operations follow the same `<group> <name>` format.

| **Topic** | **Description** |
|------|----------|
| **[Pipeline commands](commands/pipeline.md)** | Explore commands to track pipeline progress and view the available command catalog. |
| **[Batch command](commands/batch.md)** | Explore how to run several commands in one transactional request, with cross-operation result references, atomic Undo, and dry-run validation. |
| **[Asset & file commands](commands/assets-and-files.md)** | Explore commands to create, import, move, copy, rename, delete, and find project assets. |
| **[Scene commands](commands/scenes.md)** | Explore commands to create, open, save, or inspect scenes, and manage the Build Settings scene list. |
| **[GameObject & component commands](commands/gameobjects-and-components.md)** | Explore commands to create, find, modify, and transform GameObjects and their components. |
| **[Prefab commands](commands/prefabs.md)** | Explore commands to create and instantiate prefabs, derive variants, apply or revert overrides, unpack instances, and edit prefab contents. |
| **[Script commands](commands/scripts.md)** | Explore commands to create and attach C# scripts, read and write serialized fields, and compile and run code in memory. |
| **[Animation commands](commands/animation.md)** | Explore commands to author animation assets, such as animation clips, Animator Controllers, and Timeline assets. |
| **[Material & shader commands](commands/materials.md)** | Explore commands to discover, read, and edit material shader properties. |
| **[Baking commands](commands/baking.md)** | Explore commands to configure, bake, and clear lighting, NavMesh, and occlusion data. |
| **[Navigation & selection commands](commands/navigation.md)** | Explore commands to read or modify the current Editor selection and perform Unity Search queries. |
| **[Capture commands](commands/capture.md)** | Explore commands to capture the **Game** view, **Scene** view, or a UI Toolkit element as a PNG image. |
| **[Wait commands](commands/wait.md)** | Explore commands that wait on the server for a condition, instead of polling. |
| **[Build, compilation & test commands](commands/build-and-compilation.md)** | Explore commands to build the Player, switch the build target, edit build settings or profiles, recompile scripts, and run tests. |
| **[Project settings commands](commands/project-settings.md)** | Explore commands to manage project-wide settings, such as Audio, Graphics, Physics, Player, Quality, Tags and Layers, and Time settings. |
| **[Package Manager commands](commands/package-manager.md)** | Explore commands to list, search for, add, remove, and resolve packages, and to check the status of Package Manager operations. |
| **[Editor lifecycle & observability commands](commands/editor-lifecycle-and-observability.md)** | Explore commands to control Play mode, Editor focus, and menus, and to observe the Editor's state, console, and performance. |
| **[Multiplayer Play Mode & instance commands](commands/multiplayer-play-mode.md)** | Explore commands to run Play Mode Scenarios with the main Editor and virtual players, author scenario assets, and find clone instances. |
| **[Runtime commands](commands/runtime.md)** | Explore commands that run in a Player, such as commands to get status, quit, set the frame rate and time scale, read logs, evaluate code, and reload code. |

## Additional resources

- [Creating commands](creating-commands.md)
- [Safety & mutations](safety-and-mutations.md)
- [Connectivity](connectivity.md)
- [Runtime connection & setup](runtime-setup.md)

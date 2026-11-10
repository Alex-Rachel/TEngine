namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// Full names of the commands that code refers to, in the form a user types and reads: the CLI
    /// <c>group name</c> (<c>build create_profile</c>). Use them for dispatch and comparisons, and in
    /// every text shown to a user: descriptions, hints, messages, logs. Referring through these
    /// constants keeps that code correct when a command is renamed. Add a constant when code needs
    /// one; a command does not need a constant to exist.
    /// </summary>
    internal static class CommandNames
    {
        // animation.animator
        internal const string AnimationAnimatorAddLayer = "animation.animator add_layer";
        internal const string AnimationAnimatorAddParameter = "animation.animator add_parameter";
        internal const string AnimationAnimatorAddState = "animation.animator add_state";
        internal const string AnimationAnimatorAddTransition = "animation.animator add_transition";
        internal const string AnimationAnimatorCreateController = "animation.animator create_controller";

        // animation.clip
        internal const string AnimationClipCreate = "animation.clip create";
        internal const string AnimationClipRemoveCurve = "animation.clip remove_curve";
        internal const string AnimationClipSetCurve = "animation.clip set_curve";

        // animation.timeline
        internal const string AnimationTimelineAddClip = "animation.timeline add_clip";
        internal const string AnimationTimelineAddTrack = "animation.timeline add_track";
        internal const string AnimationTimelineCreate = "animation.timeline create";

        // asset
        internal const string AssetCopy = "asset copy";
        internal const string AssetCreate = "asset create";
        internal const string AssetCreateFolder = "asset create_folder";
        internal const string AssetDelete = "asset delete";
        internal const string AssetFind = "asset find";
        internal const string AssetGetImportSettings = "asset get_import_settings";
        internal const string AssetImport = "asset import";
        internal const string AssetMove = "asset move";
        internal const string AssetReadText = "asset read_text";
        internal const string AssetRename = "asset rename";
        internal const string AssetSetImportSettings = "asset set_import_settings";
        internal const string AssetWriteText = "asset write_text";

        // authoring
        internal const string AuthoringSetRoot = "authoring set_root";

        // build
        internal const string BuildCreateProfile = "build create_profile";
        internal const string BuildGetSettings = "build get_settings";
        internal const string BuildListPlatforms = "build list_platforms";
        internal const string BuildListProfiles = "build list_profiles";
        internal const string BuildRun = "build run";
        internal const string BuildSetProfile = "build set_profile";
        internal const string BuildSetSettings = "build set_settings";
        internal const string BuildStatus = "build status";

        // build.target
        internal const string BuildTargetList = "build.target list";
        internal const string BuildTargetSwitch = "build.target switch";
        internal const string BuildTargetSwitchStatus = "build.target switch_status";

        // capture
        internal const string CaptureEditorWindow = "capture editor_window";
        internal const string CaptureGameView = "capture game_view";
        internal const string CaptureSceneView = "capture scene_view";
        internal const string CaptureScreenshot = "capture screenshot";
#if UNITY_6000_7_OR_NEWER
        // Declared only on 6000.7+, like the commands themselves.
        internal const string CaptureEditorElement = "capture editor_element";
        internal const string CaptureRuntimeElement = "capture runtime_element";
#endif

        // component
        internal const string ComponentAdd = "component add";
        internal const string ComponentGetFields = "component get_fields";
        internal const string ComponentSetField = "component set_field";
        internal const string ComponentSetProperties = "component set_properties";

        // console
        internal const string ConsoleClear = "console clear";
        internal const string ConsoleGet = "console get";
        internal const string ConsoleStatus = "console status";
        internal const string ConsoleWrite = "console write";

        // diagnostics
        internal const string DiagnosticsAuditStatus = "diagnostics audit_status";
        internal const string DiagnosticsGetPerformance = "diagnostics get_performance";
        internal const string DiagnosticsMemorySnapshot = "diagnostics memory_snapshot";
        internal const string DiagnosticsMemorySnapshotStatus = "diagnostics memory_snapshot_status";
        internal const string DiagnosticsProfilerConnect = "diagnostics profiler_connect";
        internal const string DiagnosticsProfilerRecord = "diagnostics profiler_record";
        internal const string DiagnosticsProfilerRecordStatus = "diagnostics profiler_record_status";
        internal const string DiagnosticsProfilerRecordStop = "diagnostics profiler_record_stop";
        internal const string DiagnosticsProfilerTargets = "diagnostics profiler_targets";
        internal const string DiagnosticsRecentAnalytics = "diagnostics recent_analytics";
        internal const string DiagnosticsRecordAnalytics = "diagnostics record_analytics";
        internal const string DiagnosticsReportEvals = "diagnostics report_evals";
        internal const string DiagnosticsRunAudit = "diagnostics run_audit";

        // editor
        internal const string EditorFocus = "editor focus";
        internal const string EditorInvokeMenu = "editor invoke_menu";
        internal const string EditorQuit = "editor quit";
        internal const string EditorSaveAll = "editor save_all";
        internal const string EditorSearch = "editor search";
        internal const string EditorSetAutoTick = "editor set_auto_tick";
        internal const string EditorStatus = "editor status";

        // editor.playmode
        internal const string EditorPlaymodePause = "editor.playmode pause";
        internal const string EditorPlaymodeSetRunInBackground = "editor.playmode set_run_in_background";
        internal const string EditorPlaymodeStart = "editor.playmode start";
        internal const string EditorPlaymodeStop = "editor.playmode stop";

        // gameobject
        internal const string GameobjectCreate = "gameobject create";
        internal const string GameobjectCreateMany = "gameobject create_many";
        internal const string GameobjectFind = "gameobject find";
        internal const string GameobjectGetSelection = "gameobject get_selection";
        internal const string GameobjectRename = "gameobject rename";
        internal const string GameobjectSetActive = "gameobject set_active";
        internal const string GameobjectSetParent = "gameobject set_parent";
        internal const string GameobjectSetTransform = "gameobject set_transform";

        // job
        internal const string JobBatch = "job batch";
        internal const string JobWait = "job wait";
        internal const string JobWaitCancel = "job wait_cancel";
        internal const string JobWaitStatus = "job wait_status";

        // lighting
        internal const string LightingBake = "lighting bake";
        internal const string LightingBakeStatus = "lighting bake_status";
        internal const string LightingCancelBake = "lighting cancel_bake";
        internal const string LightingClearBake = "lighting clear_bake";
        internal const string LightingGetSettings = "lighting get_settings";
        internal const string LightingSetSettings = "lighting set_settings";

        // material
        internal const string MaterialListShaders = "material list_shaders";
        internal const string MaterialSetProperties = "material set_properties";

        // navmesh
        internal const string NavmeshBake = "navmesh bake";
        internal const string NavmeshBakeStatus = "navmesh bake_status";
        internal const string NavmeshBakeSurfaces = "navmesh bake_surfaces";
        internal const string NavmeshCancelBake = "navmesh cancel_bake";
        internal const string NavmeshClearBake = "navmesh clear_bake";
        internal const string NavmeshGetSettings = "navmesh get_settings";
        internal const string NavmeshSetSettings = "navmesh set_settings";

        // occlusion
        internal const string OcclusionBake = "occlusion bake";
        internal const string OcclusionBakeStatus = "occlusion bake_status";
        internal const string OcclusionCancelBake = "occlusion cancel_bake";
        internal const string OcclusionClearBake = "occlusion clear_bake";

        // package
        internal const string PackageAdd = "package add";
        internal const string PackageList = "package list";
        internal const string PackageRemove = "package remove";
        internal const string PackageResolve = "package resolve";
        internal const string PackageSearch = "package search";
        internal const string PackageStatus = "package status";

        // pipeline
        internal const string PipelineListCommands = "pipeline list_commands";

        // prefab
        internal const string PrefabApplyOverrides = "prefab apply_overrides";
        internal const string PrefabCreate = "prefab create";
        internal const string PrefabCreateVariant = "prefab create_variant";
        internal const string PrefabInstantiate = "prefab instantiate";
        internal const string PrefabSaveContents = "prefab save_contents";

        // runtime
        internal const string RuntimeQuit = "runtime quit";
        internal const string RuntimeSetTargetFrameRate = "runtime set_target_frame_rate";
        internal const string RuntimeSetTimeScale = "runtime set_time_scale";
        internal const string RuntimeSimulateKey = "runtime simulate_key";
        internal const string RuntimeSimulatePointer = "runtime simulate_pointer";
        internal const string RuntimeStatus = "runtime status";

        // scene
        internal const string SceneAddToBuild = "scene add_to_build";
        internal const string SceneCreate = "scene create";
        internal const string SceneGetHierarchy = "scene get_hierarchy";
        internal const string SceneOpen = "scene open";
        internal const string SceneRemoveFromBuild = "scene remove_from_build";
        internal const string SceneSave = "scene save";
        internal const string SceneSetActive = "scene set_active";

        // script
        internal const string ScriptAttach = "script attach";
        internal const string ScriptCreate = "script create";
        internal const string ScriptEval = "script eval";
        internal const string ScriptEvalFile = "script eval_file";
        internal const string ScriptRecompile = "script recompile";
        internal const string ScriptRecompileStatus = "script recompile_status";
        internal const string ScriptRun = "script run";

        // script.methodreload
        internal const string ScriptMethodreloadCleanup = "script.methodreload cleanup";
        internal const string ScriptMethodreloadReload = "script.methodreload reload";
        internal const string ScriptMethodreloadReloadEditor = "script.methodreload reload_editor";
        internal const string ScriptMethodreloadReloadPlayer = "script.methodreload reload_player";
        internal const string ScriptMethodreloadStatus = "script.methodreload status";

        // settings.audio
        internal const string SettingsAudioSet = "settings.audio set";

        // settings.graphics
        internal const string SettingsGraphicsSet = "settings.graphics set";

        // settings.input
        internal const string SettingsInputSet = "settings.input set";

        // settings.physics
        internal const string SettingsPhysicsSet = "settings.physics set";

#if UNITY_6000_6_OR_NEWER
        // settings.multiplayer_roles: declared only on 6000.6+, like the commands themselves.
        internal const string SettingsMultiplayerRolesGet = "settings.multiplayer_roles get";
        internal const string SettingsMultiplayerRolesSet = "settings.multiplayer_roles set";
#endif

        // settings.player
        internal const string SettingsPlayerGet = "settings.player get";
        internal const string SettingsPlayerSet = "settings.player set";

        // settings.quality
        internal const string SettingsQualityGet = "settings.quality get";
        internal const string SettingsQualitySet = "settings.quality set";

        // settings.tags_layers
        internal const string SettingsTagsLayersGet = "settings.tags_layers get";
        internal const string SettingsTagsLayersSet = "settings.tags_layers set";

        // settings.time
        internal const string SettingsTimeSet = "settings.time set";

        // test
        internal const string TestCancel = "test cancel";
        internal const string TestList = "test list";
        internal const string TestRun = "test run";
        internal const string TestStatus = "test status";
    }
}

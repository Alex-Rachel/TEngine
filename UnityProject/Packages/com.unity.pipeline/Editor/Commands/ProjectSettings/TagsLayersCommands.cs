using System.Collections.Generic;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditorInternal;

namespace Unity.Pipeline.Editor.Commands.ProjectSettings
{
    /// <summary>
    /// Get/set project tags and layers (CLI-202). Tags use the
    /// <see cref="InternalEditorUtility"/> add/remove API; user layers (indices 8..31; 0..7 are
    /// reserved) are assigned via the TagManager's serialized <c>layers</c> array.
    /// </summary>
    static class TagsLayersCommands
    {
        const string Domain = "tags_layers";
        const string Group = "settings." + Domain;
        const string TagManagerPath = "ProjectSettings/TagManager.asset";
        const int FirstUserLayer = 8;
        const int LastUserLayer = 31;

        [CliGroupCommand(Group, "Read the project's tags and (named) layers.", MainThreadRequired = true, Tags = new[] { "settings/tags_layers" })]
        public static ProjectSettingsResponse Get() => ProjectSettingsCommand.Get(Domain, Read);

        [CliGroupCommand(Group, "Add/remove tags and assign user layer names (index 8-31). Requires confirm=true; use dry-run to preview. Not undoable via Ctrl+Z.", MainThreadRequired = true, Tags = new[] { "settings/tags_layers" })]
        public static ProjectSettingsResponse Set(
            [CliArg("settings", "Tag/layer changes to make.")] TagsLayersInput settings = null,
            [CliArg("confirm", "Apply the change. Without it the call is refused.")] bool confirm = false,
            [CliArg("dry-run", "Preview the change without applying it.")] bool dryRun = false)
        {
            if (settings == null)
                return ProjectSettingsResponse.Fail(Domain, "No 'settings' object provided.");

            if (settings.SetLayers != null)
            {
                foreach (var layer in settings.SetLayers)
                {
                    if (layer == null)
                        continue;
                    if (layer.Index < FirstUserLayer || layer.Index > LastUserLayer)
                        return ProjectSettingsResponse.Fail(Domain,
                            $"layer index {layer.Index} is reserved or out of range; user layers are {FirstUserLayer}..{LastUserLayer}.");
                }
            }

            var existingTags = new HashSet<string>(InternalEditorUtility.tags);
            var existingLayers = InternalEditorUtility.layers;
            var changes = new List<string>();

            if (settings.AddTags != null)
                foreach (var tag in settings.AddTags)
                    if (!string.IsNullOrEmpty(tag) && !existingTags.Contains(tag))
                        changes.Add($"add tag '{tag}'");

            if (settings.RemoveTags != null)
                foreach (var tag in settings.RemoveTags)
                    if (!string.IsNullOrEmpty(tag) && existingTags.Contains(tag))
                        changes.Add($"remove tag '{tag}'");

            if (settings.SetLayers != null)
                foreach (var layer in settings.SetLayers)
                    if (layer != null)
                    {
                        var desired = layer.Name ?? string.Empty;
                        var current = (existingLayers != null && layer.Index >= 0 && layer.Index < existingLayers.Length)
                            ? (existingLayers[layer.Index] ?? string.Empty)
                            : string.Empty;
                        if (current != desired)
                            changes.Add($"layer[{layer.Index}] = '{desired}'");
                    }

            if (changes.Count == 0)
                return NoChanges();

            var planText = "Set tags/layers: " + string.Join("; ", changes);

            void Apply()
            {
                // Remove before add so a remove+re-add in one call behaves predictably.
                if (settings.RemoveTags != null)
                    foreach (var tag in settings.RemoveTags)
                        if (!string.IsNullOrEmpty(tag))
                            InternalEditorUtility.RemoveTag(tag);

                if (settings.AddTags != null)
                    foreach (var tag in settings.AddTags)
                        if (!string.IsNullOrEmpty(tag))
                            InternalEditorUtility.AddTag(tag);

                if (settings.SetLayers != null && settings.SetLayers.Length > 0)
                {
                    var so = ProjectSettingsAsset.Load(TagManagerPath);
                    var layersProp = so.FindProperty("layers");
                    foreach (var layer in settings.SetLayers)
                    {
                        if (layer == null)
                            continue;
                        layersProp.GetArrayElementAtIndex(layer.Index).stringValue = layer.Name ?? string.Empty;
                    }
                    so.ApplyModifiedProperties();
                }

                AssetDatabase.SaveAssets();
            }

            return ProjectSettingsCommand.Apply(Domain, confirm, dryRun, settings, () => planText, Apply, Read);
        }

        static ProjectSettingsResponse NoChanges()
        {
            var response = ProjectSettingsCommand.Get(Domain, Read);
            response.Message = "No changes specified; nothing to apply.";
            return response;
        }

        static Dictionary<string, object> Read() => new Dictionary<string, object>
        {
            ["tags"] = InternalEditorUtility.tags,
            ["layers"] = InternalEditorUtility.layers
        };
    }

    /// <summary>Tag/layer changes. All fields optional; omitted ones make no change.</summary>
    class TagsLayersInput : IStructuredCommandInput
    {
        [CliArg("add-tags", "Tag names to add.")]
        public string[] AddTags { get; set; }

        [CliArg("remove-tags", "Tag names to remove.")]
        public string[] RemoveTags { get; set; }

        [CliArg("set-layers", "User layer assignments (index 8-31).")]
        public LayerAssignment[] SetLayers { get; set; }
    }

    /// <summary>Assigns a name to a single user layer slot.</summary>
    class LayerAssignment : IStructuredCommandInput
    {
        [CliArg("index", "Layer index (8-31 for user layers).", Required = true)]
        public int Index { get; set; }

        [CliArg("name", "Layer name (empty string clears the slot).", Required = true)]
        public string Name { get; set; }
    }
}

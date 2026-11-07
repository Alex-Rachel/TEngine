using System.Collections.Generic;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands.ProjectSettings
{
    /// <summary>
    /// Get/set a representative slice of 3D <see cref="Physics"/> settings (CLI-202): gravity (by
    /// component), default solver iterations, and the bounce threshold.
    /// </summary>
    static class PhysicsSettingsCommands
    {
        const string Domain = "physics";
        const string Group = "settings." + Domain;

        [CliGroupCommand(Group, "Read Physics settings (gravity, solver iterations, bounce threshold).", MainThreadRequired = true, Tags = new[] { "settings/physics" })]
        public static ProjectSettingsResponse Get() => ProjectSettingsCommand.Get(Domain, Read);

        [CliGroupCommand(Group, "Change Physics settings. Requires confirm=true; use dry-run to preview. Not undoable via Ctrl+Z.", MainThreadRequired = true, Tags = new[] { "settings/physics" })]
        public static ProjectSettingsResponse Set(
            [CliArg("settings", "Fields to change; omitted fields are left unchanged.")] PhysicsSettingsInput settings = null,
            [CliArg("confirm", "Apply the change. Without it the call is refused.")] bool confirm = false,
            [CliArg("dry-run", "Preview the change without applying it.")] bool dryRun = false)
        {
            if (settings == null)
                return ProjectSettingsResponse.Fail(Domain, "No 'settings' object provided.");

            var changes = new List<string>();

            var gravity = Physics.gravity;
            var newGravity = gravity;
            if (settings.GravityX.HasValue) newGravity.x = settings.GravityX.Value;
            if (settings.GravityY.HasValue) newGravity.y = settings.GravityY.Value;
            if (settings.GravityZ.HasValue) newGravity.z = settings.GravityZ.Value;
            if (newGravity != gravity)
                changes.Add($"gravity {gravity} -> {newGravity}");

            if (settings.DefaultSolverIterations.HasValue && settings.DefaultSolverIterations.Value != Physics.defaultSolverIterations)
                changes.Add($"default-solver-iterations {Physics.defaultSolverIterations} -> {settings.DefaultSolverIterations.Value}");
            if (settings.BounceThreshold.HasValue && !Mathf.Approximately(settings.BounceThreshold.Value, Physics.bounceThreshold))
                changes.Add($"bounce-threshold {Physics.bounceThreshold} -> {settings.BounceThreshold.Value}");

            if (changes.Count == 0)
                return NoChanges();

            var planText = "Set physics settings: " + string.Join("; ", changes);

            void Apply()
            {
                Physics.gravity = newGravity;
                if (settings.DefaultSolverIterations.HasValue) Physics.defaultSolverIterations = settings.DefaultSolverIterations.Value;
                if (settings.BounceThreshold.HasValue) Physics.bounceThreshold = settings.BounceThreshold.Value;
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

        static Dictionary<string, object> Read()
        {
            var g = Physics.gravity;
            return new Dictionary<string, object>
            {
                ["gravity-x"] = g.x,
                ["gravity-y"] = g.y,
                ["gravity-z"] = g.z,
                ["default-solver-iterations"] = Physics.defaultSolverIterations,
                ["bounce-threshold"] = Physics.bounceThreshold
            };
        }
    }

    /// <summary>Physics settings to change. Null/omitted fields are left unchanged.</summary>
    class PhysicsSettingsInput : IStructuredCommandInput
    {
        [CliArg("gravity-x", "Gravity X component.")]
        public float? GravityX { get; set; }

        [CliArg("gravity-y", "Gravity Y component (e.g. -9.81).")]
        public float? GravityY { get; set; }

        [CliArg("gravity-z", "Gravity Z component.")]
        public float? GravityZ { get; set; }

        [CliArg("default-solver-iterations", "Default solver iteration count.")]
        public int? DefaultSolverIterations { get; set; }

        [CliArg("bounce-threshold", "Bounce threshold velocity.")]
        public float? BounceThreshold { get; set; }
    }
}

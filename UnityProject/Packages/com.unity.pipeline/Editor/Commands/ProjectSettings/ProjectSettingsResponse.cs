using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Unity.Pipeline.Editor.Commands.ProjectSettings
{
    /// <summary>
    /// <para>
    /// Shared response for the project-settings get/set commands (CLI-202). One type backs every
    /// settings group (PlayerSettings, Quality, Graphics, Physics, Time, Input, Tags &amp; Layers,
    /// Audio) so agents get a uniform shape across them.
    /// </para>
    ///
    /// <para><see cref="Values"/> carries the current settings for the group as a flat
    /// name → value map — returned by a <c>get</c>, and re-read after a <c>set</c> so the caller can
    /// confirm the change without a second round-trip.</para>
    ///
    /// <para>Writes follow the shared <c>confirm</c>/<c>dry-run</c> convention, so
    /// <see cref="Applied"/> / <see cref="DryRun"/> mirror its semantics: a change only mutates when
    /// it actually ran (not a preview, not refused). <see cref="RequiresDomainReload"/> and
    /// <see cref="RequiresReimport"/> flag side effects an agent must wait out — e.g. switching the
    /// scripting backend or API level triggers a domain reload; some graphics/texture changes trigger
    /// an asset reimport. They describe the operation, so a <c>dry-run</c> still reports them.</para>
    /// </summary>
    [Serializable]
    class ProjectSettingsResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        /// <summary>Settings group this response is for (e.g. "player", "quality", "time").</summary>
        [JsonProperty("group")]
        public string Group { get; set; }

        /// <summary>True only when a write actually mutated the project (false for get/dry-run/refused/failed).</summary>
        [JsonProperty("applied")]
        public bool Applied { get; set; }

        /// <summary>True when a write was a preview only (no mutation).</summary>
        [JsonProperty("dryRun")]
        public bool DryRun { get; set; }

        /// <summary>The operation triggers a domain reload; the agent should wait for the editor to settle.</summary>
        [JsonProperty("requiresDomainReload")]
        public bool RequiresDomainReload { get; set; }

        /// <summary>The operation triggers an asset reimport; the agent should wait for it to finish.</summary>
        [JsonProperty("requiresReimport")]
        public bool RequiresReimport { get; set; }

        /// <summary>Current settings for the group as a name → value map.</summary>
        [JsonProperty("values")]
        public Dictionary<string, object> Values { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        public static ProjectSettingsResponse Fail(string group, string message) => new ProjectSettingsResponse
        {
            Success = false,
            Group = group,
            Message = message
        };
    }
}

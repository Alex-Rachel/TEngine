using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands.ProjectSettings
{
    /// <summary>
    /// Shared plumbing the per-group project-settings commands (CLI-202) wrap, so each group's
    /// <c>get</c>/<c>set</c> handler stays thin and the policy is applied uniformly:
    /// <list type="bullet">
    /// <item><description><see cref="Get"/> — read a group's values into a <see cref="ProjectSettingsResponse"/>.</description></item>
    /// <item><description><see cref="Apply"/> — gate a write with the <c>confirm</c>/<c>dry-run</c> convention and
    /// apply it (project-settings writes are not part of Unity's Undo), then re-read the group so the response
    /// reflects the post-change state, and surface domain-reload / reimport signals.</description></item>
    /// </list>
    ///
    /// Group handlers supply two callbacks: <c>readValues</c> (snapshot the group's current values)
    /// and, for writes, <c>apply</c> (perform the mutation) plus a <c>plan</c> describing the intended
    /// effect. All run on the main thread (these commands are <c>MainThreadRequired = true</c>).
    /// </summary>
    static class ProjectSettingsCommand
    {
        /// <summary>
        /// Read-only path: build a success response carrying the group's current values.
        /// </summary>
        public static ProjectSettingsResponse Get(string group, Func<Dictionary<string, object>> readValues)
        {
            if (readValues == null)
                throw new ArgumentNullException(nameof(readValues));

            if (!TryReadValues(readValues, out var values, out var readError))
                return ProjectSettingsResponse.Fail(group, $"Failed to read {group} settings: {readError}");

            return new ProjectSettingsResponse
            {
                Success = true,
                Group = group,
                Values = values,
                Message = $"Read {group} settings."
            };
        }

        /// <summary>
        /// Write path: gate the change with the <c>confirm</c>/<c>dry-run</c> convention and apply it
        /// (project-settings writes are not undoable via Ctrl+Z), then re-read the
        /// group. <paramref name="requiresDomainReload"/> / <paramref name="requiresReimport"/> declare
        /// side effects of this specific change (Unity defers them, so the response returns first);
        /// they are reported for an applied change and for a dry-run preview alike, and suppressed when
        /// the operation is refused or fails.
        /// </summary>
        public static ProjectSettingsResponse Apply(
            string group,
            bool confirm,
            bool dryRun,
            object auditParams,
            Func<string> plan,
            Action apply,
            Func<Dictionary<string, object>> readValues,
            bool requiresDomainReload = false,
            bool requiresReimport = false)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            if (apply == null)
                throw new ArgumentNullException(nameof(apply));
            if (readValues == null)
                throw new ArgumentNullException(nameof(readValues));

            string planText;
            try
            {
                planText = plan();
            }
            catch (Exception ex)
            {
                return ProjectSettingsResponse.Fail(group, $"Failed to compute the operation preview: {ex.Message}");
            }

            // Inline confirm / dry-run gate + Undo grouping (the codebase-wide mutating convention).
            var response = new ProjectSettingsResponse { Group = group };
            if (dryRun)
            {
                response.Success = true;
                response.DryRun = true;
                response.Applied = false;
                response.Message = $"Dry run — no changes made. Intended effect: {planText}";
            }
            else if (!confirm)
            {
                response.Success = false;
                response.Applied = false;
                response.Message =
                    "Refused: this operation mutates the project. Re-run with confirm=true to apply, " +
                    "or dry-run=true to preview the change.";
            }
            else
            {
                try
                {
                    apply();
                    response.Success = true;
                    response.Applied = true;
                    response.Message = planText;
                }
                catch (Exception ex)
                {
                    response.Success = false;
                    response.Applied = false;
                    response.Message = $"Operation failed: {ex.Message}";
                }
            }

            // Side-effect signals are only meaningful when the operation went through (applied) or was
            // previewed (dry run); suppress them for a refusal or failure.
            response.RequiresDomainReload = response.Success && requiresDomainReload;
            response.RequiresReimport = response.Success && requiresReimport;

            // Re-read so the caller sees the resulting (or, for a dry run / refusal, the unchanged)
            // state. A read failure must not mask the write outcome, so it is non-fatal here.
            if (TryReadValues(readValues, out var values, out var readError))
                response.Values = values;
            else
                Debug.LogWarning($"[pipeline] Read-back of {group} settings failed: {readError}");

            return response;
        }

        private static bool TryReadValues(Func<Dictionary<string, object>> readValues,
            out Dictionary<string, object> values, out string error)
        {
            try
            {
                values = readValues() ?? new Dictionary<string, object>();
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                values = null;
                error = ex.Message;
                return false;
            }
        }
    }
}

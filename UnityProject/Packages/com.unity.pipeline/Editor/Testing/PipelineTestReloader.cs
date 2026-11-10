using System;
using System.IO;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Testing
{
    /// <summary>
    /// Resumes a test run that a domain reload interrupted.
    ///
    /// Kept apart from <see cref="PipelineTestRunner"/> on purpose: that class's static fields are
    /// typed against UnityEditor.TestRunner, so touching it at all loads that assembly. This runs on
    /// every domain reload, and only reaches the runner when a run is actually pending — when the
    /// assembly cannot be loaded, an idle editor stays silent and a pending run fails with one
    /// warning instead of an exception per reload.
    /// </summary>
    [InitializeOnLoad]
    static class PipelineTestReloader
    {
        static PipelineTestReloader()
        {
            EditorApplication.delayCall += CheckForPendingTests;
        }

        private static void CheckForPendingTests()
        {
            if (!File.Exists(PipelineTestRunner.TestRequestFile)) return;
            if (File.Exists(PipelineTestRunner.TestStatusFile)) return; // Already completed

            try
            {
                ResumePendingTests();
            }
            catch (Exception ex) when (ex is TypeLoadException || ex is FileNotFoundException || ex is FileLoadException)
            {
                // End the run the way PipelineTestRunner does when resuming fails: an error status
                // for get_test_status to report, and no request file, so later reloads stop retrying.
                var message = $"Failed to resume tests: {ex.Message}";
                Debug.LogWarning($"[PipelineTestRunner] {message}");
                try
                {
                    File.WriteAllText(PipelineTestRunner.TestStatusFile,
                        JsonConvert.SerializeObject(new { status = "error", message }, Formatting.Indented));
                    File.Delete(PipelineTestRunner.TestRequestFile);
                }
                catch (Exception ioEx)
                {
                    Debug.LogError($"[PipelineTestRunner] Failed to record the failed resume: {ioEx.Message}");
                }
            }
        }

        // A separate, non-inlined method so the type load it triggers surfaces when it is called,
        // inside the try above, rather than when CheckForPendingTests itself is compiled.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ResumePendingTests()
        {
            PipelineTestRunner.CheckForPendingTests();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

[assembly: InternalsVisibleTo("Unity.Pipeline.Tests.Editor")]

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// Appends editor pipeline request/response transactions to &lt;project&gt;/Logs/pipeline.log as a
    /// JSON array. <see cref="RotateForNewSession"/> rotates any existing log to pipeline_old.log on
    /// the first server start of a Unity session: SessionState survives domain reloads but resets on
    /// editor restart, so the log accumulates across a session's server start/stop cycles and starts
    /// fresh each new session.
    ///
    /// Rotation must run on the main thread (SessionState is main-thread only), hence it lives in
    /// RotateForNewSession (called from server startup), NOT in Append — which runs on the background
    /// HTTP thread and is restricted to thread-safe file I/O.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [NoAutoStaticsCleanup]
#endif
    internal static class PipelineTransactionLog
    {
        private const string SessionRotatedKey = "Unity.Pipeline.TransactionLog.SessionRotated";
        private const string LogFileName = "pipeline.log";
        private const string OldLogFileName = "pipeline_old.log";

        // Field names are the JSON keys, so they intentionally stay lowercase. request/response are
        // JToken so the raw JSON is embedded as real JSON (not a stringified blob) in the log array.
        private struct Entry
        {
            public JToken request;
            public JToken response;
            public string time;
        }

        /// <summary>
        /// On the first call of a Unity session, back up any existing pipeline.log to pipeline_old.log
        /// so the session starts with a fresh log. Gated by SessionState (resets on editor restart),
        /// so domain-reload server restarts within a session do not rotate. Main thread only.
        /// </summary>
        public static void RotateForNewSession()
        {
            try
            {
                if (SessionState.GetBool(SessionRotatedKey, false))
                    return;

                Rotate(GetLogsDirectory());
                SessionState.SetBool(SessionRotatedKey, true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Pipeline transaction log rotation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Append one transaction to the JSON-array log. Runs on the background HTTP thread, so it
        /// uses only thread-safe file I/O (no Unity editor APIs).
        /// </summary>
        public static void Append(string requestJson, string responseJson)
        {
            try
            {
                Append(GetLogsDirectory(), requestJson, responseJson);
            }
            catch (Exception ex)
            {
                // Logging must never break request handling.
                Debug.LogWarning($"Pipeline transaction log write failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Back up an existing pipeline.log in <paramref name="logsDir"/> to pipeline_old.log,
        /// replacing any prior backup. No SessionState gate — exposed as the testable rotation seam.
        /// </summary>
        internal static void Rotate(string logsDir)
        {
            Directory.CreateDirectory(logsDir);

            var logPath = Path.Combine(logsDir, LogFileName);
            var oldPath = Path.Combine(logsDir, OldLogFileName);
            RotatingFileBackup.RotateToBackup(logPath, oldPath);
        }

        /// <summary>
        /// Serializes the log's read-modify-write. Requests are processed concurrently since the
        /// /api/progress work (an exec response can be written while the next exec runs), so two
        /// appends may race; without this lock they would drop or corrupt entries.
        /// </summary>
        private static readonly object m_AppendGate = new object();

        /// <summary>
        /// Append a transaction to the JSON-array log in an explicit directory. Testable seam behind
        /// the public <see cref="Append(string,string)"/>.
        /// </summary>
        internal static void Append(string logsDir, string requestJson, string responseJson)
        {
            lock (m_AppendGate)
            {
                AppendLocked(logsDir, requestJson, responseJson);
            }
        }

        private static void AppendLocked(string logsDir, string requestJson, string responseJson)
        {
            Directory.CreateDirectory(logsDir);
            var logPath = Path.Combine(logsDir, LogFileName);

            var entries = ReadEntries(logPath);
            entries.Add(new Entry
            {
                request = ToJsonToken(requestJson),
                response = ToJsonToken(responseJson),
                time = DateTime.UtcNow.ToString("o")
            });

            File.WriteAllText(logPath, JsonConvert.SerializeObject(entries, Formatting.Indented));
        }

        /// <summary>
        /// Embed a raw JSON payload as a real JSON token. Falls back to a JSON string for empty or
        /// non-JSON payloads, so the log file always stays valid JSON.
        /// </summary>
        private static JToken ToJsonToken(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return JValue.CreateNull();

            try
            {
                return JToken.Parse(raw);
            }
            catch (JsonException)
            {
                return new JValue(raw);
            }
        }

        private static string GetLogsDirectory()
        {
            var projectPath = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectPath, "Logs");
        }

        private static List<Entry> ReadEntries(string logPath)
        {
            if (!File.Exists(logPath))
                return new List<Entry>();

            var json = File.ReadAllText(logPath);
            if (string.IsNullOrEmpty(json))
                return new List<Entry>();

            return JsonConvert.DeserializeObject<List<Entry>>(json) ?? new List<Entry>();
        }
    }
}

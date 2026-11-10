using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Unity.Pipeline.Models;
using UnityEngine;

namespace Unity.Pipeline.Console
{
    /// <summary>
    /// Bounded, thread-safe ring buffer of captured Unity console entries that backs the
    /// <c>console</c> command.
    ///
    /// Why thread-safe: entries arrive via <see cref="Application.logMessageReceivedThreaded"/>,
    /// which can fire from background threads, while the command reads the buffer from an HTTP
    /// request thread. All access is guarded by a single lock.
    ///
    /// Each entry gets a monotonic <see cref="ConsoleLogEntry.Seq"/> that never repeats and only
    /// increases — including across domain reloads, because the buffer (and the seq counter) is
    /// persisted via <see cref="Save"/>/<see cref="Load"/>. The seq is the cursor a "--follow"
    /// client uses to fetch only newer entries.
    /// </summary>
    class ConsoleLogBuffer
    {
        /// <summary>Maximum number of entries retained. Older entries are evicted first.</summary>
        public const int Capacity = 2000;

        // Severity levels, ordered so a "minimum severity" filter is a simple >= comparison.
        public const int SeverityLog = 0;
        public const int SeverityWarn = 1;
        public const int SeverityError = 2;

        public const string LevelLog = "log";
        public const string LevelWarn = "warn";
        public const string LevelError = "error";

        readonly object m_Lock = new object();
        readonly Queue<StoredEntry> m_Entries = new Queue<StoredEntry>();
        long m_LastSeq;
        string m_Session = Guid.NewGuid().ToString("N");

        struct StoredEntry
        {
            public int Severity;
            public ConsoleLogEntry Entry;
        }

        /// <summary>
        /// Identifies the capture session that <see cref="ConsoleLogEntry.Seq"/> values belong to.
        /// Sequence numbers restart from zero with the process, so a cursor only names the same
        /// entries within one session. In the Editor this is adopted from SessionState, which has
        /// exactly the lifetime of one editor launch; in a player the constructor's value stands.
        /// </summary>
        public string Session
        {
            get { lock (m_Lock) { return m_Session; } }
        }

        /// <summary>Adopt an externally owned session identifier. See the Editor bootstrap.</summary>
        /// <param name="session">The identifier to report as <see cref="Session"/>.</param>
        public void SetSession(string session)
        {
            if (string.IsNullOrEmpty(session))
                return;

            lock (m_Lock)
                m_Session = session;
        }

        /// <summary>
        /// Capture a console entry. Assigns the next sequence number and evicts the oldest entry if
        /// the buffer is full. Safe to call from any thread.
        /// </summary>
        /// <param name="type">Unity log type; mapped to a severity via <see cref="SeverityFromLogType"/>.</param>
        /// <param name="message">The log message.</param>
        /// <param name="stackTrace">The stack trace, if any.</param>
        /// <param name="timestampUtc">UTC time the entry was captured.</param>
        /// <param name="seeded">
        /// True when the entry was read from the Editor console's own store rather than captured
        /// live, which makes <paramref name="timestampUtc"/> the read time.
        /// </param>
        public void Add(LogType type, string message, string stackTrace, DateTime timestampUtc, bool seeded = false)
        {
            var severity = SeverityFromLogType(type);
            lock (m_Lock)
            {
                m_LastSeq++;
                if (m_Entries.Count >= Capacity)
                    m_Entries.Dequeue();

                m_Entries.Enqueue(new StoredEntry
                {
                    Severity = severity,
                    Entry = new ConsoleLogEntry
                    {
                        Seq = m_LastSeq,
                        TimestampUtc = timestampUtc,
                        Level = LevelName(severity),
                        LogType = type.ToString(),
                        Message = message ?? string.Empty,
                        StackTrace = stackTrace ?? string.Empty,
                        Seeded = seeded
                    }
                });
            }
        }

        /// <summary>
        /// Query the buffer for the <c>console</c> command.
        /// </summary>
        /// <param name="since">
        /// Cursor: return only entries with <c>Seq &gt; since</c>. Pass a negative value (e.g. -1)
        /// for a snapshot of the most recent entries with no cursor filtering.
        /// </param>
        /// <param name="tail">
        /// Maximum number of entries to return (the most recent matches). Values &lt;= 0 mean "no
        /// limit" (up to <see cref="Capacity"/>).
        /// </param>
        /// <param name="minSeverity">Minimum severity to include (see the Severity* constants).</param>
        /// <param name="sinceSession">
        /// The <see cref="Session"/> that issued <paramref name="since"/>. When it names a different
        /// session the cursor is refused; omit it and the cursor is taken at face value.
        /// </param>
        /// <returns>The matching entries plus cursor/drop metadata for the <c>console</c> command.</returns>
        public ConsoleLogResponse Query(long since, int tail, int minSeverity, string sinceSession = null)
        {
            lock (m_Lock)
            {
                // A cursor issued in another session, or one past the counter, cannot be honored:
                // sequence numbers restart from zero with the process, so such a cursor filters out
                // every entry held and reads as "nothing new". Ignore it, return the tail, and say so.
                var reset = since > m_LastSeq
                    || (!string.IsNullOrEmpty(sinceSession) && sinceSession != m_Session);
                if (reset)
                    since = -1;

                var counts = new ConsoleLevelCounts();
                var matches = new List<ConsoleLogEntry>();
                foreach (var stored in m_Entries)
                {
                    // Counted before the level filter and the tail trim, so the counts describe
                    // what the buffer holds rather than what this call returned.
                    Tally(counts, stored.Severity);

                    if (stored.Severity < minSeverity)
                        continue;
                    if (since >= 0 && stored.Entry.Seq <= since)
                        continue;
                    matches.Add(stored.Entry);
                }

                // Keep only the most recent `tail` matches.
                if (tail > 0 && matches.Count > tail)
                    matches.RemoveRange(0, matches.Count - tail);

                return new ConsoleLogResponse
                {
                    Entries = matches.ToArray(),
                    Cursor = m_LastSeq,
                    Session = m_Session,
                    Returned = matches.Count,
                    Dropped = reset || ComputeDropped(since),
                    Reset = reset,
                    Counts = counts
                };
            }
        }

        /// <summary>
        /// The buffer's counters without its entries: retained counts per severity, the cursor, and
        /// the session that issued it. Backs <c>console_status</c>, which clients poll in a loop, so
        /// it deliberately avoids materializing the entry list.
        /// </summary>
        /// <returns>A response with an empty <see cref="ConsoleLogResponse.Entries"/>.</returns>
        public ConsoleLogResponse Stats()
        {
            lock (m_Lock)
            {
                var counts = new ConsoleLevelCounts();
                foreach (var stored in m_Entries)
                    Tally(counts, stored.Severity);

                return new ConsoleLogResponse
                {
                    Entries = Array.Empty<ConsoleLogEntry>(),
                    Cursor = m_LastSeq,
                    Session = m_Session,
                    Counts = counts
                };
            }
        }

        /// <summary>
        /// True when <paramref name="since"/> refers to entries that have already been evicted, so
        /// the caller missed some output. Caller must hold <see cref="m_Lock"/>.
        /// </summary>
        bool ComputeDropped(long since)
        {
            if (since < 0)
                return false;
            if (since >= m_LastSeq)
                return false;

            // Everything after `since` was evicted (or never retained): buffer empty but seq moved on.
            if (m_Entries.Count == 0)
                return m_LastSeq > since;

            // The next entry the caller expects is since+1; if the oldest entry we still hold is
            // newer than that, the gap in between was evicted.
            var oldestSeq = m_Entries.Peek().Entry.Seq;
            return oldestSeq > since + 1;
        }

        /// <summary>Remove all entries. Does NOT reset the sequence counter (cursors stay monotonic).</summary>
        public void Clear()
        {
            lock (m_Lock)
            {
                m_Entries.Clear();
            }
        }

        /// <summary>Current number of retained entries. For diagnostics/tests.</summary>
        public int Count
        {
            get { lock (m_Lock) { return m_Entries.Count; } }
        }

        /// <summary>The highest sequence number assigned so far (the live cursor).</summary>
        public long LastSeq
        {
            get { lock (m_Lock) { return m_LastSeq; } }
        }

        /// <summary>
        /// Raise the sequence counter to <paramref name="seq"/> if it is behind. Never lowers it, so
        /// a cursor already handed to a client can never be issued again for different entries.
        /// </summary>
        /// <param name="seq">The floor the counter must be at or above.</param>
        public void RaiseLastSeq(long seq)
        {
            lock (m_Lock)
            {
                if (seq > m_LastSeq)
                    m_LastSeq = seq;
            }
        }

        #region Persistence

        [Serializable]
        class Snapshot
        {
            [JsonProperty("lastSeq")] public long LastSeq;
            [JsonProperty("entries")] public ConsoleLogEntry[] Entries;
        }

        /// <summary>
        /// Persist the buffer (entries + sequence counter) to <paramref name="path"/> so it survives
        /// a domain reload. Failures are swallowed (logging is best-effort, never fatal).
        /// </summary>
        /// <param name="path">File to write the snapshot to.</param>
        public void Save(string path)
        {
            try
            {
                Snapshot snapshot;
                lock (m_Lock)
                {
                    var entries = new ConsoleLogEntry[m_Entries.Count];
                    var i = 0;
                    foreach (var stored in m_Entries)
                        entries[i++] = stored.Entry;
                    snapshot = new Snapshot { LastSeq = m_LastSeq, Entries = entries };
                }

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonConvert.SerializeObject(snapshot));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Pipeline] Failed to persist console log buffer: {ex.Message}");
            }
        }

        /// <summary>
        /// Merge a buffer previously written by <see cref="Save"/> into this one. Missing or
        /// unreadable files leave the buffer untouched. Returns true if a snapshot was loaded.
        /// </summary>
        /// <param name="path">File previously written by <see cref="Save"/>.</param>
        /// <returns>True if a snapshot was found and loaded.</returns>
        public bool Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return false;

                var snapshot = JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(path));
                if (snapshot == null)
                    return false;

                lock (m_Lock)
                {
                    // Merge rather than replace. Where [NoAutoStaticsCleanup] keeps this buffer alive
                    // across a domain reload, entries logged since the snapshot are already here, and
                    // a snapshot entry whose seq the counter has already issued is one of those.
                    // Anything with a higher seq was never seen in memory, so it is newer than
                    // everything held and appends in order.
                    if (snapshot.Entries != null)
                    {
                        foreach (var entry in snapshot.Entries)
                        {
                            if (entry == null || entry.Seq <= m_LastSeq)
                                continue;
                            if (m_Entries.Count >= Capacity)
                                m_Entries.Dequeue();

                            m_Entries.Enqueue(new StoredEntry
                            {
                                Severity = SeverityFromLevelName(entry.Level),
                                Entry = entry
                            });
                            m_LastSeq = entry.Seq;
                        }
                    }

                    // Keep the counter ahead of the snapshot even when it held no entries, and never
                    // lower it: a cursor already handed out must not be issued a second time.
                    if (snapshot.LastSeq > m_LastSeq)
                        m_LastSeq = snapshot.LastSeq;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Pipeline] Failed to restore console log buffer: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Severity mapping

        /// <summary>Map a Unity <see cref="LogType"/> to an ordered severity.</summary>
        /// <param name="type">The Unity log type.</param>
        /// <returns>One of the Severity* constants.</returns>
        public static int SeverityFromLogType(LogType type)
        {
            switch (type)
            {
                case LogType.Warning:
                    return SeverityWarn;
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    return SeverityError;
                default:
                    return SeverityLog;
            }
        }

        /// <summary>
        /// Parse a "--level" value ("log"/"warn"/"error", plus common aliases) into a minimum
        /// severity. Unknown values fall back to <see cref="SeverityLog"/> (everything), matching the
        /// lenient behavior of the existing log command.
        /// </summary>
        /// <param name="level">Level name to parse (case-insensitive).</param>
        /// <returns>One of the Severity* constants.</returns>
        public static int SeverityFromLevelName(string level)
        {
            switch ((level ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "error":
                case "err":
                case "exception":
                    return SeverityError;
                case "warn":
                case "warning":
                    return SeverityWarn;
                default:
                    return SeverityLog;
            }
        }

        /// <summary>Add one entry of <paramref name="severity"/> to <paramref name="counts"/>.</summary>
        /// <param name="counts">The tally to add to.</param>
        /// <param name="severity">One of the Severity* constants.</param>
        static void Tally(ConsoleLevelCounts counts, int severity)
        {
            switch (severity)
            {
                case SeverityError:
                    counts.Error++;
                    break;
                case SeverityWarn:
                    counts.Warn++;
                    break;
                default:
                    counts.Log++;
                    break;
            }
        }

        /// <summary>The canonical level name for a severity.</summary>
        /// <param name="severity">One of the Severity* constants.</param>
        /// <returns>The matching Level* constant.</returns>
        public static string LevelName(int severity)
        {
            switch (severity)
            {
                case SeverityError:
                    return LevelError;
                case SeverityWarn:
                    return LevelWarn;
                default:
                    return LevelLog;
            }
        }

        #endregion
    }
}

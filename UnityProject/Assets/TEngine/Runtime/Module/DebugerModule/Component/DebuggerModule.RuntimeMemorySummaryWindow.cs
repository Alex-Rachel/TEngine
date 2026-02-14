using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_5_5_OR_NEWER
using UnityEngine.Profiling;
#endif

namespace TEngine
{
    public sealed partial class Debugger
    {
        private sealed partial class RuntimeMemorySummaryWindow : ScrollableDebuggerWindowBase
        {
            private readonly List<Record> _records = new List<Record>();
            private readonly Comparison<Record> _recordComparer = RecordComparer;
            private DateTime _sampleTime = DateTime.MinValue;
            private int _sampleCount = 0;
            private long _sampleSize = 0L;

            protected override void OnDrawScrollableWindow()
            {
                GUILayout.Label("<b>Runtime Memory Summary</b>");
                GUILayout.BeginVertical("box");
                {
                    if (GUILayout.Button("Take Sample", GUILayout.Height(30f)))
                    {
                        TakeSample();
                    }

                    if (_sampleTime <= DateTime.MinValue)
                    {
                        GUILayout.Label("<b>Please take sample first.</b>");
                    }
                    else
                    {
                        GUILayout.Label(Utility.Text.Format("<b>{0} Objects ({1}) obtained at {2:yyyy-MM-dd HH:mm:ss}.</b>", _sampleCount, GetByteLengthString(_sampleSize), _sampleTime.ToLocalTime()));

                        GUILayout.BeginHorizontal();
                        {
                            GUILayout.Label("<b>Type</b>");
                            GUILayout.Label("<b>Count</b>", GUILayout.Width(120f));
                            GUILayout.Label("<b>Size</b>", GUILayout.Width(120f));
                        }
                        GUILayout.EndHorizontal();

                        for (int i = 0; i < _records.Count; i++)
                        {
                            GUILayout.BeginHorizontal();
                            {
                                GUILayout.Label(_records[i].Name);
                                GUILayout.Label(_records[i].Count.ToString(), GUILayout.Width(120f));
                                GUILayout.Label(GetByteLengthString(_records[i].Size), GUILayout.Width(120f));
                            }
                            GUILayout.EndHorizontal();
                        }
                    }
                }
                GUILayout.EndVertical();
            }

            private void TakeSample()
            {
                _records.Clear();
                _sampleTime = DateTime.UtcNow;
                _sampleCount = 0;
                _sampleSize = 0L;

                UnityEngine.Object[] samples = Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
                for (int i = 0; i < samples.Length; i++)
                {
                    long sampleSize = 0L;
#if UNITY_5_6_OR_NEWER
                    sampleSize = Profiler.GetRuntimeMemorySizeLong(samples[i]);
#else
                    sampleSize = Profiler.GetRuntimeMemorySize(samples[i]);
#endif
                    string name = samples[i].GetType().Name;
                    _sampleCount++;
                    _sampleSize += sampleSize;

                    Record record = null;
                    foreach (Record r in _records)
                    {
                        if (r.Name == name)
                        {
                            record = r;
                            break;
                        }
                    }

                    if (record == null)
                    {
                        record = new Record(name);
                        _records.Add(record);
                    }

                    record.Count++;
                    record.Size += sampleSize;
                }

                _records.Sort(_recordComparer);
            }

            private static int RecordComparer(Record a, Record b)
            {
                int result = b.Size.CompareTo(a.Size);
                if (result != 0)
                {
                    return result;
                }

                result = a.Count.CompareTo(b.Count);
                if (result != 0)
                {
                    return result;
                }

                return a.Name.CompareTo(b.Name);
            }
        }
    }
}

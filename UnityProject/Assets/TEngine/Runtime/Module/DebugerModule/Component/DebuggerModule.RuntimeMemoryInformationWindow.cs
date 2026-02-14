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
        private sealed partial class RuntimeMemoryInformationWindow<T> : ScrollableDebuggerWindowBase where T : UnityEngine.Object
        {
            private const int SHOW_SAMPLE_COUNT = 300;

            private readonly List<Sample> _samples = new List<Sample>();
            private readonly Comparison<Sample> _sampleComparer = SampleComparer;
            private DateTime _sampleTime = DateTime.MinValue;
            private long _sampleSize = 0L;
            private long _duplicateSampleSize = 0L;
            private int _duplicateSimpleCount = 0;

            protected override void OnDrawScrollableWindow()
            {
                string typeName = typeof(T).Name;
                GUILayout.Label(Utility.Text.Format("<b>{0} Runtime Memory Information</b>", typeName));
                GUILayout.BeginVertical("box");
                {
                    if (GUILayout.Button(Utility.Text.Format("Take Sample for {0}", typeName), GUILayout.Height(30f)))
                    {
                        TakeSample();
                    }

                    if (_sampleTime <= DateTime.MinValue)
                    {
                        GUILayout.Label(Utility.Text.Format("<b>Please take sample for {0} first.</b>", typeName));
                    }
                    else
                    {
                        if (_duplicateSimpleCount > 0)
                        {
                            GUILayout.Label(Utility.Text.Format("<b>{0} {1}s ({2}) obtained at {3:yyyy-MM-dd HH:mm:ss}, while {4} {1}s ({5}) might be duplicated.</b>", _samples.Count, typeName, GetByteLengthString(_sampleSize), _sampleTime.ToLocalTime(), _duplicateSimpleCount, GetByteLengthString(_duplicateSampleSize)));
                        }
                        else
                        {
                            GUILayout.Label(Utility.Text.Format("<b>{0} {1}s ({2}) obtained at {3:yyyy-MM-dd HH:mm:ss}.</b>", _samples.Count, typeName, GetByteLengthString(_sampleSize), _sampleTime.ToLocalTime()));
                        }

                        if (_samples.Count > 0)
                        {
                            GUILayout.BeginHorizontal();
                            {
                                GUILayout.Label(Utility.Text.Format("<b>{0} Name</b>", typeName));
                                GUILayout.Label("<b>Type</b>", GUILayout.Width(240f));
                                GUILayout.Label("<b>Size</b>", GUILayout.Width(80f));
                            }
                            GUILayout.EndHorizontal();
                        }

                        int count = 0;
                        for (int i = 0; i < _samples.Count; i++)
                        {
                            GUILayout.BeginHorizontal();
                            {
                                GUILayout.Label(_samples[i].Highlight ? Utility.Text.Format("<color=yellow>{0}</color>", _samples[i].Name) : _samples[i].Name);
                                GUILayout.Label(_samples[i].Highlight ? Utility.Text.Format("<color=yellow>{0}</color>", _samples[i].Type) : _samples[i].Type, GUILayout.Width(240f));
                                GUILayout.Label(_samples[i].Highlight ? Utility.Text.Format("<color=yellow>{0}</color>", GetByteLengthString(_samples[i].Size)) : GetByteLengthString(_samples[i].Size), GUILayout.Width(80f));
                            }
                            GUILayout.EndHorizontal();

                            count++;
                            if (count >= SHOW_SAMPLE_COUNT)
                            {
                                break;
                            }
                        }
                    }
                }
                GUILayout.EndVertical();
            }

            private void TakeSample()
            {
                _sampleTime = DateTime.UtcNow;
                _sampleSize = 0L;
                _duplicateSampleSize = 0L;
                _duplicateSimpleCount = 0;
                _samples.Clear();

                T[] samples = Resources.FindObjectsOfTypeAll<T>();
                for (int i = 0; i < samples.Length; i++)
                {
                    long sampleSize = 0L;
#if UNITY_5_6_OR_NEWER
                    sampleSize = Profiler.GetRuntimeMemorySizeLong(samples[i]);
#else
                    sampleSize = Profiler.GetRuntimeMemorySize(samples[i]);
#endif
                    _sampleSize += sampleSize;
                    _samples.Add(new Sample(samples[i].name, samples[i].GetType().Name, sampleSize));
                }

                _samples.Sort(_sampleComparer);

                for (int i = 1; i < _samples.Count; i++)
                {
                    if (_samples[i].Name == _samples[i - 1].Name && _samples[i].Type == _samples[i - 1].Type && _samples[i].Size == _samples[i - 1].Size)
                    {
                        _samples[i].Highlight = true;
                        _duplicateSampleSize += _samples[i].Size;
                        _duplicateSimpleCount++;
                    }
                }
            }

            private static int SampleComparer(Sample a, Sample b)
            {
                int result = b.Size.CompareTo(a.Size);
                if (result != 0)
                {
                    return result;
                }

                result = a.Type.CompareTo(b.Type);
                if (result != 0)
                {
                    return result;
                }

                return a.Name.CompareTo(b.Name);
            }
        }
    }
}

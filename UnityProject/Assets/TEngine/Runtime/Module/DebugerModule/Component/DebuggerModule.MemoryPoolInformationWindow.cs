using System;
using System.Collections.Generic;
using UnityEngine;

namespace TEngine
{
    public sealed partial class Debugger
    {
        private sealed class MemoryPoolPoolInformationWindow : ScrollableDebuggerWindowBase
        {
            private readonly Dictionary<string, List<MemoryPoolInfo>> _memoryPoolInfos = new Dictionary<string, List<MemoryPoolInfo>>(StringComparer.Ordinal);
            private readonly Comparison<MemoryPoolInfo> _normalClassNameComparer = NormalClassNameComparer;
            private readonly Comparison<MemoryPoolInfo> _fullClassNameComparer = FullClassNameComparer;
            private bool _showFullClassName = false;

            public override void Initialize(params object[] args)
            {
            }

            protected override void OnDrawScrollableWindow()
            {
                GUILayout.Label("<b>Memory Pool Information</b>");
                GUILayout.BeginVertical("box");
                {
                    DrawItem("Enable Strict Check", MemoryPool.EnableStrictCheck.ToString());
                    DrawItem("Memory Pool Count", MemoryPool.Count.ToString());
                }
                GUILayout.EndVertical();

                _showFullClassName = GUILayout.Toggle(_showFullClassName, "Show Full Class Name");
                _memoryPoolInfos.Clear();
                MemoryPoolInfo[] memoryPoolInfos = MemoryPool.GetAllMemoryPoolInfos();
                foreach (MemoryPoolInfo memoryPoolInfo in memoryPoolInfos)
                {
                    string assemblyName = memoryPoolInfo.Type.Assembly.GetName().Name;
                    List<MemoryPoolInfo> results = null;
                    if (!_memoryPoolInfos.TryGetValue(assemblyName, out results))
                    {
                        results = new List<MemoryPoolInfo>();
                        _memoryPoolInfos.Add(assemblyName, results);
                    }

                    results.Add(memoryPoolInfo);
                }

                foreach (KeyValuePair<string, List<MemoryPoolInfo>> assemblyMemoryPoolInfo in _memoryPoolInfos)
                {
                    GUILayout.Label(Utility.Text.Format("<b>Assembly: {0}</b>", assemblyMemoryPoolInfo.Key));
                    GUILayout.BeginVertical("box");
                    {
                        GUILayout.BeginHorizontal();
                        {
                            GUILayout.Label(_showFullClassName ? "<b>Full Class Name</b>" : "<b>Class Name</b>");
                            GUILayout.Label("<b>Unused</b>", GUILayout.Width(60f));
                            GUILayout.Label("<b>Using</b>", GUILayout.Width(60f));
                            GUILayout.Label("<b>Acquire</b>", GUILayout.Width(60f));
                            GUILayout.Label("<b>Release</b>", GUILayout.Width(60f));
                            GUILayout.Label("<b>Add</b>", GUILayout.Width(60f));
                            GUILayout.Label("<b>Remove</b>", GUILayout.Width(60f));
                        }
                        GUILayout.EndHorizontal();

                        if (assemblyMemoryPoolInfo.Value.Count > 0)
                        {
                            assemblyMemoryPoolInfo.Value.Sort(_showFullClassName ? _fullClassNameComparer : _normalClassNameComparer);
                            foreach (MemoryPoolInfo memoryPoolInfo in assemblyMemoryPoolInfo.Value)
                            {
                                DrawMemoryPoolInfo(memoryPoolInfo);
                            }
                        }
                        else
                        {
                            GUILayout.Label("<i>Memory Pool is Empty ...</i>");
                        }
                    }
                    GUILayout.EndVertical();
                }
            }

            private void DrawMemoryPoolInfo(MemoryPoolInfo memoryPoolInfo)
            {
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Label(_showFullClassName ? memoryPoolInfo.Type.FullName : memoryPoolInfo.Type.Name);
                    GUILayout.Label(memoryPoolInfo.UnusedMemoryCount.ToString(), GUILayout.Width(60f));
                    GUILayout.Label(memoryPoolInfo.UsingMemoryCount.ToString(), GUILayout.Width(60f));
                    GUILayout.Label(memoryPoolInfo.AcquireMemoryCount.ToString(), GUILayout.Width(60f));
                    GUILayout.Label(memoryPoolInfo.ReleaseMemoryCount.ToString(), GUILayout.Width(60f));
                    GUILayout.Label(memoryPoolInfo.AddMemoryCount.ToString(), GUILayout.Width(60f));
                    GUILayout.Label(memoryPoolInfo.RemoveMemoryCount.ToString(), GUILayout.Width(60f));
                }
                GUILayout.EndHorizontal();
            }

            private static int NormalClassNameComparer(MemoryPoolInfo a, MemoryPoolInfo b)
            {
                return a.Type.Name.CompareTo(b.Type.Name);
            }

            private static int FullClassNameComparer(MemoryPoolInfo a, MemoryPoolInfo b)
            {
                return a.Type.FullName.CompareTo(b.Type.FullName);
            }
        }
    }
}

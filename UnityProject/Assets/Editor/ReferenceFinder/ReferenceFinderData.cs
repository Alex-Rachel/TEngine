using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEditor;
using UnityEngine;

// ReSharper disable InconsistentNaming

namespace TEngine.Editor
{
    internal sealed class ReferenceFinderData
    {
        public enum AssetState : byte
        {
            Normal,
            Changed,
            Missing,
            Invalid
        }

        private const string CachePath = "Library/ReferenceFinderCache";
        public const int MinThreadCount = 8;
        private const int SingleThreadReadCount = 100;
        private static readonly int ThreadCount = Math.Max(MinThreadCount, Environment.ProcessorCount);
        private static string _basePath;

        private static readonly HashSet<string> FileExtension = new HashSet<string>
        {
            ".prefab",
            ".unity",
            ".mat",
            ".asset",
            ".anim",
            ".controller"
        };

        private static readonly Regex GuidRegex = new Regex("guid: ([a-z0-9]{32})", RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly Dictionary<(AssetDescription, AssetDescription), int> _dictCache = new Dictionary<(AssetDescription, AssetDescription), int>();
        private readonly List<Dictionary<string, AssetDescription>> _threadAssetDict = new List<Dictionary<string, AssetDescription>>();
        private readonly List<Thread> _threadList = new List<Thread>();
        private int _curReadAssetCount;
        private int _totalCount;
        public string[] allAssets;
        public Dictionary<string, AssetDescription> assetDict = new Dictionary<string, AssetDescription>();

        public void CollectDependenciesInfo()
        {
            try
            {
                _basePath = Application.dataPath.Replace("/Assets", "");
                ReadFromCache();
                allAssets = AssetDatabase.GetAllAssetPaths();
                _totalCount = allAssets.Length;
                _threadList.Clear();
                _curReadAssetCount = 0;
                foreach (Dictionary<string, AssetDescription> i in _threadAssetDict)
                    i.Clear();
                _threadAssetDict.Clear();
                for (int i = 0; i < ThreadCount; i++) _threadAssetDict.Add(new Dictionary<string, AssetDescription>());
                bool allThreadFinish = false;
                for (int i = 0; i < ThreadCount; i++)
                {
                    ThreadStart method = ReadAssetInfo;
                    Thread readThread = new Thread(method);
                    _threadList.Add(readThread);
                    readThread.Start();
                }

                while (!allThreadFinish)
                {
                    if (_curReadAssetCount % 500 == 0 &&
                        EditorUtility.DisplayCancelableProgressBar("Updating", $"Handle {_curReadAssetCount}", (float)_curReadAssetCount / _totalCount))
                    {
                        EditorUtility.ClearProgressBar();
                        foreach (Thread i in _threadList)
                            i.Abort();
                        return;
                    }

                    allThreadFinish = true;
                    foreach (Thread i in _threadList)
                    {
                        if (i.IsAlive)
                        {
                            allThreadFinish = false;
                            break;
                        }
                    }
                }

                foreach (Dictionary<string, AssetDescription> dict in _threadAssetDict)
                {
                    foreach (KeyValuePair<string, AssetDescription> j in dict)
                        assetDict[j.Key] = j.Value;
                }

                EditorUtility.DisplayCancelableProgressBar("Updating", "Write cache", 1f);
                WriteToChache();
                EditorUtility.DisplayCancelableProgressBar("Updating", "Generate reference data", 1f);
                UpdateResourceReferenceInfo();
                EditorUtility.ClearProgressBar();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                EditorUtility.ClearProgressBar();
            }
        }

        public void ReadAssetInfo()
        {
            int index = Thread.CurrentThread.ManagedThreadId % ThreadCount;
            int intervalLength = _totalCount / ThreadCount;
            int start = intervalLength * index;
            int end = start + intervalLength;
            if (_totalCount - end < intervalLength)
                end = _totalCount;
            int readAssetCount = 0;
            for (int i = start; i < end; i++)
            {
                if (readAssetCount % SingleThreadReadCount == 0)
                {
                    _curReadAssetCount += readAssetCount;
                    readAssetCount = 0;
                }

                GetAsset(_basePath, allAssets[i]);
                readAssetCount++;
            }
        }

        public void GetAsset(string dataPath, string assetPath)
        {
            string extLowerStr = Path.GetExtension(assetPath).ToLower();
            bool needReadFile = FileExtension.Contains(extLowerStr);
            string fileName = $"{dataPath}/{assetPath}";
            string metaFile = $"{dataPath}/{assetPath}.meta";
            if (File.Exists(fileName) && File.Exists(metaFile))
            {
                string metaText = File.ReadAllText(metaFile, Encoding.UTF8);
                MatchCollection matchRs = GuidRegex.Matches(metaText);
                string selfGuid = matchRs[0].Groups[1].Value.ToLower();
                string lastModifyTime = File.GetLastWriteTime(fileName).ToString(CultureInfo.InvariantCulture);
                MatchCollection guids = null;
                List<string> depend = new List<string>();
                if (needReadFile)
                {
                    string fileStr = File.ReadAllText(fileName, Encoding.UTF8);
                    guids = GuidRegex.Matches(fileStr);
                }

                int curListIndex = Thread.CurrentThread.ManagedThreadId % ThreadCount;
                Dictionary<string, AssetDescription> curDict = _threadAssetDict[curListIndex];
                if (!curDict.ContainsKey(selfGuid) || curDict[selfGuid].assetDependencyHashString != lastModifyTime)
                {
                    if (guids != null)
                    {
                        for (int index = 0; index < guids.Count; ++index)
                        {
                            Match i = guids[index];
                            depend.Add(i.Groups[1].Value.ToLower());
                        }
                    }

                    AssetDescription ad = new AssetDescription
                    {
                        name = Path.GetFileNameWithoutExtension(assetPath),
                        path = assetPath,
                        assetDependencyHashString = lastModifyTime,
                        dependencies = depend
                    };

                    if (_threadAssetDict[curListIndex].ContainsKey(selfGuid))
                        _threadAssetDict[curListIndex][selfGuid] = ad;
                    else
                        _threadAssetDict[curListIndex].Add(selfGuid, ad);
                }
            }
        }

        private void UpdateResourceReferenceInfo()
        {
            foreach (KeyValuePair<string, AssetDescription> asset in assetDict)
            {
                foreach (string assetGuid in asset.Value.dependencies)
                {
                    if (assetDict.ContainsKey(assetGuid))
                        assetDict[assetGuid].references.Add(asset.Key);
                }
            }
        }

        public bool ReadFromCache()
        {
            assetDict.Clear();
            ClearCache();
            if (File.Exists(CachePath))
            {
                List<string> serializedGuid;
                List<string> serializedDependencyHash;
                List<int[]> serializedDenpendencies;
                using (FileStream fs = File.OpenRead(CachePath))
                {
                    BinaryFormatter bf = new BinaryFormatter();
                    if (EditorUtility.DisplayCancelableProgressBar("Import Cache", "Reading Cache", 0))
                    {
                        EditorUtility.ClearProgressBar();
                        return false;
                    }

                    serializedGuid = (List<string>)bf.Deserialize(fs);
                    serializedDependencyHash = (List<string>)bf.Deserialize(fs);
                    serializedDenpendencies = (List<int[]>)bf.Deserialize(fs);
                    EditorUtility.ClearProgressBar();
                }

                for (int i = 0; i < serializedGuid.Count; ++i)
                {
                    string path = AssetDatabase.GUIDToAssetPath(serializedGuid[i]);
                    if (string.IsNullOrEmpty(path))
                    {
                        AssetDescription ad = new AssetDescription
                        {
                            name = Path.GetFileNameWithoutExtension(path),
                            path = path,
                            assetDependencyHashString = serializedDependencyHash[i]
                        };
                        assetDict.Add(serializedGuid[i], ad);
                    }
                }

                for (int i = 0; i < serializedGuid.Count; ++i)
                {
                    string guid = serializedGuid[i];
                    if (assetDict.ContainsKey(guid))
                    {
                        List<string> guids = new List<string>();
                        foreach (int index in serializedDenpendencies[i])
                        {
                            string g = serializedGuid[index];
                            if (assetDict.ContainsKey(g))
                                guids.Add(g);
                        }

                        assetDict[guid].dependencies = guids;
                    }
                }

                UpdateResourceReferenceInfo();
                return true;
            }

            return false;
        }

        private void WriteToChache()
        {
            if (File.Exists(CachePath))
                File.Delete(CachePath);
            List<string> serializedGuid = new List<string>();
            List<string> serializedDependencyHash = new List<string>();
            List<int[]> serializedDenpendencies = new List<int[]>();
            Dictionary<string, int> guidIndex = new Dictionary<string, int>();
            using FileStream fs = File.OpenWrite(CachePath);
            foreach (KeyValuePair<string, AssetDescription> pair in assetDict)
            {
                guidIndex.Add(pair.Key, guidIndex.Count);
                serializedGuid.Add(pair.Key);
                serializedDependencyHash.Add(pair.Value.assetDependencyHashString);
            }

            foreach (string guid in serializedGuid)
            {
                List<int> res = new List<int>();
                foreach (string i in assetDict[guid].dependencies)
                {
                    if (guidIndex.TryGetValue(i, out var value))
                        res.Add(value);
                }

                int[] indexes = res.ToArray();
                serializedDenpendencies.Add(indexes);
            }

            BinaryFormatter bf = new BinaryFormatter();
            bf.Serialize(fs, serializedGuid);
            bf.Serialize(fs, serializedDependencyHash);
            bf.Serialize(fs, serializedDenpendencies);
        }

        public void UpdateAssetState(string guid)
        {
            if (assetDict.TryGetValue(guid, out AssetDescription ad) && ad.state != AssetState.Invalid)
            {
                if (File.Exists(ad.path))
                    ad.state = ad.assetDependencyHashString != File.GetLastWriteTime(ad.path).ToString(CultureInfo.InvariantCulture) ? AssetState.Changed : AssetState.Normal;
                else
                    ad.state = AssetState.Missing;
            }
            else if (!assetDict.TryGetValue(guid, out ad))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ad = new AssetDescription
                {
                    name = Path.GetFileNameWithoutExtension(path),
                    path = path,
                    state = AssetState.Invalid
                };
                assetDict.Add(guid, ad);
            }
        }

        public static string GetInfoByState(AssetState state)
        {
            if (state == AssetState.Changed)
                return "<color=red>缓存不匹配</color>";
            if (state == AssetState.Missing)
                return "<color=red>缓存丢失</color>";
            if (state == AssetState.Invalid)
                return "<color=yellow>缓存无效</color>";
            return "<color=green>缓存正常</color>";
        }

        private int GetRefCount(string assetGUID, AssetDescription desc, List<string> guidStack)
        {
            if (guidStack.Contains(assetGUID))
            {
                Debug.Log("有循环引用, 计数可能不准确");
                return 0;
            }

            guidStack.Add(assetGUID);
            int total = 0;
            if (assetDict.TryGetValue(assetGUID, out AssetDescription value))
            {
                if (value.references.Count > 0)
                {
                    Dictionary<string, int> cachedRefCount = new Dictionary<string, int>();
                    foreach (string refs in value.references)
                    {
                        if (!cachedRefCount.ContainsKey(refs))
                        {
                            int refCount = GetRefCount(refs, value, guidStack);
                            cachedRefCount[refs] = refCount;
                            total += refCount;
                        }
                    }
                }
                else
                {
                    total = 0;
                    if (desc != null)
                    {
                        string guid = AssetDatabase.AssetPathToGUID(desc.path);
                        foreach (string deps in value.dependencies)
                        {
                            if (guid == deps)
                                total++;
                        }
                    }
                }
            }

            guidStack.RemoveAt(guidStack.Count - 1);
            return total;
        }

        public void ClearCache() => _dictCache.Clear();

        public string GetRefCount(AssetDescription desc, AssetDescription parentDesc)
        {
            if (_dictCache.TryGetValue((desc, parentDesc), out int total))
                return total.ToString();
            string rootGUID = AssetDatabase.AssetPathToGUID(desc.path);
            List<string> guidInStack = new List<string> { rootGUID };
            Dictionary<string, int> cachedRefCount = new Dictionary<string, int>();
            foreach (string refs in desc.references)
            {
                if (!cachedRefCount.ContainsKey(refs))
                {
                    int refCount = GetRefCount(refs, desc, guidInStack);
                    cachedRefCount[refs] = refCount;
                    total += refCount;
                }
            }

            if (desc.references.Count == 0 && parentDesc != null)
            {
                string guid = AssetDatabase.AssetPathToGUID(desc.path);
                foreach (string refs in parentDesc.references)
                {
                    if (refs == guid)
                        total++;
                }
            }

            guidInStack.RemoveAt(guidInStack.Count - 1);
            _dictCache.Add((desc, parentDesc), total);
            return total.ToString();
        }

        internal sealed class AssetDescription
        {
            public string assetDependencyHashString;
            public List<string> dependencies = new List<string>();
            public string name = "";
            public string path = "";
            public List<string> references = new List<string>();
            public AssetState state = AssetState.Normal;
        }
    }
}
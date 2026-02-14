using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace TEngine.Editor
{
    internal sealed class ResourceReferenceInfo : EditorWindow
    {
        private const string IS_DEPEND_PREF_KEY = "ReferenceFinderData_IsDepend";
        public static readonly ReferenceFinderData Data = new ReferenceFinderData();
        private static bool _initializedData;

        [SerializeField]
        private TreeViewState _treeViewState;

        public bool needUpdateAssetTree;
        public bool needUpdateState = true;
        public List<string> selectedAssetGuid = new List<string>();
        private readonly HashSet<string> _brotherAssetIsAdd = new HashSet<string>();
        private readonly HashSet<string> _parentAssetIsAdd = new HashSet<string>();
        private readonly HashSet<string> _updatedAssetSet = new HashSet<string>();
        private Dictionary<string, ListInfo> _artInfo = new Dictionary<string, ListInfo>();
        private bool _initializedGUIStyle;
        private bool _isDepend;
        private GUIStyle _toolbarButtonGUIStyle;
        private GUIStyle _toolbarGUIStyle;
        public AssetTreeView mAssetTreeView;

        private void OnEnable() => _isDepend = PlayerPrefs.GetInt(IS_DEPEND_PREF_KEY, 0) == 1;

        private void OnGUI()
        {
            UpdateDragAssets();
            InitGUIStyleIfNeeded();
            DrawOptionBar();
            UpdateAssetTree();
            mAssetTreeView?.OnGUI(new Rect(0, _toolbarGUIStyle.fixedHeight, position.width, position.height - _toolbarGUIStyle.fixedHeight));
        }

        [MenuItem("TEngine/查找资产引用 _F10", false, 100)]
        public static void FindRef()
        {
            InitDataIfNeeded();
            OpenWindow();
            ResourceReferenceInfo window = GetWindow<ResourceReferenceInfo>();
            window.UpdateSelectedAssets();
        }

        private static void OpenWindow()
        {
            ResourceReferenceInfo window = GetWindow<ResourceReferenceInfo>();
            window.wantsMouseMove = false;
            window.titleContent = new GUIContent("查找资产引用");
            window.Show();
            window.Focus();
            SortHelper.Init();
        }

        private static void InitDataIfNeeded()
        {
            if (!_initializedData)
            {
                if (!Data.ReadFromCache())
                    Data.CollectDependenciesInfo();
                _initializedData = true;
            }
        }

        private void InitGUIStyleIfNeeded()
        {
            if (!_initializedGUIStyle)
            {
                _toolbarButtonGUIStyle = new GUIStyle("ToolbarButton");
                _toolbarGUIStyle = new GUIStyle("Toolbar");
                _initializedGUIStyle = true;
            }
        }

        private void UpdateSelectedAssets()
        {
            _artInfo = new Dictionary<string, ListInfo>();
            selectedAssetGuid.Clear();
            foreach (Object obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (Directory.Exists(path))
                {
                    string[] folder = { path };
                    string[] guids = AssetDatabase.FindAssets(null, folder);
                    foreach (string guid in guids)
                    {
                        if (!selectedAssetGuid.Contains(guid) && !Directory.Exists(AssetDatabase.GUIDToAssetPath(guid)))
                            selectedAssetGuid.Add(guid);
                    }
                }
                else
                {
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    selectedAssetGuid.Add(guid);
                }
            }

            needUpdateAssetTree = true;
        }

        private void UpdateDragAssets()
        {
            if (mouseOverWindow)
            {
                Object[] tempObj = DragAreaGetObject.GetObjects();
                if (tempObj != null)
                {
                    InitDataIfNeeded();
                    selectedAssetGuid.Clear();
                    foreach (Object obj in tempObj)
                    {
                        string path = AssetDatabase.GetAssetPath(obj);
                        if (Directory.Exists(path))
                        {
                            string[] folder = { path };
                            string[] guids = AssetDatabase.FindAssets(null, folder);
                            foreach (string guid in guids)
                            {
                                if (!selectedAssetGuid.Contains(guid) && !Directory.Exists(AssetDatabase.GUIDToAssetPath(guid)))
                                    selectedAssetGuid.Add(guid);
                            }
                        }
                        else
                        {
                            string guid = AssetDatabase.AssetPathToGUID(path);
                            selectedAssetGuid.Add(guid);
                        }
                    }

                    needUpdateAssetTree = true;
                }
            }
        }

        private void UpdateAssetTree()
        {
            if (needUpdateAssetTree && selectedAssetGuid.Count != 0)
            {
                AssetViewItem root = SelectedAssetGuidToRootItem(selectedAssetGuid);
                if (mAssetTreeView == null)
                {
                    if (_treeViewState == null)
                        _treeViewState = new TreeViewState();
                    MultiColumnHeaderState headerState = AssetTreeView.CreateDefaultMultiColumnHeaderState(position.width, _isDepend);
                    ClickColumn multiColumnHeader = new ClickColumn(headerState);
                    mAssetTreeView = new AssetTreeView(_treeViewState, multiColumnHeader);
                }
                else
                {
                    MultiColumnHeaderState headerState = AssetTreeView.CreateDefaultMultiColumnHeaderState(position.width, _isDepend);
                    ClickColumn multiColumnHeader = new ClickColumn(headerState);
                    mAssetTreeView.multiColumnHeader = multiColumnHeader;
                }

                mAssetTreeView.assetRoot = root;
                mAssetTreeView.Reload();
                needUpdateAssetTree = false;
                int totalPrefab = 0;
                int totalMat = 0;
                string prefabName = "";
                string matName = "";
                StringBuilder sb = new StringBuilder();
                if (_artInfo.Count > 0)
                {
                    foreach (KeyValuePair<string, ListInfo> kv in _artInfo)
                    {
                        if (kv.Value.Type == "prefab")
                        {
                            totalPrefab += kv.Value.Count;
                            prefabName += kv.Value.Name + "<--->";
                        }

                        if (kv.Value.Type == "mat")
                        {
                            totalMat += kv.Value.Count;
                            matName += kv.Value.Name + "<--->";
                        }

                        string tempInfo = $"name  <color=green>[{kv.Key}]</color>, type: <color=orange>[{kv.Value.Type}]</color>, count: <color=red>[{kv.Value.Count}]</color>";
                        sb.AppendLine(tempInfo);
                    }
                }

                if (totalPrefab > 0)
                    sb.Insert(0, $"预制体总数  <color=red>[{totalPrefab}]</color>  预制体详情  <color=green>[{prefabName}]</color> \r\n");
                if (totalMat > 0)
                    sb.Insert(0, $"材质总数  <color=red>[{totalMat}]</color>  材质详情  <color=green>[{matName}]</color>  \r\n");
                string str = sb.ToString();
                if (!string.IsNullOrEmpty(str))
                    Debug.Log(str);
            }
        }

        public void DrawOptionBar()
        {
            EditorGUILayout.BeginHorizontal(_toolbarGUIStyle);
            if (GUILayout.Button("点击更新本地缓存", _toolbarButtonGUIStyle))
            {
                Data.CollectDependenciesInfo();
                needUpdateAssetTree = true;
                GUIUtility.ExitGUI();
            }

            bool preIsDepend = _isDepend;
            _isDepend = GUILayout.Toggle(_isDepend, _isDepend ? "依赖模式" : "引用模式", _toolbarButtonGUIStyle, GUILayout.Width(100));
            if (preIsDepend != _isDepend)
                OnModelSelect();
            if (GUILayout.Button("展开", _toolbarButtonGUIStyle))
                mAssetTreeView?.ExpandAll();
            if (GUILayout.Button("折叠", _toolbarButtonGUIStyle))
                mAssetTreeView?.CollapseAll();
            EditorGUILayout.EndHorizontal();
        }

        private void OnModelSelect()
        {
            needUpdateAssetTree = true;
            PlayerPrefs.SetInt(IS_DEPEND_PREF_KEY, _isDepend ? 1 : 0);
            UpdateAssetTree();
        }

        private AssetViewItem SelectedAssetGuidToRootItem(List<string> inputSelectedAssetGuid)
        {
            _updatedAssetSet.Clear();
            _parentAssetIsAdd.Clear();
            _brotherAssetIsAdd.Clear();
            int elementCount = 0;
            AssetViewItem root = new AssetViewItem { id = elementCount, depth = -1, displayName = "Root", data = null };
            const int depth = 0;
            foreach (string childGuid in inputSelectedAssetGuid)
            {
                AssetViewItem rs = CreateTree(childGuid, ref elementCount, depth);
                root.AddChild(rs);
            }

            _updatedAssetSet.Clear();
            return root;
        }

        private AssetViewItem CreateTree(string guid, ref int elementCount, int depth)
        {
            if (_parentAssetIsAdd.Contains(guid))
                return null;
            if (needUpdateState && !_updatedAssetSet.Contains(guid))
            {
                Data.UpdateAssetState(guid);
                _updatedAssetSet.Add(guid);
            }

            ++elementCount;
            ReferenceFinderData.AssetDescription referenceData = Data.assetDict[guid];
            AssetViewItem root = new AssetViewItem { id = elementCount, displayName = referenceData.name, data = referenceData, depth = depth };
            List<string> childGuids = _isDepend ? referenceData.dependencies : referenceData.references;
            _parentAssetIsAdd.Add(guid);
            foreach (string childGuid in childGuids)
            {
                if (_brotherAssetIsAdd.Contains(childGuid)) continue;
                ListInfo listInfo = new ListInfo();
                if (AssetDatabase.GUIDToAssetPath(childGuid).EndsWith(".mat") && depth < 2)
                {
                    listInfo.Type = "mat";
                    listInfo.Count = 1;
                    listInfo.Name = Path.GetFileName(AssetDatabase.GUIDToAssetPath(childGuid));
                    if (!_artInfo.TryAdd(root.displayName, listInfo))
                    {
                        _artInfo[root.displayName].Count += 1;
                        _artInfo[root.displayName].Name += "<<==>>" + listInfo.Name;
                    }
                }

                if (AssetDatabase.GUIDToAssetPath(childGuid).EndsWith(".prefab") && !AssetDatabase.GUIDToAssetPath(childGuid).Contains("_gen_render") && depth < 2)
                {
                    listInfo.Type = "prefab";
                    listInfo.Count = 1;
                    listInfo.Name = Path.GetFileName(AssetDatabase.GUIDToAssetPath(childGuid));
                    if (!_artInfo.TryAdd(root.displayName, listInfo))
                    {
                        _artInfo[root.displayName].Count += 1;
                        _artInfo[root.displayName].Name += "<<==>>" + listInfo.Name;
                    }
                }

                _brotherAssetIsAdd.Add(childGuid);
                AssetViewItem rs = CreateTree(childGuid, ref elementCount, depth + 1);
                if (rs != null)
                    root.AddChild(rs);
            }

            foreach (string childGuid in childGuids)
            {
                if (_brotherAssetIsAdd.Contains(childGuid))
                    _brotherAssetIsAdd.Remove(childGuid);
            }

            _parentAssetIsAdd.Remove(guid);
            return root;
        }
    }
}
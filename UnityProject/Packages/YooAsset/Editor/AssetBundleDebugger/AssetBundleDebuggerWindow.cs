#if UNITY_2019_4_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor.Networking.PlayerConnection;
using UnityEngine.Networking.PlayerConnection;

namespace YooAsset.Editor
{
    public class AssetBundleDebuggerWindow : EditorWindow
    {
        [MenuItem("YooAsset/AssetBundle Debugger", false, 104)]
        public static void OpenWindow()
        {
            AssetBundleDebuggerWindow wnd = GetWindow<AssetBundleDebuggerWindow>("AssetBundle Debugger", true, WindowsDefine.DockedWindowTypes);
            wnd.minSize = new Vector2(800, 600);
        }

        /// <summary>
        /// 视图模式
        /// </summary>
        private enum EViewMode
        {
            /// <summary>
            /// 内存视图
            /// </summary>
            MemoryView,

            /// <summary>
            /// 资源对象视图
            /// </summary>
            AssetView,

            /// <summary>
            /// 资源包视图
            /// </summary>
            BundleView,

            /// <summary>
            /// 异步操作视图
            /// </summary>
            OperationView,
        }


        private readonly Dictionary<int, RemotePlayerSession> _playerSessions = new Dictionary<int, RemotePlayerSession>();

        private ToolbarButton _playerName;
        private ToolbarMenu _viewModeMenu;
        private SliderInt _frameSlider;
        private DebuggerAssetListViewer _assetListViewer;
        private DebuggerBundleListViewer _bundleListViewer;
        private DebuggerOperationListViewer _operationListViewer;

        private EViewMode _viewMode;
        private string _searchKeyWord;
        private DebugReport _currentReport;
        private RemotePlayerSession _currentPlayerSession;

        private double _lastRepaintTime = 0;
        private int _nextRepaintIndex = -1;
        private int _lastRepaintIndex = 0;
        private int _rangeIndex = 0;


        public void CreateGUI()
        {
            try
            {
                VisualElement root = rootVisualElement;

                // 加载布局文件
                var visualAsset = UxmlLoader.LoadWindowUXML<AssetBundleDebuggerWindow>();
                if (visualAsset == null)
                    return;

                visualAsset.CloneTree(root);

                // 采样按钮
                var sampleBtn = root.Q<Button>("SampleButton");
                sampleBtn.clicked += SampleBtn_onClick;

                // 导出按钮
                var exportBtn = root.Q<Button>("ExportButton");
                exportBtn.clicked += ExportBtn_clicked;

                // 用户列表菜单
                _playerName = root.Q<ToolbarButton>("PlayerName");
                _playerName.text = "Editor player";

                // 视口模式菜单
                _viewModeMenu = root.Q<ToolbarMenu>("ViewModeMenu");
                _viewModeMenu.menu.AppendAction(EViewMode.AssetView.ToString(), OnViewModeMenuChange, OnViewModeMenuStatusUpdate, EViewMode.AssetView);
                _viewModeMenu.menu.AppendAction(EViewMode.BundleView.ToString(), OnViewModeMenuChange, OnViewModeMenuStatusUpdate, EViewMode.BundleView);
                _viewModeMenu.menu.AppendAction(EViewMode.OperationView.ToString(), OnViewModeMenuChange, OnViewModeMenuStatusUpdate, EViewMode.OperationView);
                _viewModeMenu.text = EViewMode.AssetView.ToString();

                // 搜索栏
                var searchField = root.Q<ToolbarSearchField>("SearchField");
                searchField.RegisterValueChangedCallback(OnSearchKeyWordChange);

                // 帧数相关
                {
                    _frameSlider = root.Q<SliderInt>("FrameSlider");
                    _frameSlider.label = "Frame:";
                    _frameSlider.highValue = 0;
                    _frameSlider.lowValue = 0;
                    _frameSlider.value = 0;
                    _frameSlider.RegisterValueChangedCallback(evt =>
                    {
                        OnFrameSliderChange(evt.newValue);
                    });

                    var frameLast = root.Q<ToolbarButton>("FrameLast");
                    frameLast.clicked += OnFrameLast_clicked;

                    var frameNext = root.Q<ToolbarButton>("FrameNext");
                    frameNext.clicked += OnFrameNext_clicked;

                    var frameClear = root.Q<ToolbarButton>("FrameClear");
                    frameClear.clicked += OnFrameClear_clicked;

                    var recorderToggle = root.Q<ToggleRecord>("FrameRecord");
                    recorderToggle.RegisterValueChangedCallback(OnRecordToggleValueChange);
                }

                // 加载视图
                _assetListViewer = new DebuggerAssetListViewer();
                _assetListViewer.InitViewer();

                // 加载视图
                _bundleListViewer = new DebuggerBundleListViewer();
                _bundleListViewer.InitViewer();

                // 加载视图
                _operationListViewer = new DebuggerOperationListViewer();
                _operationListViewer.InitViewer();

                // 显示视图
                _viewMode = EViewMode.AssetView;
                _assetListViewer.AttachParent(root);

                // 远程调试
                EditorConnection.instance.Initialize();
                EditorConnection.instance.RegisterConnection(OnHandleConnectionEvent);
                EditorConnection.instance.RegisterDisconnection(OnHandleDisconnectionEvent);
                EditorConnection.instance.Register(RemoteDebuggerDefine.kMsgPlayerSendToEditor, OnHandlePlayerMessage);
                RemoteEditorConnection.Instance.Initialize();
                RemoteEditorConnection.Instance.Register(RemoteDebuggerDefine.kMsgPlayerSendToEditor, OnHandlePlayerMessage);
            }
            catch (Exception e)
            {
                Debug.LogError(e.ToString());
            }
        }
        public void OnDestroy()
        {
            // 远程调试
            EditorConnection.instance.UnregisterConnection(OnHandleConnectionEvent);
            EditorConnection.instance.UnregisterDisconnection(OnHandleDisconnectionEvent);
            EditorConnection.instance.Unregister(RemoteDebuggerDefine.kMsgPlayerSendToEditor, OnHandlePlayerMessage);
            RemoteEditorConnection.Instance.Unregister(RemoteDebuggerDefine.kMsgPlayerSendToEditor);
            _playerSessions.Clear();
        }
        public void Update()
        {
            // 每间隔1秒绘制一次页面
            if (EditorApplication.timeSinceStartup - _lastRepaintTime > 1f)
            {
                _lastRepaintTime = EditorApplication.timeSinceStartup;
                if (_nextRepaintIndex >= 0)
                {
                    RepaintFrame(_nextRepaintIndex);
                    _nextRepaintIndex = -1;
                }
            }
        }

        private void OnHandleConnectionEvent(int playerId)
        {
            Debug.Log($"Game player connection : {playerId}");
            _playerName.text = $"Connected player : {playerId}";
        }
        private void OnHandleDisconnectionEvent(int playerId)
        {
            Debug.Log($"Game player disconnection : {playerId}");
            _playerName.text = $"Disconneced player : {playerId}";
        }
        private void OnHandlePlayerMessage(MessageEventArgs args)
        {
            int playerId = args.playerId;
            var debugReport = DebugReport.Deserialize(args.data);

            if (debugReport.DebuggerVersion != RemoteDebuggerDefine.DebuggerVersion)
            {
                Debug.LogWarning($"Debugger versions are inconsistent : {debugReport.DebuggerVersion} != {RemoteDebuggerDefine.DebuggerVersion}");
                return;
            }

            //Debug.Log($"Handle player {playerId} debug report !");
            _currentPlayerSession = GetOrCreatePlayerSession(playerId);
            _currentPlayerSession.AddDebugReport(debugReport);
            _nextRepaintIndex = _currentPlayerSession.MaxRangeValue;
        }

        private void OnFrameSliderChange(int sliderValue)
        {
            if (_currentPlayerSession != null)
            {
                _rangeIndex = _currentPlayerSession.ClampRangeIndex(sliderValue); ;
                RepaintFrame(_rangeIndex);
            }
        }
        private void OnFrameLast_clicked()
        {
            if (_currentPlayerSession != null)
            {
                _rangeIndex = _currentPlayerSession.ClampRangeIndex(_rangeIndex - 1);
                _frameSlider.value = _rangeIndex;
                RepaintFrame(_rangeIndex);
            }
        }
        private void OnFrameNext_clicked()
        {
            if (_currentPlayerSession != null)
            {
                _rangeIndex = _currentPlayerSession.ClampRangeIndex(_rangeIndex + 1);
                _frameSlider.value = _rangeIndex;
                RepaintFrame(_rangeIndex);
            }
        }
        private void OnFrameClear_clicked()
        {
            _nextRepaintIndex = -1;
            _lastRepaintIndex = 0;
            _rangeIndex = 0;

            _frameSlider.label = $"Frame:";
            _frameSlider.value = 0;
            _frameSlider.lowValue = 0;
            _frameSlider.highValue = 0;
            _assetListViewer.ClearView();
            _bundleListViewer.ClearView();
            _operationListViewer.ClearView();

            if (_currentPlayerSession != null)
            {
                _currentPlayerSession.ClearDebugReport();
            }
        }
        private void OnRecordToggleValueChange(ChangeEvent<bool> evt)
        {
            // 发送采集数据的命令
            RemoteCommand command = new RemoteCommand();
            command.CommandType = (int)ERemoteCommand.SampleAuto;
            command.CommandParam = evt.newValue ? "open" : "close";
            byte[] data = RemoteCommand.Serialize(command);
            EditorConnection.instance.Send(RemoteDebuggerDefine.kMsgEditorSendToPlayer, data);
            RemoteEditorConnection.Instance.Send(RemoteDebuggerDefine.kMsgEditorSendToPlayer, data);
        }

        private void SampleBtn_onClick()
        {
            // 发送采集数据的命令
            RemoteCommand command = new RemoteCommand();
            command.CommandType = (int)ERemoteCommand.SampleOnce;
            command.CommandParam = string.Empty;
            byte[] data = RemoteCommand.Serialize(command);
            EditorConnection.instance.Send(RemoteDebuggerDefine.kMsgEditorSendToPlayer, data);
            RemoteEditorConnection.Instance.Send(RemoteDebuggerDefine.kMsgEditorSendToPlayer, data);
        }
        private void ExportBtn_clicked()
        {
            if (_currentReport == null)
            {
                Debug.LogWarning("Debug report is null.");
                return;
            }

            string resultPath = EditorTools.OpenFolderPanel("Export JSON", "Assets/");
            if (resultPath != null)
            {
                // 注意：排序保证生成配置的稳定性
                foreach (var packageData in _currentReport.PackageDatas)
                {
                    packageData.ProviderInfos.Sort();
                    foreach (var providerInfo in packageData.ProviderInfos)
                    {
                        providerInfo.DependBundles.Sort();
                    }
                }

                string filePath = $"{resultPath}/{nameof(DebugReport)}_{_currentReport.FrameCount}.json";
                string fileContent = JsonUtility.ToJson(_currentReport, true);
                FileUtility.WriteAllText(filePath, fileContent);
                Debug.Log($"Debug report file saved : {filePath}");
            }
        }
        private void OnSearchKeyWordChange(ChangeEvent<string> e)
        {
            _searchKeyWord = e.newValue;
            if (_currentReport != null)
            {
                _assetListViewer.RebuildView(_searchKeyWord);
                _bundleListViewer.RebuildView(_searchKeyWord);
                _operationListViewer.RebuildView(_searchKeyWord);
            }
        }
        private void OnViewModeMenuChange(DropdownMenuAction action)
        {
            var viewMode = (EViewMode)action.userData;
            if (_viewMode != viewMode)
            {
                _viewMode = viewMode;
                VisualElement root = this.rootVisualElement;
                _viewModeMenu.text = viewMode.ToString();

                if (viewMode == EViewMode.AssetView)
                {
                    _assetListViewer.AttachParent(root);
                    _bundleListViewer.DetachParent();
                    _operationListViewer.DetachParent();
                }
                else if (viewMode == EViewMode.BundleView)
                {
                    _assetListViewer.DetachParent();
                    _bundleListViewer.AttachParent(root);
                    _operationListViewer.DetachParent();
                }
                else if (viewMode == EViewMode.OperationView)
                {
                    _assetListViewer.DetachParent();
                    _bundleListViewer.DetachParent();
                    _operationListViewer.AttachParent(root);
                }
                else
                {
                    throw new NotImplementedException(viewMode.ToString());
                }

                // 重新绘制该帧数据
                RepaintFrame(_lastRepaintIndex);
            }
        }
        private DropdownMenuAction.Status OnViewModeMenuStatusUpdate(DropdownMenuAction action)
        {
            var viewMode = (EViewMode)action.userData;
            if (_viewMode == viewMode)
                return DropdownMenuAction.Status.Checked;
            else
                return DropdownMenuAction.Status.Normal;
        }

        private RemotePlayerSession GetOrCreatePlayerSession(int playerId)
        {
            if (_playerSessions.TryGetValue(playerId, out RemotePlayerSession session))
            {
                return session;
            }
            else
            {
                RemotePlayerSession newSession = new RemotePlayerSession(playerId);
                _playerSessions.Add(playerId, newSession);
                return newSession;
            }
        }
        private void RepaintFrame(int repaintIndex)
        {
            if (_currentPlayerSession == null)
            {
                _assetListViewer.ClearView();
                _bundleListViewer.ClearView();
                _operationListViewer.ClearView();
                return;
            }

            var debugReport = _currentPlayerSession.GetDebugReport(repaintIndex);
            if (debugReport != null)
            {
                _lastRepaintIndex = repaintIndex;
                _currentReport = debugReport;
                _frameSlider.label = $"Frame: {debugReport.FrameCount}";
                _frameSlider.highValue = _currentPlayerSession.MaxRangeValue;
                _frameSlider.value = repaintIndex;

                if (_viewMode == EViewMode.AssetView)
                {
                    _assetListViewer.FillViewData(debugReport);
                    _bundleListViewer.ClearView();
                    _operationListViewer.ClearView();
                }
                else if (_viewMode == EViewMode.BundleView)
                {
                    _assetListViewer.ClearView();
                    _bundleListViewer.FillViewData(debugReport);
                    _operationListViewer.ClearView();
                }
                else if (_viewMode == EViewMode.OperationView)
                {
                    _assetListViewer.ClearView();
                    _bundleListViewer.ClearView();
                    _operationListViewer.FillViewData(debugReport);
                }
                else
                {
                    throw new System.NotImplementedException(_viewMode.ToString());
                }
            }
        }
    }
}
#endif
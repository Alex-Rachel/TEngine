using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace TEngine.Editor.Inspector
{
    [CustomEditor(typeof(ResourceModuleDriver))]
    internal sealed class ResourceModuleDriverInspector : GameFrameworkInspector
    {
        private static readonly string[] m_playModeNames = new string[]
        {
            "EditorSimulateMode (编辑器下的模拟模式)",
            "OfflinePlayMode (单机模式)",
            "HostPlayMode (联机运行模式)",
            "WebGLPlayMode (WebGL运行模式)"
        };

        private static readonly string[] m_encryptionNames = new string[]
        {
            "无加密",
            "文件偏移加密",
            "文件流加密",
        };

        private SerializedProperty m_playMode;
        private SerializedProperty m_encryptionType;
        private SerializedProperty m_updatableWhilePlaying;
        private SerializedProperty m_milliseconds;
        private SerializedProperty m_autoUnloadBundleWhenUnused;
        private SerializedProperty m_minUnloadUnusedAssetsInterval;
        private SerializedProperty m_maxUnloadUnusedAssetsInterval;
        private SerializedProperty m_useSystemUnloadUnusedAssets;
        private SerializedProperty m_assetAutoReleaseInterval;
        private SerializedProperty m_assetPoolCapacity;
        private SerializedProperty m_assetExpireTime;
        private SerializedProperty m_assetPoolPriority;
        private SerializedProperty m_failedTryAgain;
        private SerializedProperty m_packageName;
        private SerializedProperty m_downloadingMaxNum;
        private int m_playModeIndex;
        private int m_packageNameIndex;
        private int m_encryptionNameIndex;
        private string[] m_packageNames;

        // UI状态
        private Vector2 m_scrollPosition;
        private bool m_showBasicSettings = true;
        private bool m_showResourcePoolSettings = true;
        private bool m_showDownloadSettings = true;
        private bool m_showAdvancedSettings = true;
        private bool m_showRuntimeInfo = true;

        // 颜色定义
        private Color m_headerColor = new Color(0.1f, 0.5f, 0.8f, 1f);
        private Color m_warningColor = new Color(1f, 0.6f, 0.2f, 1f);
        private Color m_successColor = new Color(0.2f, 0.8f, 0.3f, 1f);

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();

            ResourceModuleDriver t = (ResourceModuleDriver)target;

            // 绘制标题区域
            DrawInspectorHeader();

            EditorGUI.BeginDisabledGroup(EditorApplication.isPlayingOrWillChangePlaymode);
            {
                DrawBasicSettings(t);
                DrawResourcePoolSettings(t);
                DrawDownloadSettings(t);
                DrawAdvancedSettings(t);
            }
            EditorGUI.EndDisabledGroup();

            DrawRuntimeInfo(t);
            DrawStatistics(t);

            serializedObject.ApplyModifiedProperties();
            Repaint();
        }

        private void DrawInspectorHeader()
        {
            // 标题背景
            // Rect headerRect = EditorGUILayout.GetControlRect(false, 50);
            // EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, position.width, 50),
            //     new Color(0.1f, 0.1f, 0.1f, 0.8f));

            GUILayout.Space(5);

            // 主标题
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            var titleStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            EditorGUILayout.LabelField(new GUIContent("TEngine资源模块配置", "Resource Module Configuration"),
                titleStyle, GUILayout.Height(30));

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // 副标题
            var subtitleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.8f, 0.8f, 0.8f, 1f) }
            };

            EditorGUILayout.LabelField("配置 YooAsset 资源管理系统", subtitleStyle);
            GUILayout.Space(5);

            // 分隔线
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            GUILayout.Space(10);
        }

        private void DrawBasicSettings(ResourceModuleDriver t)
        {
            m_showBasicSettings = EditorGUILayout.BeginFoldoutHeaderGroup(m_showBasicSettings,
                new GUIContent("基础设置", "资源运行模式和加密配置"));

            if (m_showBasicSettings)
            {
                EditorGUILayout.BeginVertical("HelpBox");
                {
                    // 资源运行模式
                    EditorGUILayout.LabelField("资源运行模式", EditorStyles.boldLabel);
                    if (EditorApplication.isPlaying && IsPrefabInHierarchy(t.gameObject))
                    {
                        EditorGUILayout.EnumPopup("当前模式", t.PlayMode);
                    }
                    else
                    {
                        int selectedIndex = EditorGUILayout.Popup("运行模式", m_playModeIndex, m_playModeNames);
                        if (selectedIndex != m_playModeIndex)
                        {
                            m_playModeIndex = selectedIndex;
                            m_playMode.enumValueIndex = selectedIndex;
                        }
                    }

                    EditorGUILayout.Space(5);

                    // 资源加密模式
                    EditorGUILayout.LabelField("资源加密模式", EditorStyles.boldLabel);
                    if (EditorApplication.isPlaying && IsPrefabInHierarchy(t.gameObject))
                    {
                        EditorGUILayout.EnumPopup("当前加密", t.EncryptionType);
                    }
                    else
                    {
                        int selectedIndex = EditorGUILayout.Popup("加密方式", m_encryptionNameIndex, m_encryptionNames);
                        if (selectedIndex != m_encryptionNameIndex)
                        {
                            m_encryptionNameIndex = selectedIndex;
                            m_encryptionType.enumValueIndex = selectedIndex;
                        }
                    }

                    EditorGUILayout.Space(5);

                    // 资源包名
                    EditorGUILayout.LabelField("资源包配置", EditorStyles.boldLabel);
                    m_packageNames = GetBuildPackageNames().ToArray();
                    m_packageNameIndex = Array.IndexOf(m_packageNames, m_packageName.stringValue);

                    if (m_packageNameIndex < 0)
                    {
                        m_packageNameIndex = 0;
                    }
                    m_packageNameIndex = EditorGUILayout.Popup("资源包名", m_packageNameIndex, m_packageNames);
                    if (m_packageName.stringValue != m_packageNames[m_packageNameIndex])
                    {
                        m_packageName.stringValue = m_packageNames[m_packageNameIndex];
                    }

                    EditorGUILayout.Space(3);
                    EditorGUILayout.HelpBox(GetPlayModeDescription(m_playModeIndex), MessageType.Info);
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            GUILayout.Space(8);
        }

        private void DrawResourcePoolSettings(ResourceModuleDriver t)
        {
            m_showResourcePoolSettings = EditorGUILayout.BeginFoldoutHeaderGroup(m_showResourcePoolSettings,
                new GUIContent("对象池设置", "资源对象池和内存管理配置"));

            if (m_showResourcePoolSettings)
            {
                EditorGUILayout.BeginVertical("HelpBox");
                {
                    // 资源回收设置
                    EditorGUILayout.LabelField("对象池回收设置", EditorStyles.boldLabel);

                    bool useSystemUnloadUnusedAssets = EditorGUILayout.ToggleLeft(
                        new GUIContent("使用资源模块卸载回收资源", "启用自动资源回收"),
                        m_useSystemUnloadUnusedAssets.boolValue);

                    if (useSystemUnloadUnusedAssets != m_useSystemUnloadUnusedAssets.boolValue)
                    {
                        if (EditorApplication.isPlaying)
                        {
                            t.UseSystemUnloadUnusedAssets = useSystemUnloadUnusedAssets;
                        }
                        else
                        {
                            m_useSystemUnloadUnusedAssets.boolValue = useSystemUnloadUnusedAssets;
                        }
                    }

                    if (useSystemUnloadUnusedAssets)
                    {
                        float minUnloadUnusedAssetsInterval = EditorGUILayout.Slider(
                            new GUIContent("最小回收间隔(秒)", "资源回收的最小时间间隔"),
                            m_minUnloadUnusedAssetsInterval.floatValue, 0f, 3600f);

                        if (Mathf.Abs(minUnloadUnusedAssetsInterval - m_minUnloadUnusedAssetsInterval.floatValue) > 0.01f)
                        {
                            if (EditorApplication.isPlaying)
                            {
                                t.MinUnloadUnusedAssetsInterval = minUnloadUnusedAssetsInterval;
                            }
                            else
                            {
                                m_minUnloadUnusedAssetsInterval.floatValue = minUnloadUnusedAssetsInterval;
                            }
                        }

                        float maxUnloadUnusedAssetsInterval = EditorGUILayout.Slider(
                            new GUIContent("最大回收间隔(秒)", "资源回收的最大时间间隔"),
                            m_maxUnloadUnusedAssetsInterval.floatValue, 0f, 3600f);

                        if (Mathf.Abs(maxUnloadUnusedAssetsInterval - m_maxUnloadUnusedAssetsInterval.floatValue) > 0.01f)
                        {
                            if (EditorApplication.isPlaying)
                            {
                                t.MaxUnloadUnusedAssetsInterval = maxUnloadUnusedAssetsInterval;
                            }
                            else
                            {
                                m_maxUnloadUnusedAssetsInterval.floatValue = maxUnloadUnusedAssetsInterval;
                            }
                        }
                    }

                    EditorGUILayout.Space(5);

                    // 对象池设置
                    EditorGUILayout.LabelField("对象池设置", EditorStyles.boldLabel);

                    float assetAutoReleaseInterval = EditorGUILayout.FloatField(
                        new GUIContent("自动释放间隔(秒)", "资源对象池自动释放对象的时间间隔"),
                        m_assetAutoReleaseInterval.floatValue);

                    if (Mathf.Abs(assetAutoReleaseInterval - m_assetAutoReleaseInterval.floatValue) > 0.01f)
                    {
                        if (EditorApplication.isPlaying)
                        {
                            t.AssetAutoReleaseInterval = assetAutoReleaseInterval;
                        }
                        else
                        {
                            m_assetAutoReleaseInterval.floatValue = assetAutoReleaseInterval;
                        }
                    }

                    int assetCapacity = EditorGUILayout.IntField(
                        new GUIContent("对象池容量", "资源对象池的最大容量"),
                        m_assetPoolCapacity.intValue);

                    if (assetCapacity != m_assetPoolCapacity.intValue)
                    {
                        if (EditorApplication.isPlaying)
                        {
                            t.AssetCapacity = assetCapacity;
                        }
                        else
                        {
                            m_assetPoolCapacity.intValue = assetCapacity;
                        }
                    }

                    float assetExpireTime = EditorGUILayout.FloatField(
                        new GUIContent("资源过期时间(秒)", "资源在对象池中的过期时间"),
                        m_assetExpireTime.floatValue);

                    if (Mathf.Abs(assetExpireTime - m_assetExpireTime.floatValue) > 0.01f)
                    {
                        if (EditorApplication.isPlaying)
                        {
                            t.AssetExpireTime = assetExpireTime;
                        }
                        else
                        {
                            m_assetExpireTime.floatValue = assetExpireTime;
                        }
                    }

                    int assetPoolPriority = EditorGUILayout.IntField(
                        new GUIContent("对象池优先级", "资源对象池的优先级"),
                        m_assetPoolPriority.intValue);

                    if (assetPoolPriority != m_assetPoolPriority.intValue)
                    {
                        if (EditorApplication.isPlaying)
                        {
                            t.AssetPriority = assetPoolPriority;
                        }
                        else
                        {
                            m_assetPoolPriority.intValue = assetPoolPriority;
                        }
                    }

                    EditorGUILayout.Space(3);
                    EditorGUILayout.HelpBox("合理配置资源池参数可以有效管理内存使用", MessageType.Info);
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            GUILayout.Space(8);
        }

        private void DrawDownloadSettings(ResourceModuleDriver t)
        {
            m_showDownloadSettings = EditorGUILayout.BeginFoldoutHeaderGroup(m_showDownloadSettings,
                new GUIContent("下载设置", "网络下载和重试配置"));

            if (m_showDownloadSettings)
            {
                EditorGUILayout.BeginVertical("HelpBox");
                {
                    // 边玩边下
                    bool updatableWhilePlaying = EditorGUILayout.ToggleLeft(
                        new GUIContent("允许边玩边下", "游戏运行时允许下载资源"),
                        m_updatableWhilePlaying.boolValue);

                    if (updatableWhilePlaying != m_updatableWhilePlaying.boolValue)
                    {
                        if (EditorApplication.isPlaying)
                        {
                            t.updatableWhilePlaying = updatableWhilePlaying;
                        }
                        else
                        {
                            m_updatableWhilePlaying.boolValue = updatableWhilePlaying;
                        }
                    }

                    EditorGUILayout.Space(5);

                    // 下载限制
                    int downloadingMaxNum = EditorGUILayout.IntSlider(
                        new GUIContent("最大下载数量", "同时进行的最大下载任务数"),
                        m_downloadingMaxNum.intValue, 1, 48);

                    if (downloadingMaxNum != m_downloadingMaxNum.intValue)
                    {
                        if (EditorApplication.isPlaying)
                        {
                            t.DownloadingMaxNum = downloadingMaxNum;
                        }
                        else
                        {
                            m_downloadingMaxNum.intValue = downloadingMaxNum;
                        }
                    }

                    // 重试设置
                    int failedTryAgain = EditorGUILayout.IntSlider(
                        new GUIContent("失败重试次数", "下载失败时的重试次数"),
                        m_failedTryAgain.intValue, 1, 48);

                    if (failedTryAgain != m_failedTryAgain.intValue)
                    {
                        if (EditorApplication.isPlaying)
                        {
                            t.FailedTryAgain = failedTryAgain;
                        }
                        else
                        {
                            m_failedTryAgain.intValue = failedTryAgain;
                        }
                    }

                    EditorGUILayout.Space(3);
                    EditorGUILayout.HelpBox("下载设置影响网络资源加载的效率和稳定性", MessageType.Info);
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            GUILayout.Space(8);
        }

        private void DrawAdvancedSettings(ResourceModuleDriver t)
        {
            m_showAdvancedSettings = EditorGUILayout.BeginFoldoutHeaderGroup(m_showAdvancedSettings,
                new GUIContent("YooAsset设置", "性能调优相关配置"));

            if (m_showAdvancedSettings)
            {
                EditorGUILayout.BeginVertical("HelpBox");
                {
                    int milliseconds = EditorGUILayout.IntSlider(
                        new GUIContent("异步处理帧时间限制(毫秒)", "每帧处理资源操作的最大时间"),
                        m_milliseconds.intValue, 1, 100);

                    if (milliseconds != m_milliseconds.intValue)
                    {
                        if (EditorApplication.isPlaying)
                        {
                            t.milliseconds = milliseconds;
                        }
                        else
                        {
                            m_milliseconds.intValue = milliseconds;
                        }
                    }

                    bool autoUnloadBundleWhenUnused = EditorGUILayout.ToggleLeft(
                        new GUIContent("自动释放资源引用计数为0的资源包", "自动释放资源引用计数为0的资源包"),
                        m_autoUnloadBundleWhenUnused.boolValue);

                    if (autoUnloadBundleWhenUnused != m_autoUnloadBundleWhenUnused.boolValue)
                    {
                        if (EditorApplication.isPlaying)
                        {
                            t.autoUnloadBundleWhenUnused = autoUnloadBundleWhenUnused;
                        }
                        else
                        {
                            m_autoUnloadBundleWhenUnused.boolValue = autoUnloadBundleWhenUnused;
                        }
                    }

                    EditorGUILayout.Space(3);
                    string tips = $"每帧最多处理 {milliseconds}ms 的资源操作，避免卡顿\n" +
                                  $"自动释放资源引用计数为0的资源包: {(m_autoUnloadBundleWhenUnused.boolValue ? "启用" : "禁用")}";
                    EditorGUILayout.HelpBox(tips, MessageType.Info);
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            GUILayout.Space(8);
        }

        private void DrawRuntimeInfo(ResourceModuleDriver t)
        {
            if (EditorApplication.isPlaying && IsPrefabInHierarchy(t.gameObject))
            {
                m_showRuntimeInfo = EditorGUILayout.BeginFoldoutHeaderGroup(m_showRuntimeInfo,
                    new GUIContent("运行时信息", "游戏运行时的资源状态"));

                if (m_showRuntimeInfo)
                {
                    EditorGUILayout.BeginVertical("HelpBox");
                    {
                        EditorGUILayout.BeginHorizontal();
                        {
                            EditorGUILayout.LabelField("资源回收状态:", GUILayout.Width(100));
                            EditorGUILayout.LabelField(
                                Utility.Text.Format("{0:F2} / {1:F2}",
                                    t.LastUnloadUnusedAssetsOperationElapseSeconds,
                                    t.MaxUnloadUnusedAssetsInterval),
                                EditorStyles.miniLabel);
                        }
                        EditorGUILayout.EndHorizontal();

                        EditorGUILayout.BeginHorizontal();
                        {
                            EditorGUILayout.LabelField("游戏版本:", GUILayout.Width(100));
                            EditorGUILayout.LabelField(
                                !string.IsNullOrEmpty(t.ApplicableGameVersion) ? t.ApplicableGameVersion : "<Unknown>",
                                EditorStyles.miniLabel);
                        }
                        EditorGUILayout.EndHorizontal();

                        EditorGUILayout.Space(3);
                        EditorGUILayout.HelpBox("实时监控资源使用状态", MessageType.Info);
                    }
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndFoldoutHeaderGroup();
                GUILayout.Space(8);
            }
        }

        private void DrawStatistics(ResourceModuleDriver t)
        {
            EditorGUILayout.BeginVertical("Box");
            {
                EditorGUILayout.LabelField("配置概览", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("运行模式:", GUILayout.Width(80));
                    string modeName = m_playModeIndex < m_playModeNames.Length
                        ? m_playModeNames[m_playModeIndex].Split(' ')[0]
                        : "未知";
                    EditorGUILayout.LabelField(modeName, EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("资源包:", GUILayout.Width(80));
                    EditorGUILayout.LabelField(m_packageName.stringValue, EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("边玩边下:", GUILayout.Width(80));
                    EditorGUILayout.LabelField(m_updatableWhilePlaying.boolValue ? "启用" : "禁用", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("自动回收:", GUILayout.Width(80));
                    EditorGUILayout.LabelField(m_useSystemUnloadUnusedAssets.boolValue ? "启用" : "禁用",
                        EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("YooAsset自动释放资源引用计数为0的资源包:", GUILayout.Width(80));
                    EditorGUILayout.LabelField(m_autoUnloadBundleWhenUnused.boolValue ? "启用" : "禁用",
                        EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();

                // 操作按钮
                EditorGUILayout.Space(5);
                EditorGUI.BeginDisabledGroup(EditorApplication.isPlayingOrWillChangePlaymode);
                {
                    EditorGUILayout.BeginHorizontal();
                    {
                        if (GUILayout.Button("刷新配置", GUILayout.Height(25)))
                        {
                            RefreshTypeNames();
                            RefreshPlayModeNames();
                        }

                        if (GUILayout.Button("保存配置", GUILayout.Height(25)))
                        {
                            serializedObject.ApplyModifiedProperties();
                            Debug.Log("资源模块配置已保存");
                        }
                    }
                    EditorGUI.EndDisabledGroup();
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
            }
        }

        private string GetPlayModeDescription(int modeIndex)
        {
            switch (modeIndex)
            {
                case 0: return "编辑器模拟模式 - 适合开发调试";
                case 1: return "单机模式 - 适合离线游戏";
                case 2: return "联机运行模式 - 适合网络游戏";
                case 3: return "WebGL运行模式 - 适合网页游戏";
                default: return "未知运行模式";
            }
        }

        protected override void OnCompileComplete()
        {
            base.OnCompileComplete();
            RefreshTypeNames();
        }

        private List<string> GetBuildPackageNames()
        {
            List<string> packageNames = new List<string>();

            foreach (var package in AssetBundleCollectorSettingData.Setting.Packages)
            {
                packageNames.Add(package.PackageName);
            }
            return packageNames;
        }

        private void OnEnable()
        {
            m_playMode = serializedObject.FindProperty("playMode");
            m_encryptionType = serializedObject.FindProperty("encryptionType");
            m_updatableWhilePlaying = serializedObject.FindProperty("updatableWhilePlaying");
            m_milliseconds = serializedObject.FindProperty("milliseconds");
            m_autoUnloadBundleWhenUnused = serializedObject.FindProperty("autoUnloadBundleWhenUnused");
            m_minUnloadUnusedAssetsInterval = serializedObject.FindProperty("minUnloadUnusedAssetsInterval");
            m_maxUnloadUnusedAssetsInterval = serializedObject.FindProperty("maxUnloadUnusedAssetsInterval");
            m_useSystemUnloadUnusedAssets = serializedObject.FindProperty("useSystemUnloadUnusedAssets");
            m_assetAutoReleaseInterval = serializedObject.FindProperty("assetAutoReleaseInterval");
            m_assetPoolCapacity = serializedObject.FindProperty("assetCapacity");
            m_assetExpireTime = serializedObject.FindProperty("assetExpireTime");
            m_assetPoolPriority = serializedObject.FindProperty("assetPriority");
            m_failedTryAgain = serializedObject.FindProperty("failedTryAgain");
            m_packageName = serializedObject.FindProperty("packageName");
            m_downloadingMaxNum = serializedObject.FindProperty("downloadingMaxNum");

            RefreshPlayModeNames();
            RefreshTypeNames();
        }

        private void RefreshTypeNames()
        {
            serializedObject.ApplyModifiedProperties();
        }

        private void RefreshPlayModeNames()
        {
            m_playModeIndex = m_playMode.enumValueIndex > 0 ? m_playMode.enumValueIndex : 0;
            m_encryptionNameIndex = m_encryptionType.enumValueIndex > 0 ? m_encryptionType.enumValueIndex : 0;
        }
    }
}
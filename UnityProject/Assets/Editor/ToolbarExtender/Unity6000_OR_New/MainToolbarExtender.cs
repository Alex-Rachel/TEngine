#if UNITY_6000_3_OR_NEWER

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public class MainToolbarInitializeOnLoad
{
    static MainToolbarInitializeOnLoad()
    {
        MainToolbarSceneLauncherButton.Init();
        MainToolbarDropdownSceneSelector.Init();
        MainToolbarDropdownPlayMode.Init();
    }
}

public class MainToolbarSceneLauncherButton
{
    private const string PreviousSceneKey = "TEngine_PreviousScenePath"; // 用于存储之前场景路径的键
    private const string IsLauncherBtn = "TEngine_IsLauncher"; // 用于存储之前是否按下launcher

    private static readonly string SceneMain = "main";

    [MainToolbarElement("TEngine/Scene Launcher Button", defaultDockIndex = -10, defaultDockPosition = MainToolbarDockPosition.Middle)]
    private static MainToolbarElement ProjectSettingsButton()
    {
        var onIcon = EditorGUIUtility.IconContent("PlayButton").image as Texture2D;
        var offIcon = EditorGUIUtility.IconContent("StopButton").image as Texture2D;
        var icon = !EditorApplication.isPlaying ? onIcon : offIcon;
        var content = new MainToolbarContent("Launcher", icon, "");
        var launcherBtn = new MainToolbarButton(content, () => { SceneHelper.StartScene(SceneMain); })
        {
            displayed = true
        };
        return launcherBtn;
    }

    public static void Init()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.quitting -= OnEditorQuit;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.quitting += OnEditorQuit;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        ProjectSettingsButton();
        MainToolbar.Refresh("TEngine/Scene Launcher Button");
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            // 从 EditorPrefs 读取之前的场景路径 并恢复之前的场景
            var previousScenePath = EditorPrefs.GetString(PreviousSceneKey, string.Empty);
            if (!string.IsNullOrEmpty(previousScenePath) && EditorPrefs.GetBool(IsLauncherBtn))
            {
                EditorApplication.delayCall += () =>
                {
                    if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    {
                        EditorSceneManager.OpenScene(previousScenePath);
                    }
                };
            }

            EditorPrefs.SetBool(IsLauncherBtn, false);
        }
    }

    private static void OnEditorQuit()
    {
        EditorPrefs.SetString(PreviousSceneKey, "");
        EditorPrefs.SetBool(IsLauncherBtn, false);
    }

    private static class SceneHelper
    {
        private static string m_sceneToOpen;

        public static void StartScene(string sceneName)
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }

            var activeScene = SceneManager.GetActiveScene();

            // 缓存一下当前正在进行编辑的场景文件
            if (activeScene.isLoaded && activeScene.name != sceneName)
            {
                EditorPrefs.SetString(PreviousSceneKey, activeScene.path);
                EditorPrefs.SetBool(IsLauncherBtn, true);
            }

            m_sceneToOpen = sceneName;
            EditorApplication.update += OnUpdate;
        }

        private static void OnUpdate()
        {
            if (string.IsNullOrEmpty(m_sceneToOpen) ||
                EditorApplication.isPlaying || EditorApplication.isPaused ||
                EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            EditorApplication.update -= OnUpdate;

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                string[] guids = AssetDatabase.FindAssets("t:scene " + m_sceneToOpen, null);

                if (guids.Length <= 0)
                {
                    Debug.LogWarning("找不到场景文件");
                }
                else
                {
                    string scenePath = null;

                    for (int i = 0; i < guids.Length; i++)
                    {
                        scenePath = AssetDatabase.GUIDToAssetPath(guids[i]);

                        if (scenePath.EndsWith("/" + m_sceneToOpen + ".unity"))
                        {
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(scenePath))
                    {
                        scenePath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    }

                    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    EditorApplication.isPlaying = true;
                }
            }

            m_sceneToOpen = null;
        }
    }
}

public class MainToolbarDropdownSceneSelector
{
    const string kElementPath = "TEngine/Scene Switcher";

    private static List<(string sceneName, string scenePath)> m_initScenes;
    private static List<(string sceneName, string scenePath)> m_defaultScenes;
    private static List<(string sceneName, string scenePath)> m_otherScenes;

    private static string initScenePath = "Assets/Scenes";
    private static string defaultScenePath = "Assets/AssetRaw/Scenes";

    static string[] scenePaths;

    [MainToolbarElement(kElementPath, defaultDockPosition = MainToolbarDockPosition.Middle, defaultDockIndex = 50)]
    public static MainToolbarElement CreateSceneSelectorDropdown()
    {
        string activeSceneName;
        if (Application.isPlaying)
            activeSceneName = SceneManager.GetActiveScene().name;
        else
            activeSceneName = EditorSceneManager.GetActiveScene().name;
        if (activeSceneName.Length == 0)
            activeSceneName = "Untitled";

        var icon = EditorGUIUtility.IconContent("UnityLogo").image as Texture2D;
        var content = new MainToolbarContent(activeSceneName, icon, "Select active scene");
        return new MainToolbarDropdown(content, ShowDropdownMenu);
    }

    public static void Init()
    {
        EditorApplication.projectChanged += UpdateScenes;
        UpdateScenes();
        SceneManager.activeSceneChanged += SceneSwitched;
        EditorSceneManager.activeSceneChangedInEditMode += SceneSwitched;
    }

    static void ShowDropdownMenu(Rect dropDownRect)
    {
        var menu = new GenericMenu();
        AddScenesToMenu(m_initScenes, "初始化场景", menu);
        AddScenesToMenu(m_defaultScenes, "默认场景", menu);
        AddScenesToMenu(m_otherScenes, "其他场景", menu);
        menu.DropDown(dropDownRect);
    }

    private static void AddScenesToMenu(List<(string sceneName, string scenePath)> scenes, string category, GenericMenu menu)
    {
        if (scenes != null && scenes.Count > 0)
        {
            foreach (var scene in scenes)
            {
                menu.AddItem(new GUIContent($"{category}/{scene.sceneName}"), false, () =>
                {
                    SwitchScene(scene.scenePath);
                });
            }
        }
    }

    static void SwitchScene(string scenePath)
    {
        if (Application.isPlaying)
        {
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            if (Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.Log($"Switching to scene: {sceneName}");
                SceneManager.LoadScene(sceneName);
            }
            else
            {
                Debug.LogError($"Scene '{sceneName}' is not in the Build Settings.");
            }
        }
        else
        {
            if (File.Exists(scenePath))
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    Debug.Log($"Switching to scene: {scenePath}");
                    EditorSceneManager.OpenScene(scenePath);
                }
            }
            else
            {
                Debug.LogError($"Scene at path '{scenePath}' does not exist.");
            }
        }
    }

    static void SceneSwitched(Scene oldScene, Scene newScene)
    {
        MainToolbar.Refresh(kElementPath);
    }

    static void UpdateScenes()
    {
        m_initScenes = SceneSwitcher.GetScenesInPath(initScenePath);
        m_defaultScenes = SceneSwitcher.GetScenesInPath(defaultScenePath);

        List<(string sceneName, string scenePath)> allScenes = GetScenesInPath();
        m_otherScenes = new List<(string sceneName, string scenePath)>(allScenes);
        m_otherScenes.RemoveAll(scene =>
            m_initScenes.Exists(init => init.scenePath == scene.scenePath) ||
            m_defaultScenes.Exists(abScene => abScene.scenePath == scene.scenePath));
    }

    private static List<(string sceneName, string scenePath)> GetScenesInPath()
    {
        var allScenes = new List<(string sceneName, string scenePath)>();

        // 查找项目中所有场景文件
        string[] guids = AssetDatabase.FindAssets("t:Scene");
        foreach (var guid in guids)
        {
            var scenePath = AssetDatabase.GUIDToAssetPath(guid);
            var sceneName = Path.GetFileNameWithoutExtension(scenePath);
            allScenes.Add((sceneName, scenePath));
        }

        return allScenes;
    }

    private static class SceneSwitcher
    {
        public static List<(string sceneName, string scenePath)> GetScenesInPath(string path)
        {
            var scenes = new List<(string sceneName, string scenePath)>();
            var guids = AssetDatabase.FindAssets("t:Scene", new string[] { path });

            foreach (var guid in guids)
            {
                var scenePath = AssetDatabase.GUIDToAssetPath(guid);
                var sceneName = Path.GetFileNameWithoutExtension(scenePath);
                scenes.Add((sceneName, scenePath));
            }
            return scenes;
        }

        public static bool PromptSaveCurrentScene()
        {
            if (SceneManager.GetActiveScene().isDirty)
            {
                bool saveScene = EditorUtility.DisplayDialog(
                    "是否保存当前场景",
                    "当前场景有未保存的更改，是否想保存？",
                    "保存",
                    "取消");

                if (saveScene)
                {
                    EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                }
                else
                {
                    return false;
                }
                return true;
            }
            return true;
        }
    }
}

public class MainToolbarDropdownPlayMode
{
    const string kElementPath = "TEngine/Play Mode";

    private static readonly string[] _resourceModeNames =
    {
        "EditorMode (编辑器下的模拟模式)",
        "OfflinePlayMode (单机模式)",
        "HostPlayMode (联机运行模式)",
        "WebPlayMode (WebGL运行模式)"
    };

    private static int _resourceModeIndex = 0;
    public static int ResourceModeIndex => _resourceModeIndex;

    private static MainToolbarElement m_btn;

    [MainToolbarElement(kElementPath, defaultDockPosition = MainToolbarDockPosition.Middle, defaultDockIndex = 51)]
    public static MainToolbarElement CreateExampleDropdown()
    {
        var content = new MainToolbarContent(_resourceModeNames[ResourceModeIndex]);
        m_btn = new MainToolbarDropdown(content, ShowDropdownMenu)
        {
            enabled = !EditorApplication.isPlaying
        };
        _resourceModeIndex = EditorPrefs.GetInt("EditorPlayMode");
        return m_btn;
    }

    public static void Init()
    {
        // 监听播放模式变化
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        // EditorApplication.projectChanged += UpdateScenes;
        // SceneManager.activeSceneChanged += SceneSwitched;
        // EditorSceneManager.activeSceneChangedInEditMode += SceneSwitched;
    }
    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        CreateExampleDropdown();
        MainToolbar.Refresh(kElementPath);
    }

    static void ShowDropdownMenu(Rect dropDownRect)
    {
        var menu = new GenericMenu();

        for (var index = 0; index < _resourceModeNames.Length; index++)
        {
            int i = index;
            var resourceModeName = _resourceModeNames[index];
            menu.AddItem(new GUIContent(resourceModeName), false, () =>
            {
                _resourceModeIndex = i;
                Debug.Log($"更改编辑器资源运行模式：{_resourceModeNames[_resourceModeIndex]}");
                EditorPrefs.SetInt("EditorPlayMode", _resourceModeIndex);
                MainToolbar.Refresh(kElementPath);
            });
        }

        menu.DropDown(dropDownRect);
    }
}

#endif
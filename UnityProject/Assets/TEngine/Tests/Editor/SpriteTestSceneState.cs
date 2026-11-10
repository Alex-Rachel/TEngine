using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// EnterPlayMode 会重载域，测试 iterator 的 SceneSetup[] 不会被 Runner 保留。
internal static class SpriteTestSceneState
{
    [Serializable]
    private sealed class State
    {
        public Entry[] scenes;
    }

    [Serializable]
    private sealed class Entry
    {
        public string path;
        public bool isLoaded,
            isActive;
    }

    public static void Capture(string key)
    {
        var setup = EditorSceneManager.GetSceneManagerSetup();
        SessionState.SetString(
            key,
            JsonUtility.ToJson(
                new State
                {
                    scenes = Array.ConvertAll(
                        setup,
                        item => new Entry
                        {
                            path = item.path,
                            isLoaded = item.isLoaded,
                            isActive = item.isActive,
                        }
                    ),
                }
            )
        );
    }

    public static void Restore(string key)
    {
        var json = SessionState.GetString(key, "");
        if (string.IsNullOrEmpty(json))
            throw new InvalidOperationException("测试场景状态丢失: " + key);
        var state = JsonUtility.FromJson<State>(json);
        SessionState.EraseString(key);
        if (
            Array.Exists(
                state.scenes,
                item => item.isLoaded && item.isActive && !string.IsNullOrEmpty(item.path)
            )
        )
            EditorSceneManager.RestoreSceneManagerSetup(
                Array.ConvertAll(
                    state.scenes,
                    item => new SceneSetup
                    {
                        path = item.path,
                        isLoaded = item.isLoaded,
                        isActive = item.isActive,
                    }
                )
            );
        else
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }
}

using UnityEditor;
using UnityEngine;

namespace TEngine.Editor
{
    public static class LubanTools
    {
        [MenuItem("TEngine/Luban/转表 &X", priority = -100)]
        private static void ZhuanXiaoYi()
        {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            string path = Application.dataPath + "/../../Configs/GameConfig/gen_code_bin_to_project_lazyload.sh";
#elif UNITY_EDITOR_WIN
            string path = Application.dataPath + "/../../Configs/GameConfig/gen_code_bin_to_project_lazyload.bat";
#endif
            Debug.Log($"执行转表：{path}");
            ShellHelper.RunByPath(path);
        }
    }
}
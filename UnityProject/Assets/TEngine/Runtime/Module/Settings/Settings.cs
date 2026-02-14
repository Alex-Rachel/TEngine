using UnityEngine;

namespace TEngine
{
    public class Settings : MonoBehaviour
    {
        private static Settings _instance;

        public static Settings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Utility.Unity.FindObjectOfType<Settings>();

                    if (_instance != null)
                    {
                        return _instance;
                    }
                }

                return _instance;
            }
        }

        [SerializeField]
        private AudioSetting audioSetting;

        [SerializeField]
        private ProcedureSetting procedureSetting;

        [SerializeField]
        private UpdateSetting updateSetting;

        public static AudioSetting AudioSetting => Instance.audioSetting;

        public static ProcedureSetting ProcedureSetting => Instance.procedureSetting;

        public static UpdateSetting UpdateSetting
        {
            get
            {
#if UNITY_EDITOR
                if (Instance == null)
                {
                    string[] guids = UnityEditor.AssetDatabase.FindAssets("t:UpdateSetting");
                    if (guids.Length >= 1)
                    {
                        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                        return UnityEditor.AssetDatabase.LoadAssetAtPath<UpdateSetting>(path);
                    }
                }
#endif
                return Instance.updateSetting;
            }
        }
    }
}
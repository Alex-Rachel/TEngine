using UnityEngine;

namespace TEngine
{
    [CreateAssetMenu(menuName = "TEngine/AudioSetting", fileName = "AudioSetting")]
    public class AudioSetting : ScriptableObject
    {
        public AudioGroupConfig[] audioGroupConfigs = null;
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;
using YooAsset;

namespace TEngine
{
    internal class AudioModule : Module, IAudioModule, IUpdateModule
    {
        public const string MUSIC_VOLUME_NAME = "MusicVolume";
        public const string UI_SOUND_VOLUME_NAME = "UISoundVolume";
        public const string VOICE_VOLUME_NAME = "VoiceVolume";

        private AudioMixer _audioMixer;
        private Transform _instanceRoot = null;
        private AudioGroupConfig[] _audioGroupConfigs = null;

        private float _volume = 1f;
        private bool _enable = true;
        private readonly AudioCategory[] _audioCategories = new AudioCategory[(int)AudioType.Max];
        private readonly float[] _categoriesVolume = new float[(int)AudioType.Max];
        private bool _bUnityAudioDisabled = false;

        #region Public Propreties

        public Dictionary<string, AssetHandle> AudioClipPool { get; set; } = new Dictionary<string, AssetHandle>();

        /// <summary>
        /// 音频混响器。
        /// </summary>
        public AudioMixer AudioMixer => _audioMixer;

        /// <summary>
        /// 实例化根节点。
        /// </summary>
        public Transform InstanceRoot => _instanceRoot;

        /// <summary>
        /// 总音量控制。
        /// </summary>
        public float Volume
        {
            get
            {
                if (_bUnityAudioDisabled)
                {
                    return 0.0f;
                }

                return _volume;
            }
            set
            {
                if (_bUnityAudioDisabled)
                {
                    return;
                }

                _volume = value;
                AudioListener.volume = _volume;
            }
        }

        /// <summary>
        /// 总开关。
        /// </summary>
        public bool Enable
        {
            get
            {
                if (_bUnityAudioDisabled)
                {
                    return false;
                }

                return _enable;
            }
            set
            {
                if (_bUnityAudioDisabled)
                {
                    return;
                }

                _enable = value;
                AudioListener.volume = _enable ? _volume : 0f;
            }
        }

        /// <summary>
        /// 音乐音量。
        /// </summary>
        public float MusicVolume
        {
            get
            {
                if (_bUnityAudioDisabled)
                {
                    return 0.0f;
                }

                return _categoriesVolume[(int)AudioType.Music];
            }
            set
            {
                if (_bUnityAudioDisabled)
                {
                    return;
                }

                float volume = Mathf.Clamp(value, 0.0001f, 1.0f);
                _categoriesVolume[(int)AudioType.Music] = volume;
                _audioMixer.SetFloat(MUSIC_VOLUME_NAME, Mathf.Log10(volume) * 20f);
            }
        }

        /// <summary>
        /// 音效音量。
        /// </summary>
        public float SoundVolume
        {
            get
            {
                if (_bUnityAudioDisabled)
                {
                    return 0.0f;
                }

                return _categoriesVolume[(int)AudioType.Sound];
            }
            set
            {
                if (_bUnityAudioDisabled)
                {
                    return;
                }

                float volume = Mathf.Clamp(value, 0.0001f, 1.0f);
                _categoriesVolume[(int)AudioType.Sound] = volume;
                _audioMixer.SetFloat("SoundVolume", Mathf.Log10(volume) * 20f);
            }
        }

        /// <summary>
        /// UI音效音量。
        /// </summary>
        public float UISoundVolume
        {
            get
            {
                if (_bUnityAudioDisabled)
                {
                    return 0.0f;
                }

                return _categoriesVolume[(int)AudioType.UISound];
            }
            set
            {
                if (_bUnityAudioDisabled)
                {
                    return;
                }

                float volume = Mathf.Clamp(value, 0.0001f, 1.0f);
                _categoriesVolume[(int)AudioType.UISound] = volume;
                _audioMixer.SetFloat(UI_SOUND_VOLUME_NAME, Mathf.Log10(volume) * 20f);
            }
        }

        /// <summary>
        /// 语音音量。
        /// </summary>
        public float VoiceVolume
        {
            get
            {
                if (_bUnityAudioDisabled)
                {
                    return 0.0f;
                }

                return _categoriesVolume[(int)AudioType.Voice];
            }
            set
            {
                if (_bUnityAudioDisabled)
                {
                    return;
                }

                float volume = Mathf.Clamp(value, 0.0001f, 1.0f);
                _categoriesVolume[(int)AudioType.Voice] = volume;
                _audioMixer.SetFloat(VOICE_VOLUME_NAME, Mathf.Log10(volume) * 20f);
            }
        }

        /// <summary>
        /// 音乐开关
        /// </summary>
        public bool MusicEnable
        {
            get
            {
                if (_bUnityAudioDisabled)
                {
                    return false;
                }

                if (_audioMixer.GetFloat(MUSIC_VOLUME_NAME, out var db))
                {
                    return db > -80f;
                }
                else
                {
                    return false;
                }
            }
            set
            {
                if (_bUnityAudioDisabled)
                {
                    return;
                }

                _audioCategories[(int)AudioType.Music].Enable = value;

                // 音乐采用0音量方式，避免恢复播放时的复杂逻辑
                if (value)
                {
                    _audioMixer.SetFloat(MUSIC_VOLUME_NAME, Mathf.Log10(_categoriesVolume[(int)AudioType.Music]) * 20f);
                }
                else
                {
                    _audioMixer.SetFloat(MUSIC_VOLUME_NAME, -80f);
                }
            }
        }

        /// <summary>
        /// 音效开关。
        /// </summary>
        public bool SoundEnable
        {
            get
            {
                if (_bUnityAudioDisabled)
                {
                    return false;
                }

                return _audioCategories[(int)AudioType.Sound].Enable;
            }
            set
            {
                if (_bUnityAudioDisabled)
                {
                    return;
                }

                _audioCategories[(int)AudioType.Sound].Enable = value;
            }
        }

        /// <summary>
        /// UI音效开关。
        /// </summary>
        public bool UISoundEnable
        {
            get
            {
                if (_bUnityAudioDisabled)
                {
                    return false;
                }

                return _audioCategories[(int)AudioType.UISound].Enable;
            }
            set
            {
                if (_bUnityAudioDisabled)
                {
                    return;
                }

                _audioCategories[(int)AudioType.UISound].Enable = value;
            }
        }

        /// <summary>
        /// 语音开关。
        /// </summary>
        public bool VoiceEnable
        {
            get
            {
                if (_bUnityAudioDisabled)
                {
                    return false;
                }

                return _audioCategories[(int)AudioType.Voice].Enable;
            }
            set
            {
                if (_bUnityAudioDisabled)
                {
                    return;
                }

                _audioCategories[(int)AudioType.Voice].Enable = value;
            }
        }

        #endregion

        private IResourceModule _resourceModule;

        public override void OnInit()
        {
            _resourceModule = ModuleSystem.GetModule<IResourceModule>();
            Initialize(Settings.AudioSetting.audioGroupConfigs);
        }

        public override void Shutdown()
        {
            StopAll(fadeout: false);
            CleanSoundPool();
        }

        /// <summary>
        /// 初始化音频模块。
        /// </summary>
        /// <param name="audioGroupConfigs">音频轨道组配置。</param>
        /// <param name="instanceRoot">实例化根节点。</param>
        /// <param name="audioMixer">音频混响器。</param>
        /// <exception cref="GameFrameworkException"></exception>
        public void Initialize(AudioGroupConfig[] audioGroupConfigs, Transform instanceRoot = null, AudioMixer audioMixer = null)
        {
            if (_instanceRoot == null)
            {
                _instanceRoot = instanceRoot;
            }

            if (audioGroupConfigs == null)
            {
                throw new GameFrameworkException("AudioGroupConfig[] is invalid.");
            }

            _audioGroupConfigs = audioGroupConfigs;

            if (_instanceRoot == null)
            {
                _instanceRoot = new GameObject("[AudioModule Instances]").transform;
                _instanceRoot.localScale = Vector3.one;
                UnityEngine.Object.DontDestroyOnLoad(_instanceRoot);
            }
            
#if UNITY_EDITOR
            try
            {
                TypeInfo typeInfo = typeof(AudioSettings).GetTypeInfo();
                PropertyInfo propertyInfo = typeInfo.GetDeclaredProperty("unityAudioDisabled");
                _bUnityAudioDisabled = (bool)propertyInfo.GetValue(null);
                if (_bUnityAudioDisabled)
                {
                    return;
                }
            }
            catch (Exception e)
            {
                Log.Error(e.ToString());
            }
#endif

            if (audioMixer != null)
            {
                _audioMixer = audioMixer;
            }

            if (_audioMixer == null)
            {
                _audioMixer = Resources.Load<AudioMixer>("AudioMixer");
            }

            for (int index = 0; index < (int)AudioType.Max; ++index)
            {
                AudioType audioType = (AudioType)index;
                AudioGroupConfig audioGroupConfig = _audioGroupConfigs.First(t => t.AudioType == audioType);
                _audioCategories[index] = new AudioCategory(audioGroupConfig.AgentHelperCount, _audioMixer, audioGroupConfig);
                _categoriesVolume[index] = audioGroupConfig.Volume;
            }
        }

        /// <summary>
        /// 重启音频模块。
        /// </summary>
        public void Restart()
        {
            if (_bUnityAudioDisabled)
            {
                return;
            }

            CleanSoundPool();

            for (int i = 0; i < (int)AudioType.Max; ++i)
            {
                var audioCategory = _audioCategories[i];
                if (audioCategory != null)
                {
                    for (int j = 0; j < audioCategory.AudioAgents.Count; ++j)
                    {
                        var audioAgent = audioCategory.AudioAgents[j];
                        if (audioAgent != null)
                        {
                            audioAgent.Destroy();
                            audioAgent = null;
                        }
                    }
                }

                audioCategory = null;
            }

            Initialize(_audioGroupConfigs);
        }

        /// <summary>
        /// 播放，如果超过最大发声数采用fadeout的方式复用最久播放的AudioSource。
        /// </summary>
        /// <param name="type">声音类型</param>
        /// <param name="path">声音文件路径</param>
        /// <param name="bLoop">是否循环播放</param>>
        /// <param name="volume">音量（0-1.0）</param>
        /// <param name="bAsync">是否异步加载</param>
        /// <param name="bInPool">是否支持资源池</param>
        public AudioAgent Play(AudioType type, string path, bool bLoop = false, float volume = 1.0f, bool bAsync = false, bool bInPool = false)
        {
            if (_bUnityAudioDisabled)
            {
                return null;
            }

            AudioAgent audioAgent = _audioCategories[(int)type].Play(path, bAsync, bInPool);
            {
                if (audioAgent != null)
                {
                    audioAgent.IsLoop = bLoop;
                    audioAgent.Volume = volume;
                }

                return audioAgent;
            }
        }

        /// <summary>
        /// 停止某类声音播放。
        /// </summary>
        /// <param name="type">声音类型。</param>
        /// <param name="fadeout">是否渐消。</param>
        public void Stop(AudioType type, bool fadeout)
        {
            if (_bUnityAudioDisabled)
            {
                return;
            }

            _audioCategories[(int)type].Stop(fadeout);
        }

        /// <summary>
        /// 停止所有声音。
        /// </summary>
        /// <param name="fadeout">是否渐消。</param>
        public void StopAll(bool fadeout)
        {
            if (_bUnityAudioDisabled)
            {
                return;
            }

            for (int i = 0; i < (int)AudioType.Max; ++i)
            {
                if (_audioCategories[i] != null)
                {
                    _audioCategories[i].Stop(fadeout);
                }
            }
        }

        /// <summary>
        /// 预先加载AudioClip，并放入对象池。
        /// </summary>
        /// <param name="list">AudioClip的AssetPath集合。</param>
        public void PutInAudioPool(List<string> list)
        {
            if (_bUnityAudioDisabled)
            {
                return;
            }

            foreach (string path in list)
            {
                if (AudioClipPool != null && !AudioClipPool.ContainsKey(path))
                {
                    AssetHandle assetData = _resourceModule.LoadAssetAsyncHandle<AudioClip>(path);
                    assetData.Completed += handle => { AudioClipPool?.Add(path, handle); };
                }
            }
        }

        /// <summary>
        /// 将部分AudioClip从对象池移出。
        /// </summary>
        /// <param name="list">AudioClip的AssetPath集合。</param>
        public void RemoveClipFromPool(List<string> list)
        {
            if (_bUnityAudioDisabled)
            {
                return;
            }

            foreach (string path in list)
            {
                if (AudioClipPool.ContainsKey(path))
                {
                    AudioClipPool[path].Dispose();
                    AudioClipPool.Remove(path);
                }
            }
        }

        /// <summary>
        /// 清空AudioClip的对象池。
        /// </summary>
        public void CleanSoundPool()
        {
            if (_bUnityAudioDisabled)
            {
                return;
            }

            foreach (var dic in AudioClipPool)
            {
                dic.Value.Dispose();
            }

            AudioClipPool.Clear();
        }

        /// <summary>
        /// 音频模块轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            foreach (var audioCategory in _audioCategories)
            {
                if (audioCategory != null)
                {
                    audioCategory.Update(elapseSeconds);
                }
            }
        }
    }
}
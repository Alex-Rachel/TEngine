using UnityEngine;
using UnityEngine.Audio;
using YooAsset;

namespace TEngine
{
    /// <summary>
    /// 音频代理辅助器。
    /// </summary>
    public class AudioAgent
    {
        private int _instanceId;
        private AudioSource _source;
        private AudioData _audioData;
        private IAudioModule _audioModule;
        private IResourceModule _resourceModule;
        private Transform _transform;
        private float _volume = 1.0f;
        private float _duration;
        private float _fadeoutTimer;
        private const float FADEOUT_DURATION = 0.2f;
        private bool _inPool;

        /// <summary>
        /// 音频代理辅助器运行时状态。
        /// </summary>
        AudioAgentRuntimeState _audioAgentRuntimeState = AudioAgentRuntimeState.None;

        /// <summary>
        /// 音频代理加载请求。
        /// </summary>
        class LoadRequest
        {
            /// <summary>
            /// 音频代理辅助器加载路径。
            /// </summary>
            public string Path;
            
            /// <summary>
            /// 是否异步。
            /// </summary>
            public bool BAsync;
            
            /// <summary>
            /// 是否池化。
            /// </summary>
            public bool BInPool;
        }

        /// <summary>
        /// 音频代理加载请求。
        /// </summary>
        LoadRequest _pendingLoad = null;

        /// <summary>
        /// AudioSource实例化Id
        /// </summary>
        public int InstanceId => _instanceId;

        /// <summary>
        /// 资源操作句柄。
        /// </summary>
        public AudioData AudioData => _audioData;

        /// <summary>
        /// 音频代理辅助器音频大小。
        /// </summary>
        public float Volume
        {
            set
            {
                if (_source != null)
                {
                    _volume = value;
                    _source.volume = _volume;
                }
            }
            get => _volume;
        }

        /// <summary>
        /// 音频代理辅助器当前是否空闲。
        /// </summary>
        public bool IsFree
        {
            get
            {
                if (_source != null)
                {
                    return _audioAgentRuntimeState == AudioAgentRuntimeState.End;
                }
                else
                {
                    return true;
                }
            }
        }

        /// <summary>
        /// 音频代理辅助器播放秒数。
        /// </summary>
        public float Duration => _duration;

        /// <summary>
        /// 音频代理辅助器当前音频长度。
        /// </summary>
        public float Length
        {
            get
            {
                if (_source != null && _source.clip != null)
                {
                    return _source.clip.length;
                }

                return 0;
            }
        }

        /// <summary>
        /// 音频代理辅助器实例位置。
        /// </summary>
        public Vector3 Position
        {
            get => _transform.position;
            set => _transform.position = value;
        }

        /// <summary>
        /// 音频代理辅助器是否循环。
        /// </summary>
        public bool IsLoop
        {
            get
            {
                if (_source != null)
                {
                    return _source.loop;
                }
                else
                {
                    return false;
                }
            }
            set
            {
                if (_source != null)
                {
                    _source.loop = value;
                }
            }
        }

        /// <summary>
        /// 音频代理辅助器是否正在播放。
        /// </summary>
        internal bool IsPlaying
        {
            get
            {
                if (_source != null && _source.isPlaying)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 音频代理辅助器获取当前声源。
        /// </summary>
        /// <returns></returns>
        public AudioSource AudioResource()
        {
            return _source;
        }

        /// <summary>
        /// 创建音频代理辅助器。
        /// </summary>
        /// <param name="path">生效路径。</param>
        /// <param name="bAsync">是否异步。</param>
        /// <param name="audioCategory">音频轨道（类别）。</param>
        /// <param name="bInPool">是否池化。</param>
        /// <returns>音频代理辅助器。</returns>
        public static AudioAgent Create(string path, bool bAsync, AudioCategory audioCategory, bool bInPool = false)
        {
            AudioAgent audioAgent = new AudioAgent();
            audioAgent.Init(audioCategory);
            audioAgent.Load(path, bAsync, bInPool);
            return audioAgent;
        }

        /// <summary>
        /// 初始化音频代理辅助器。
        /// </summary>
        /// <param name="audioCategory">音频轨道（类别）。</param>
        /// <param name="index">音频代理辅助器编号。</param>
        public void Init(AudioCategory audioCategory, int index = 0)
        {
            _audioModule = ModuleSystem.GetModule<IAudioModule>();
            _resourceModule = ModuleSystem.GetModule<IResourceModule>();
            GameObject host = new GameObject(Utility.Text.Format("Audio Agent Helper - {0} - {1}", audioCategory.AudioMixerGroup.name, index));
            host.transform.SetParent(audioCategory.InstanceRoot);
            host.transform.localPosition = Vector3.zero;
            _transform = host.transform;
            _source = host.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            AudioMixerGroup[] audioMixerGroups =
                audioCategory.AudioMixer.FindMatchingGroups(Utility.Text.Format("Master/{0}/{1}", audioCategory.AudioMixerGroup.name,
                    $"{audioCategory.AudioMixerGroup.name} - {index}"));
            _source.outputAudioMixerGroup = audioMixerGroups.Length > 0 ? audioMixerGroups[0] : audioCategory.AudioMixerGroup;
            _source.rolloffMode = audioCategory.AudioGroupConfig.audioRolloffMode;
            _source.minDistance = audioCategory.AudioGroupConfig.minDistance;
            _source.maxDistance = audioCategory.AudioGroupConfig.maxDistance;
            _instanceId = _source.GetInstanceID();
        }

        /// <summary>
        /// 加载音频代理辅助器。
        /// </summary>
        /// <param name="path">资源路径。</param>
        /// <param name="bAsync">是否异步。</param>
        /// <param name="bInPool">是否池化。</param>
        public void Load(string path, bool bAsync, bool bInPool = false)
        {
            _inPool = bInPool;
            if (_audioAgentRuntimeState == AudioAgentRuntimeState.None || _audioAgentRuntimeState == AudioAgentRuntimeState.End)
            {
                _duration = 0;
                if (!string.IsNullOrEmpty(path))
                {
                    if (bInPool && _audioModule.AudioClipPool.TryGetValue(path, out var operationHandle))
                    {
                        OnAssetLoadComplete(operationHandle);
                        return;
                    }

                    if (bAsync)
                    {
                        _audioAgentRuntimeState = AudioAgentRuntimeState.Loading;
                        AssetHandle handle = _resourceModule.LoadAssetAsyncHandle<AudioClip>(path);
                        handle.Completed += OnAssetLoadComplete;
                    }
                    else
                    {
                        AssetHandle handle = _resourceModule.LoadAssetSyncHandle<AudioClip>(path);
                        OnAssetLoadComplete(handle);
                    }
                }
            }
            else
            {
                _pendingLoad = new LoadRequest { Path = path, BAsync = bAsync, BInPool = bInPool };

                if (_audioAgentRuntimeState == AudioAgentRuntimeState.Playing)
                {
                    Stop(true);
                }
            }
        }

        /// <summary>
        /// 停止播放音频代理辅助器。
        /// </summary>
        /// <param name="fadeout">是否渐出。</param>
        public void Stop(bool fadeout = false)
        {
            if (_source != null)
            {
                if (fadeout)
                {
                    _fadeoutTimer = FADEOUT_DURATION;
                    _audioAgentRuntimeState = AudioAgentRuntimeState.FadingOut;
                }
                else
                {
                    _source.Stop();
                    _audioAgentRuntimeState = AudioAgentRuntimeState.End;
                }
            }
        }

        /// <summary>
        /// 暂停音频代理辅助器。
        /// </summary>
        public void Pause()
        {
            if (_source != null)
            {
                _source.Pause();
            }
        }

        /// <summary>
        /// 取消暂停音频代理辅助器。
        /// </summary>
        public void UnPause()
        {
            if (_source != null)
            {
                _source.UnPause();
            }
        }

        /// <summary>
        /// 资源加载完成。
        /// </summary>
        /// <param name="handle">资源操作句柄。</param>
        void OnAssetLoadComplete(AssetHandle handle)
        {
            if (handle != null)
            {
                if (_inPool)
                {
                    _audioModule.AudioClipPool.TryAdd(handle.GetAssetInfo().Address, handle);
                }
            }

            if (_pendingLoad != null)
            {
                if (!_inPool && handle != null)
                {
                    handle.Dispose();
                }

                _audioAgentRuntimeState = AudioAgentRuntimeState.End;
                string path = _pendingLoad.Path;
                bool bAsync = _pendingLoad.BAsync;
                bool bInPool = _pendingLoad.BInPool;
                _pendingLoad = null;
                Load(path, bAsync, bInPool);
            }
            else if (handle != null)
            {
                if (_audioData != null)
                {
                    AudioData.DeAlloc(_audioData);
                    _audioData = null;
                }

                _audioData = AudioData.Alloc(handle, _inPool);

                _source.clip = handle.AssetObject as AudioClip;
                if (_source.clip != null)
                {
                    _source.Play();
                    _audioAgentRuntimeState = AudioAgentRuntimeState.Playing;
                }
                else
                {
                    _audioAgentRuntimeState = AudioAgentRuntimeState.End;
                }
            }
            else
            {
                _audioAgentRuntimeState = AudioAgentRuntimeState.End;
            }
        }

        /// <summary>
        /// 轮询音频代理辅助器。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        public void Update(float elapseSeconds)
        {
            if (_audioAgentRuntimeState == AudioAgentRuntimeState.Playing)
            {
                if (!_source.isPlaying)
                {
                    _audioAgentRuntimeState = AudioAgentRuntimeState.End;
                }
            }
            else if (_audioAgentRuntimeState == AudioAgentRuntimeState.FadingOut)
            {
                if (_fadeoutTimer > 0f)
                {
                    _fadeoutTimer -= elapseSeconds;
                    _source.volume = _volume * _fadeoutTimer / FADEOUT_DURATION;
                }
                else
                {
                    Stop();
                    if (_pendingLoad != null)
                    {
                        string path = _pendingLoad.Path;
                        bool bAsync = _pendingLoad.BAsync;
                        bool bInPool = _pendingLoad.BInPool;
                        _pendingLoad = null;
                        Load(path, bAsync, bInPool);
                    }

                    _source.volume = _volume;
                }
            }

            _duration += elapseSeconds;
        }

        /// <summary>
        /// 销毁音频代理辅助器。
        /// </summary>
        public void Destroy()
        {
            if (_transform != null)
            {
                Object.Destroy(_transform.gameObject);
            }

            if (_audioData != null)
            {
                AudioData.DeAlloc(_audioData);
            }
        }
    }
}
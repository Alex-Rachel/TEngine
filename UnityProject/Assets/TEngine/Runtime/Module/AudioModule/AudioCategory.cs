using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace TEngine
{
    /// <summary>
    /// 音频轨道（类别）。
    /// </summary>
    [Serializable]
    public class AudioCategory
    {
        [SerializeField]
        private AudioMixer audioMixer = null;

        public List<AudioAgent> AudioAgents;
        private readonly AudioMixerGroup _audioMixerGroup;
        private AudioGroupConfig _audioGroupConfig;
        private int _maxChannel;
        private bool _bEnable = true;

        /// <summary>
        /// 音频混响器。
        /// </summary>
        public AudioMixer AudioMixer => audioMixer;

        /// <summary>
        /// 音频混响器组。
        /// </summary>
        public AudioMixerGroup AudioMixerGroup => _audioMixerGroup;

        /// <summary>
        /// 音频组配置。
        /// </summary>
        public AudioGroupConfig AudioGroupConfig => _audioGroupConfig;

        /// <summary>
        /// 实例化根节点。
        /// </summary>
        public Transform InstanceRoot { private set; get; }

        /// <summary>
        /// 音频轨道是否启用。
        /// </summary>
        public bool Enable
        {
            get => _bEnable;
            set
            {
                if (_bEnable != value)
                {
                    _bEnable = value;
                    if (!_bEnable)
                    {
                        foreach (var audioAgent in AudioAgents)
                        {
                            if (audioAgent != null)
                            {
                                audioAgent.Stop();
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 音频轨道构造函数。
        /// </summary>
        /// <param name="maxChannel">最大Channel。</param>
        /// <param name="audioMixer">音频混响器。</param>
        /// <param name="audioGroupConfig">音频轨道组配置。</param>
        public AudioCategory(int maxChannel, AudioMixer audioMixer, AudioGroupConfig audioGroupConfig)
        {
            var audioModule = ModuleSystem.GetModule<IAudioModule>();

            this.audioMixer = audioMixer;
            _maxChannel = maxChannel;
            _audioGroupConfig = audioGroupConfig;
            AudioMixerGroup[] audioMixerGroups = audioMixer.FindMatchingGroups(Utility.Text.Format("Master/{0}", audioGroupConfig.AudioType.ToString()));
            if (audioMixerGroups.Length > 0)
            {
                _audioMixerGroup = audioMixerGroups[0];
            }
            else
            {
                _audioMixerGroup = audioMixer.FindMatchingGroups("Master")[0];
            }

            AudioAgents = new List<AudioAgent>(32);
            InstanceRoot = new GameObject(Utility.Text.Format("Audio Category - {0}", _audioMixerGroup.name)).transform;
            InstanceRoot.SetParent(audioModule.InstanceRoot);
            for (int index = 0; index < _maxChannel; index++)
            {
                AudioAgent audioAgent = new AudioAgent();
                audioAgent.Init(this, index);
                AudioAgents.Add(audioAgent);
            }
        }

        /// <summary>
        /// 增加音频。
        /// </summary>
        /// <param name="num"></param>
        public void AddAudio(int num)
        {
            _maxChannel += num;
            for (int i = 0; i < num; i++)
            {
                AudioAgents.Add(null);
            }
        }

        /// <summary>
        /// 播放音频。
        /// </summary>
        /// <param name="path"></param>
        /// <param name="bAsync"></param>
        /// <param name="bInPool"></param>
        /// <returns></returns>
        public AudioAgent Play(string path, bool bAsync, bool bInPool = false)
        {
            if (!_bEnable)
            {
                return null;
            }

            int freeChannel = -1;
            float duration = -1;

            for (int i = 0; i < AudioAgents.Count; i++)
            {
                if (AudioAgents[i].AudioData?.AssetHandle == null || AudioAgents[i].IsFree)
                {
                    freeChannel = i;
                    break;
                }
                else if (AudioAgents[i].Duration > duration)
                {
                    duration = AudioAgents[i].Duration;
                    freeChannel = i;
                }
            }

            if (freeChannel >= 0)
            {
                if (AudioAgents[freeChannel] == null)
                {
                    AudioAgents[freeChannel] = AudioAgent.Create(path, bAsync, this, bInPool);
                }
                else
                {
                    AudioAgents[freeChannel].Load(path, bAsync, bInPool);
                }

                return AudioAgents[freeChannel];
            }
            else
            {
                Log.Error($"Here is no channel to play audio {path}");
                return null;
            }
        }

        /// <summary>
        /// 暂停音频。
        /// </summary>
        /// <param name="fadeout">是否渐出</param>
        public void Stop(bool fadeout)
        {
            for (int i = 0; i < AudioAgents.Count; ++i)
            {
                if (AudioAgents[i] != null)
                {
                    AudioAgents[i].Stop(fadeout);
                }
            }
        }

        /// <summary>
        /// 音频轨道轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        public void Update(float elapseSeconds)
        {
            for (int i = 0; i < AudioAgents.Count; ++i)
            {
                if (AudioAgents[i] != null)
                {
                    AudioAgents[i].Update(elapseSeconds);
                }
            }
        }
    }
}
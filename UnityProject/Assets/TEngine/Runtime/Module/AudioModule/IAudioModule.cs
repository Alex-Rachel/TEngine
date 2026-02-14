using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using YooAsset;

namespace TEngine
{
    public interface IAudioModule
    {
        /// <summary>
        /// 总音量控制。
        /// </summary>
        public float Volume { get; set; }

        /// <summary>
        /// 总开关。
        /// </summary>
        public bool Enable { get; set; }

        /// <summary>
        /// 音乐音量
        /// </summary>
        public float MusicVolume { get; set; }

        /// <summary>
        /// 音效音量。
        /// </summary>
        public float SoundVolume { get; set; }

        /// <summary>
        /// UI音效音量。
        /// </summary>
        public float UISoundVolume { get; set; }

        /// <summary>
        /// 语音音量。
        /// </summary>
        public float VoiceVolume { get; set; }

        /// <summary>
        /// 音乐开关。
        /// </summary>
        public bool MusicEnable { get; set; }

        /// <summary>
        /// 音效开关。
        /// </summary>
        public bool SoundEnable { get; set; }

        /// <summary>
        /// UI音效开关。
        /// </summary>
        public bool UISoundEnable { get; set; }

        /// <summary>
        /// 语音开关。
        /// </summary>
        public bool VoiceEnable { get; set; }
        
        /// <summary>
        /// 音频混响器。
        /// </summary>
        public AudioMixer AudioMixer  { get;}
        
        /// <summary>
        /// 实例化根节点。
        /// </summary>
        public Transform InstanceRoot { get;}
        
        public Dictionary<string, AssetHandle> AudioClipPool  { get; set; }

        /// <summary>
        /// 初始化音频模块。
        /// </summary>
        /// <param name="audioGroupConfigs">音频轨道组配置。</param>
        /// <param name="instanceRoot">实例化根节点。</param>
        /// <param name="audioMixer">音频混响器。</param>
        /// <exception cref="GameFrameworkException"></exception>
        public void Initialize(AudioGroupConfig[] audioGroupConfigs, Transform instanceRoot = null, AudioMixer audioMixer = null);

        /// <summary>
        /// 重启音频模块。
        /// </summary>
        public void Restart();

        /// <summary>
        /// 播放音频接口。
        /// </summary>
        /// <remarks>如果超过最大发声数采用fadeout的方式复用最久播放的AudioSource。</remarks>
        /// <param name="type">声音类型。</param>
        /// <param name="path">声音文件路径。</param>
        /// <param name="bLoop">是否循环播放。</param>>
        /// <param name="volume">音量（0-1.0）。</param>
        /// <param name="bAsync">是否异步加载。</param>
        /// <param name="bInPool">是否支持资源池。</param>
        public AudioAgent Play(AudioType type, string path, bool bLoop = false, float volume = 1.0f, bool bAsync = false, bool bInPool = false);

        /// <summary>
        /// 停止某类声音播放。
        /// </summary>
        /// <param name="type">声音类型。</param>
        /// <param name="fadeout">是否渐消。</param>
        public void Stop(AudioType type, bool fadeout);

        /// <summary>
        /// 停止所有声音。
        /// </summary>
        /// <param name="fadeout">是否渐消。</param>
        public void StopAll(bool fadeout);

        /// <summary>
        /// 预先加载AudioClip，并放入对象池。
        /// </summary>
        /// <param name="list">AudioClip的AssetPath集合。</param>
        public void PutInAudioPool(List<string> list);

        /// <summary>
        /// 将部分AudioClip从对象池移出。
        /// </summary>
        /// <param name="list">AudioClip的AssetPath集合。</param>
        public void RemoveClipFromPool(List<string> list);

        /// <summary>
        /// 清空AudioClip的对象池。
        /// </summary>
        public void CleanSoundPool();
    }
}
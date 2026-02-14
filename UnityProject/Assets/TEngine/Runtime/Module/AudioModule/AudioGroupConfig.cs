using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace TEngine
{
    /// <summary>
    /// 音频轨道组配置。
    /// </summary>
    [Serializable]
    public sealed class AudioGroupConfig
    {
        [SerializeField]
        private string name = null;

        [SerializeField]
        private bool mute = false;

        [SerializeField, Range(0f, 1f)]
        private float volume = 1f;

        [SerializeField]
        private int agentHelperCount = 1;

        /// <summary>
        /// 音效分类，可分别关闭/开启对应分类音效。
        /// </summary>
        /// <remarks>命名与AudioMixer中分类名保持一致。</remarks>
        public AudioType AudioType;

        /// <summary>
        /// 音频源中3D声音的衰减模式。
        /// <remarks>Logarithmic - 当你想要实现现实世界的衰减时使用此模式。</remarks>
        /// <remarks>Linear - 当你想要随距离降低声音的音量时使用此模式。</remarks>
        /// <remarks>Custom -当你想要使用自定义衰减时使用此模式。</remarks>
        /// </summary>
        public AudioRolloffMode audioRolloffMode = AudioRolloffMode.Logarithmic;

        /// <summary>
        /// 最小距离。
        /// </summary>
        public float minDistance = 1f;

        /// <summary>
        /// 最大距离。
        /// </summary>
        public float maxDistance = 500f;

        /// <summary>
        /// 音频轨道组配置的名称。
        /// </summary>
        public string Name => name;

        /// <summary>
        /// 是否禁用。
        /// </summary>
        public bool Mute => mute;

        /// <summary>
        /// 音量大小。
        /// </summary>
        public float Volume => volume;

        /// <summary>
        /// 音频代理个数。
        /// <remarks>命名与AudioMixer中个数保持一致。</remarks>
        /// </summary>
        public int AgentHelperCount => agentHelperCount;
    }
}
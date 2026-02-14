using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fantasy.Helper;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Fantasy.IdFactory
{
    /// <summary>
    /// 表示一个运行时的ID。
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WorldRuntimeIdStruct
    {
        // RuntimeId: IsPool(1) + time(23) + SceneId(8) + WordId(8) + sequence(24) = 64 bits
        // +------------------+-------------------+--------------------------+-----------------------+--------------------------------------+
        // | IsPool(1) 对象池 |  time(23) 最大60天 | SceneId(8) 最多255个Scene | WordId(8) 最多255个世界 | sequence(24) 每秒每个进程能生产16777215个
        // +------------------+-------------------+--------------------------+-----------------------+--------------------------------------+
        public uint Time { get; private set; }
        public uint SceneId { get; private set; }
        public byte WordId { get; private set; }
        public uint Sequence { get; private set; }
        public bool IsPool { get; private set; }

        public const uint MaskSequence = 0xFFFFFF;   // 24位（从25位减少到24位，为 IsPool 腾出空间）
        public const uint MaskSceneId = 0xFF;        // 8位
        public const uint MaskWordId = 0xFF;         // 8位
        public const uint MaskTime = 0x7FFFFF;       // 23位（最高位留给 IsPool）
        
        /// <summary>
        /// WorldRuntimeIdStruct（如果超过下面参数的设定该ID会失效）。
        /// </summary>
        /// <param name="isPool">是否来自对象池</param>
        /// <param name="time">time不能超过8388607</param>
        /// <param name="sceneId">sceneId不能超过255</param>
        /// <param name="wordId">wordId不能超过255</param>
        /// <param name="sequence">sequence不能超过16777215（24位）</param>
        public WorldRuntimeIdStruct(bool isPool, uint time, uint sceneId, byte wordId, uint sequence)
        {
            // 因为都是在配置表里拿到参数、所以这个不做边界判定、能节省一点点性能。
            IsPool = isPool;
            Time = time;
            SceneId = sceneId;
            WordId = wordId;
            Sequence = sequence;
        }

        public static implicit operator long(WorldRuntimeIdStruct runtimeIdStruct)
        {
            ulong result = runtimeIdStruct.Sequence;                                                // 低24位: sequence
            result |= (ulong)runtimeIdStruct.WordId << 24;                                          // 第24-31位: wordId
            result |= (ulong)(runtimeIdStruct.SceneId % (runtimeIdStruct.WordId * 1000)) << 32;     // 第32-39位: sceneId
            result |= (ulong)runtimeIdStruct.Time << 40;                                            // 第40-62位: time
            result |= (runtimeIdStruct.IsPool ? 1UL : 0UL) << 63;                                   // 最高位63: isPool
            return (long)result;
        }

        public static implicit operator WorldRuntimeIdStruct(long runtimeId)
        {
            var result = (ulong)runtimeId;
            var wordId = (byte)((result >> 24) & MaskWordId);                               // 第24-31位: wordId
            var runtimeIdStruct = new WorldRuntimeIdStruct
            {
                Sequence = (uint)(result & MaskSequence),                                   // 低24位: sequence
                WordId = wordId,
                SceneId = (uint)((result >> 32) & MaskSceneId) + (uint)wordId * 1000,       // 第32-39位: sceneId
                Time = (uint)((result >> 40) & MaskTime),                                   // 第40-62位: time
                IsPool = ((result >> 63) & 1) == 1                                          // 最高位63: isPool
            };
            return runtimeIdStruct;
        }
    }

    public sealed class WorldRuntimeIdFactory : IRuntimeIdFactory
    {
        private readonly uint _sceneId;
        private readonly byte _worldId;
        
        private uint _lastTime;
        private uint _lastSequence;
        private readonly long _epochNow;
        private readonly long _epoch1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks / 10000;
        
        private WorldRuntimeIdFactory() { }

        public WorldRuntimeIdFactory(uint sceneId, byte worldId) : this(TimeHelper.Now, sceneId, worldId) { }

        public WorldRuntimeIdFactory(long epochNow, uint sceneId, byte worldId)
        {
            switch (sceneId)
            {
                case > 255255:
                {
                    throw new NotSupportedException($"sceneId:{sceneId} cannot be greater than 255255");
                }
                case < 1001:
                {
                    throw new NotSupportedException($"sceneId:{sceneId} cannot be less than 1001");
                }
                default:
                {
                    _sceneId = sceneId;
                    _worldId = worldId;
                    _epochNow = epochNow - _epoch1970;
                    break;
                }
            }
        }

        public long Create(bool isPool)
        {
            var time = (uint)((TimeHelper.Now - _epochNow) / 1000);

            if (time > _lastTime)
            {
                _lastTime = time;
                _lastSequence = 0;
            }
            else if (++_lastSequence > WorldRuntimeIdStruct.MaskSequence - 1)
            {
                _lastTime++;
                _lastSequence = 0;
            }

            return new WorldRuntimeIdStruct(isPool, time, _sceneId, _worldId, _lastSequence);
        }
    }

    public sealed class WorldRuntimeIdFactoryTool : IIdFactoryTool
    {
        /// <summary>
        /// 获取 RuntimeId 中的 IsPool 标志
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetIsPool(long runtimeId)
        {
            return GetIsPool(ref runtimeId);
        }

        /// <summary>
        /// 获取 RuntimeId 中的 IsPool 标志
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetIsPool(ref long runtimeId)
        {
            return (((ulong)runtimeId >> 63) & 1) == 1;                                     // 最高位
        }

        /// <summary>
        /// 获取 RuntimeId 中的时间部分
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetTime(ref long runtimeId)
        {
            var result = (ulong)runtimeId >> 40;                                            // 右移40位到第40-62位
            return (uint)(result & WorldRuntimeIdStruct.MaskTime);
        }

        /// <summary>
        /// 获取 RuntimeId 中的时间部分
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetTime(long runtimeId)
        {
            return GetTime(ref runtimeId);
        }

        /// <summary>
        /// 获取 RuntimeId 中的 SceneId 部分
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetSceneId(ref long runtimeId)
        {
            var result = (ulong)runtimeId >> 24;                                            // 右移24位
            var worldId = (uint)(result & WorldRuntimeIdStruct.MaskWordId) * 1000;        // 第24-31位: worldId
            result >>= 8;
            return (uint)(result & WorldRuntimeIdStruct.MaskSceneId) + worldId;           // 第32-39位: sceneId
        }

        /// <summary>
        /// 获取 RuntimeId 中的 SceneId 部分
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetSceneId(long runtimeId)
        {
            return GetSceneId(ref runtimeId);
        }

        /// <summary>
        /// 获取 RuntimeId 中的 WorldId 部分
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte GetWorldId(ref long runtimeId)
        {
            var result = (ulong)runtimeId >> 24;                                            // 右移24位到第24-31位
            return (byte)(result & WorldRuntimeIdStruct.MaskWordId);
        }

        /// <summary>
        /// 获取 RuntimeId 中的 WorldId 部分
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte GetWorldId(long runtimeId)
        {
            return GetWorldId(ref runtimeId);
        }

        /// <summary>
        /// 获取 RuntimeId 中的 Sequence 部分
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetSequence(ref long runtimeId)
        {
            return (uint)((ulong)runtimeId & WorldRuntimeIdStruct.MaskSequence);          // 低24位
        }

        /// <summary>
        /// 获取 RuntimeId 中的 Sequence 部分
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetSequence(long runtimeId)
        {
            return GetSequence(ref runtimeId);
        }
    }
}
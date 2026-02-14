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
    public struct RuntimeIdStruct
    {
        // RuntimeId: IsPool(1) + time(23) + SceneId(16) + sequence(24) = 64 bits
        // +------------------+-------------------+-----------------------------+--------------------------------------+
        // | IsPool(1) 对象池 |  time(23) 最大60天 | SceneId(16) 最多65535个Scene | sequence(24) 每秒每个进程能生产16777215个
        // +------------------+-------------------+-----------------------------+--------------------------------------+
        public uint Time { get; private set; }
        public uint SceneId { get; private set; }
        public uint Sequence { get; private set; }
        public bool IsPool { get; private set; }

        public const uint MaskSequence = 0xFFFFFF;   // 24位
        public const uint MaskSceneId = 0xFFFF;      // 16位
        public const uint MaskTime = 0x7FFFFF;       // 23位 (最高位留给 IsPool)

        /// <summary>
        /// RuntimeIdStruct（如果超过下面参数的设定该ID会失效）。
        /// </summary>
        /// <param name="isPool"></param>
        /// <param name="time">time不能超过8388607</param>
        /// <param name="sceneId">sceneId不能超过65535</param>
        /// <param name="sequence">sequence不能超过16777215</param>
        public RuntimeIdStruct(bool isPool, uint time, uint sceneId, uint sequence)
        {
            // 因为都是在配置表里拿到参数、所以这个不做边界判定、能节省一点点性能。
            IsPool = isPool;
            Time = time;
            SceneId = sceneId;
            Sequence = sequence;
        }

        public static implicit operator long(RuntimeIdStruct runtimeIdStruct)
        {
            ulong result = runtimeIdStruct.Sequence;                          // 低24位: sequence
            result |= (ulong)runtimeIdStruct.SceneId << 24;                   // 第24-39位: sceneId
            result |= (ulong)runtimeIdStruct.Time << 40;                      // 第40-62位: time
            result |= (runtimeIdStruct.IsPool ? 1UL : 0UL) << 63;             // 最高位63: isPool
            return (long)result;
        }

        public static implicit operator RuntimeIdStruct(long runtimeId)
        {
            var result = (ulong)runtimeId;
            var runtimeIdStruct = new RuntimeIdStruct
            {
                Sequence = (uint)(result & MaskSequence),                     // 低24位: sequence
                SceneId = (uint)((result >> 24) & MaskSceneId),              // 第24-39位: sceneId
                Time = (uint)((result >> 40) & MaskTime),                     // 第40-62位: time
                IsPool = ((result >> 63) & 1) == 1                            // 最高位63: isPool
            };
            return runtimeIdStruct;
        }
    }
    
    public sealed class RuntimeIdFactory : IRuntimeIdFactory
    {
        private readonly uint _sceneId;
        
        private uint _lastTime;
        private uint _lastSequence;
        private readonly long _epochNow;
        private readonly long _epoch1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks / 10000;
        
        private RuntimeIdFactory() { }

        public RuntimeIdFactory(uint sceneId) : this(TimeHelper.Now, sceneId) { }

        public RuntimeIdFactory(long epochNow, uint sceneId)
        {
            switch (sceneId)
            {
                case > 65535:
                {
                    throw new NotSupportedException($"sceneId:{sceneId} cannot be greater than 65535");
                }
                default:
                {
                    _sceneId = (ushort)sceneId;
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
            else if (++_lastSequence > RuntimeIdStruct.MaskSequence - 1)
            {
                _lastTime++;
                _lastSequence = 0;
            }

            return new RuntimeIdStruct(isPool, time, _sceneId, _lastSequence);
        }
    }

    public sealed class RuntimeIdFactoryTool : IIdFactoryTool
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
            return (((ulong)runtimeId >> 63) & 1) == 1;                       // 最高位
        }

        /// <summary>
        /// 获取 RuntimeId 中的时间部分
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetTime(ref long runtimeId)
        {
            var result = (ulong)runtimeId >> 40;  // 右移40位到第40-62位
            return (uint)(result & RuntimeIdStruct.MaskTime);
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
            var result = (ulong)runtimeId >> 24; // 右移24位到第24-39位
            return (uint)(result & RuntimeIdStruct.MaskSceneId);
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
        /// 获取 RuntimeId 中的 Sequence 部分
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetSequence(ref long runtimeId)
        {
            return (uint)((ulong)runtimeId & RuntimeIdStruct.MaskSequence);  // 低24位
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte GetWorldId(ref long entityId)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte GetWorldId(long entityId)
        {
            throw new NotImplementedException();
        }
    }
}
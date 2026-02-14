using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fantasy.Helper;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Fantasy.IdFactory
{
    /// <summary>
    /// 表示一个唯一实体的ID。
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WorldEntityIdStruct
    {
        // EntityId:39 + 8 + 8 + 18 =  64
        // +-------------------+--------------------------+-----------------------+------------------------------------+
        // |  time(30) 最大34年 | SceneId(8) 最多255个Scene | WordId(8) 最多255个世界 | sequence(18) 每秒每个进程能生产262143个
        // +-------------------+--------------------------+-----------------------+------------------------------------+
        public uint Time { get; private set; }
        public uint SceneId { get; private set; }
        public byte WordId { get; private set; }
        public uint Sequence { get; private set; }
        
        public const uint MaskSequence = 0x3FFFF;
        public const uint MaskSceneId = 0xFF;
        public const uint MaskWordId = 0xFF;
        public const uint MaskTime = 0x3FFFFFFF;

        /// <summary>
        /// WorldEntityIdStruct（如果超过下面参数的设定该ID会失效）。
        /// </summary>
        /// <param name="time">time不能超过1073741823</param>
        /// <param name="sceneId">sceneId不能超过255</param>
        /// <param name="wordId">wordId不能超过255</param>
        /// <param name="sequence">sequence不能超过262143</param>
        public WorldEntityIdStruct(uint time, uint sceneId, byte wordId, uint sequence)
        {
            // 因为都是在配置表里拿到参数、所以这个不做边界判定、能节省一点点性能。
            Time = time;
            SceneId = sceneId;
            WordId = wordId;
            Sequence = sequence;
        }

        public static implicit operator long(WorldEntityIdStruct entityIdStruct)
        {
            ulong result = 0;
            result |= entityIdStruct.Sequence;
            result |= (ulong)entityIdStruct.WordId << 18;
            result |= (ulong)(entityIdStruct.SceneId % (entityIdStruct.WordId * 1000)) << 26;
            result |= (ulong)entityIdStruct.Time << 34;
            return (long)result;
        }
        
        public static implicit operator WorldEntityIdStruct(long entityId)
        {
            var result = (ulong) entityId;
            var entityIdStruct = new WorldEntityIdStruct
            {
                Sequence = (uint)(result & MaskSequence)
            };
            result >>= 18;
            entityIdStruct.WordId = (byte)(result & MaskWordId);
            result >>= 8;
            entityIdStruct.SceneId = (uint)(result & MaskSceneId) + (uint)entityIdStruct.WordId * 1000;
            result >>= 8;
            entityIdStruct.Time = (uint)(result & MaskTime);
            return entityIdStruct;
        }
    }

    public sealed class WorldEntityIdFactory : IEntityIdFactory
    {
        private readonly uint _sceneId;
        private readonly byte _worldId;
        
        private uint _lastTime;
        private uint _lastSequence;
        private static readonly long Epoch1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks / 10000;
        private static readonly long EpochThisYear = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks / 10000 - Epoch1970;
    
        private WorldEntityIdFactory() { }
    
        public WorldEntityIdFactory(uint sceneId, byte worldId)
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
                    break;
                }
            }
        }
        
        public long Create
        {
            get
            {
                var time = (uint)((TimeHelper.Now - EpochThisYear) / 1000);
                
                if (time > _lastTime)
                {
                    _lastTime = time;
                    _lastSequence = 0;
                }
                else if (++_lastSequence > WorldEntityIdStruct.MaskSequence - 1)
                {
                    _lastTime++;
                    _lastSequence = 0;
                }

                return new WorldEntityIdStruct(time, _sceneId, _worldId, _lastSequence);
            }
        }
    }

    public sealed class WorldEntityIdFactoryTool : IIdFactoryTool
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetIsPool(ref long runtimeId)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetIsPool(long runtimeId)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetTime(ref long entityId)
        {
            var result = (ulong)entityId >> 34;
            return (uint)(result & WorldEntityIdStruct.MaskTime);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetTime(long entityId)
        {
            return GetTime(ref entityId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetSceneId(ref long entityId)
        {
            var result = (ulong)entityId >> 18;
            var worldId = (uint)(result & WorldEntityIdStruct.MaskWordId) * 1000;
            result >>= 8;
            return (uint)(result & WorldEntityIdStruct.MaskSceneId) + worldId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetSceneId(long entityId)
        {
            return GetSceneId(ref entityId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte GetWorldId(ref long entityId)
        {
            var result = (ulong)entityId >> 18;
            return (byte)(result & WorldEntityIdStruct.MaskWordId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte GetWorldId(long entityId)
        {
            return GetWorldId(ref entityId);
        }
    }
}
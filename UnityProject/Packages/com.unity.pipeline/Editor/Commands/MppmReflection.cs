#if UNITY_6000_3_OR_NEWER && MPPM_AVAILABLE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Unity.Pipeline.Editor.Commands
{
    internal static class MppmReflection
    {
        private const string MultiplayerTypeName = "Unity.Multiplayer.PlayMode.Editor.MultiplayerPlaymode";

        private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        static Type s_MultiplayerType;

        public static bool TryGetMultiplayerType(out Type type, out string error)
        {
            if (s_MultiplayerType != null)
            {
                type = s_MultiplayerType;
                error = null;
                return true;
            }

            foreach (var assembly in PipelineUtils.GetLoadedAssemblies())
            {
                var candidate = assembly.GetType(MultiplayerTypeName, false);
                if (candidate != null)
                {
                    s_MultiplayerType = candidate;
                    type = candidate;
                    error = null;
                    return true;
                }
            }

            type = null;
            error = $"MPPM type '{MultiplayerTypeName}' not found. Is com.unity.multiplayer.playmode installed and active?";
            return false;
        }

        public static List<MppmPlayer> ReadPlayers(out string error)
        {
            var players = new List<MppmPlayer>();
            if (!TryGetMultiplayerType(out _, out error))
                return players;

            for (int index = 1; index <= 4; index++)
            {
                if (!TryGetPlayer(index, out var player) || player == null)
                    continue;

                var name = ReadString(player, "Name");
                if (string.IsNullOrEmpty(name))
                    continue;

                players.Add(new MppmPlayer
                {
                    Name = name,
                    State = ReadString(player, "PlayerState"),
                    VpId = ReadVpId(player)
                });
            }

            return players;
        }

        public static string ReadVpId(object player)
        {
            var typeDependentInfo = ReadMember(player, "TypeDependentPlayerInfo");
            return ReadMember(typeDependentInfo, "VirtualProjectIdentifier")?.ToString();
        }

        public static object ReadMember(object instance, string memberName)
        {
            if (instance == null)
                return null;

            var property = instance.GetType().GetProperty(memberName, InstanceFlags);
            return property?.GetValue(instance);
        }

        public static bool TryGetPlayer(int index, out object player)
        {
            player = null;
            if (!TryGetMultiplayerType(out var type, out _))
                return false;

            var propertyName = index switch
            {
                1 => "PlayerOne",
                2 => "PlayerTwo",
                3 => "PlayerThree",
                4 => "PlayerFour",
                _ => null
            };
            if (propertyName == null)
                return false;

            var property = type.GetProperty(propertyName, StaticFlags);
            if (property == null)
                return false;

            player = property.GetValue(null);
            return player != null;
        }

        public static string ReadString(object instance, string memberName)
        {
            return ReadMember(instance, memberName)?.ToString();
        }

        public static List<string> ReadTags(object instance)
        {
            if (instance == null)
                return new List<string>();

            var property = instance.GetType().GetProperty("Tags", InstanceFlags);
            var value = property?.GetValue(instance);
            if (value is IEnumerable<string> tags)
                return tags.ToList();

            return new List<string>();
        }
    }
}
#endif

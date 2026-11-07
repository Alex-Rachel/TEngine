using System;

namespace Unity.Pipeline.Editor
{
    static class MppmInstanceInfo
    {
        public const string CloneFlag = "--virtual-project-clone";

        public const string VpIdFlag = "-vpId";

        public const string NameFlag = "-name";

        public const string RoleMain = "main";

        public const string RoleClone = "clone";

        public const string MainInstanceName = "Main Editor";

        public static bool IsClone => ParseIsClone(GetArgs());

        public static string Role => IsClone ? RoleClone : RoleMain;

        public static string VpId => ParseVpId(GetArgs());

        public static string InstanceName => ResolveInstanceName(GetArgs());

        public static bool ParseIsClone(string[] args) => HasFlag(args, CloneFlag);

        public static string ParseVpId(string[] args) => GetFlagValue(args, VpIdFlag);

        public static string ResolveInstanceName(string[] args)
        {
            if (!ParseIsClone(args))
                return MainInstanceName;

            var name = GetFlagValue(args, NameFlag);
            return string.IsNullOrEmpty(name) ? RoleClone : name;
        }

        private static string[] GetArgs() => Environment.GetCommandLineArgs();

        private static bool HasFlag(string[] args, string flag)
        {
            if (args == null)
                return false;

            foreach (var arg in args)
            {
                if (arg == flag || arg.StartsWith(flag + "=", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static string GetFlagValue(string[] args, string flag)
        {
            if (args == null)
                return null;

            var prefix = flag + "=";
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.StartsWith(prefix, StringComparison.Ordinal))
                    return arg.Substring(prefix.Length);
                if (arg == flag && i + 1 < args.Length)
                    return args[i + 1];
            }

            return null;
        }
    }
}

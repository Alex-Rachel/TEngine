using System;
using GameProto.Regicide;
using TEngine;

namespace GameLogic.Regicide
{
    public sealed class RegicideRuntimeConfig
    {
        public bool EnableRegicide = true;
        public RegicideClientEnvironment Environment = RegicideClientEnvironment.NetworkPlay;
        public bool StartAsHost;
        public string ServerAddress = "127.0.0.1";
        public ushort ServerPort = 7777;
        public int RuleId = 1;
        public int RandomSeed = 20260318;
        public string PlayerId = string.Empty;

        public static RegicideRuntimeConfig Load()
        {
            RegicideRuntimeConfig config = new RegicideRuntimeConfig
            {
                EnableRegicide = Utility.PlayerPrefs.GetBool("Regicide.Enable", true),
                StartAsHost = Utility.PlayerPrefs.GetBool("Regicide.StartAsHost", false),
                ServerAddress = Utility.PlayerPrefs.GetString("Regicide.Server.Address", "127.0.0.1"),
                ServerPort = (ushort)Utility.PlayerPrefs.GetInt("Regicide.Server.Port", 7777),
                RuleId = Utility.PlayerPrefs.GetInt("Regicide.RuleId", 1),
                RandomSeed = Utility.PlayerPrefs.GetInt("Regicide.Seed", 20260318),
                PlayerId = Utility.PlayerPrefs.GetString("Regicide.PlayerId", string.Empty),
            };

            string envValue = Utility.PlayerPrefs.GetString("Regicide.Environment", RegicideClientEnvironment.NetworkPlay.ToString());
            if (Enum.TryParse(envValue, true, out RegicideClientEnvironment env))
            {
                config.Environment = env;
            }

            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "--regicide-dedicated")
                {
                    config.Environment = RegicideClientEnvironment.DedicatedServer;
                    config.StartAsHost = false;
                }
                else if (arg == "--regicide-server")
                {
                    config.Environment = RegicideClientEnvironment.DedicatedServer;
                    config.StartAsHost = false;
                }
                else if (arg == "--regicide-host")
                {
                    config.Environment = RegicideClientEnvironment.NetworkPlay;
                    config.StartAsHost = true;
                }
                else if (arg == "--regicide-client")
                {
                    config.Environment = RegicideClientEnvironment.NetworkPlay;
                    config.StartAsHost = false;
                }
                else if (arg == "--regicide-single")
                {
                    config.Environment = RegicideClientEnvironment.LocalSinglePlayer;
                    config.StartAsHost = false;
                }
                else if (arg == "--regicide-address" && i + 1 < args.Length)
                {
                    config.ServerAddress = args[++i];
                }
                else if (arg == "--regicide-port" && i + 1 < args.Length && ushort.TryParse(args[i + 1], out ushort port))
                {
                    config.ServerPort = port;
                    i++;
                }
                else if (arg == "--regicide-player" && i + 1 < args.Length)
                {
                    config.PlayerId = args[++i];
                }
            }

            if (string.IsNullOrEmpty(config.PlayerId))
            {
                config.PlayerId = $"P{DateTime.UtcNow.Ticks % 100000}";
            }

            return config;
        }
    }
}

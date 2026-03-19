using Cysharp.Threading.Tasks;
using GameProto.Regicide;
using TEngine;
using UnityEngine;

namespace GameLogic.Regicide
{
    public enum RegicideMirrorLaunchMode
    {
        Host = 0,
        Client = 1,
        Server = 2,
    }

    public static class RegicideBootstrap
    {
        private const string DefaultSessionId = "REGICIDE-ROOM-001";
        private static bool _started;
        private static bool _modeSelected;
        private static RegicideRuntimeConfig _runtimeConfig;

        public static RegicideRuntimeConfig RuntimeConfig => _runtimeConfig;

        public static void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _modeSelected = false;
            _runtimeConfig = RegicideRuntimeConfig.Load();

            RegicideRuleConfigProvider configProvider = RegicideRuleConfigProvider.Instance;
            if (!configProvider.Validate(out string error))
            {
                Log.Error($"Regicide config validation failed: {error}");
            }

            SubscribeUiFlow();

            if (_runtimeConfig.Environment == RegicideClientEnvironment.DedicatedServer)
            {
                _modeSelected = true;
                StartServerModeAsync().Forget();
                return;
            }

            if (_runtimeConfig.Environment == RegicideClientEnvironment.LocalSinglePlayer)
            {
                _modeSelected = true;
                StartLocalSingleModeAsync().Forget();
                return;
            }

            // Ask mode before initializing Mirror runtime flow.
            GameModule.UI.ShowUIAsync<LoginUI>();
        }

        public static void SelectMirrorMode(RegicideMirrorLaunchMode mode)
        {
            if (!_started || _modeSelected)
            {
                return;
            }

            _modeSelected = true;
            switch (mode)
            {
                case RegicideMirrorLaunchMode.Host:
                    _runtimeConfig.Environment = RegicideClientEnvironment.NetworkPlay;
                    _runtimeConfig.StartAsHost = true;
                    StartClientModeAsync().Forget();
                    break;

                case RegicideMirrorLaunchMode.Client:
                    _runtimeConfig.Environment = RegicideClientEnvironment.NetworkPlay;
                    _runtimeConfig.StartAsHost = false;
                    StartClientModeAsync().Forget();
                    break;

                case RegicideMirrorLaunchMode.Server:
                    _runtimeConfig.Environment = RegicideClientEnvironment.DedicatedServer;
                    _runtimeConfig.StartAsHost = false;
                    StartServerModeAsync().Forget();
                    break;
            }
        }

        private static void EnsureRuntimeModules()
        {
            RegicideNetworkModule.Instance.Active();
            RegicideBattleModule.Instance.Active();
            RegicideBattleModule.Instance.Setup(_runtimeConfig);
        }

        private static async UniTaskVoid StartLocalSingleModeAsync()
        {
            EnsureRuntimeModules();
            RegicideBattleModule.Instance.TryStartLocalSinglePlayer();
            GameEvent.Send(RegicideEventIds.UiNavigateBattle);
            await UniTask.CompletedTask;
        }

        private static async UniTaskVoid StartClientModeAsync()
        {
            EnsureRuntimeModules();

            if (_runtimeConfig.StartAsHost)
            {
                RegicideAuthorityModule.Instance.Active();
            }

            bool connected = await RegicideNetworkModule.Instance.ConnectAsync(_runtimeConfig, asHost: _runtimeConfig.StartAsHost);
            if (!connected)
            {
                _modeSelected = false;
                Log.Warning("Regicide connect timeout, return to startup mode selection.");
                GameModule.UI.ShowUIAsync<LoginUI>();
                return;
            }

            if (_runtimeConfig.StartAsHost)
            {
                bool joined = await RegicideNetworkModule.Instance.JoinRoomAsync(DefaultSessionId, _runtimeConfig.PlayerId);
                if (!joined)
                {
                    await UniTask.Delay(200);
                    joined = await RegicideNetworkModule.Instance.JoinRoomAsync(DefaultSessionId, _runtimeConfig.PlayerId);
                }

                if (joined)
                {
                    GameEvent.Send(RegicideEventIds.UiNavigateRoom);
                    return;
                }

                RegicideErrorPayload error = RegicideNetworkModule.Instance.LastError;
                if (error != null && !string.IsNullOrEmpty(error.Message))
                {
                    Log.Warning($"Regicide host auto join room failed, fallback to lobby. code={error.Code} msg={error.Message}");
                }
                else
                {
                    Log.Warning("Regicide host auto join room failed, fallback to lobby.");
                }
            }

            GameEvent.Send(RegicideEventIds.UiNavigateLobby);
        }

        private static async UniTaskVoid StartServerModeAsync()
        {
            EnsureRuntimeModules();
            RegicideAuthorityModule.Instance.Active();

            bool serverReady = await RegicideNetworkModule.Instance.StartServerAsync(_runtimeConfig);
            if (!serverReady)
            {
                _modeSelected = false;
                Log.Error("Regicide dedicated server start failed.");
                if (!Application.isBatchMode)
                {
                    GameModule.UI.ShowUIAsync<LoginUI>();
                }
                return;
            }

            Log.Info($"Regicide dedicated server listening at {_runtimeConfig.ServerAddress}:{_runtimeConfig.ServerPort}");
        }

        private static void SubscribeUiFlow()
        {
            GameEvent.AddEventListener(RegicideEventIds.UiNavigateLobby, OnNavigateLobby);
            GameEvent.AddEventListener(RegicideEventIds.UiNavigateRoom, OnNavigateRoom);
            GameEvent.AddEventListener(RegicideEventIds.UiNavigateBattle, OnNavigateBattle);
            GameEvent.AddEventListener(RegicideEventIds.UiNavigateResult, OnNavigateResult);
        }

        public static void Shutdown()
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            _modeSelected = false;
            GameEvent.RemoveEventListener(RegicideEventIds.UiNavigateLobby, OnNavigateLobby);
            GameEvent.RemoveEventListener(RegicideEventIds.UiNavigateRoom, OnNavigateRoom);
            GameEvent.RemoveEventListener(RegicideEventIds.UiNavigateBattle, OnNavigateBattle);
            GameEvent.RemoveEventListener(RegicideEventIds.UiNavigateResult, OnNavigateResult);

            if (RegicideAuthorityModule.IsValid)
            {
                RegicideAuthorityModule.Instance.Release();
            }

            if (RegicideBattleModule.IsValid)
            {
                RegicideBattleModule.Instance.Release();
            }

            if (RegicideNetworkModule.IsValid)
            {
                RegicideNetworkModule.Instance.Release();
            }
        }

        private static void OnNavigateLobby()
        {
            GameModule.UI.CloseUI<LoginUI>();
            GameModule.UI.CloseUI<RoomUI>();
            GameModule.UI.CloseUI<RegicideBattleUI>();
            GameModule.UI.CloseUI<ResultUI>();
            GameModule.UI.ShowUIAsync<LobbyUI>();
        }

        private static void OnNavigateRoom()
        {
            GameModule.UI.CloseUI<LoginUI>();
            GameModule.UI.CloseUI<LobbyUI>();
            GameModule.UI.CloseUI<RegicideBattleUI>();
            GameModule.UI.CloseUI<ResultUI>();
            GameModule.UI.ShowUIAsync<RoomUI>();
        }

        private static void OnNavigateBattle()
        {
            GameModule.UI.CloseUI<LoginUI>();
            GameModule.UI.CloseUI<LobbyUI>();
            GameModule.UI.CloseUI<RoomUI>();
            GameModule.UI.CloseUI<ResultUI>();
            GameModule.UI.ShowUIAsync<RegicideBattleUI>();
        }

        private static void OnNavigateResult()
        {
            GameModule.UI.CloseUI<LoginUI>();
            GameModule.UI.CloseUI<LobbyUI>();
            GameModule.UI.CloseUI<RoomUI>();
            GameModule.UI.CloseUI<RegicideBattleUI>();
            GameModule.UI.ShowUIAsync<ResultUI>();
        }
    }
}

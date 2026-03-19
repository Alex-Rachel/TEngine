using System.Text;
using Cysharp.Threading.Tasks;
using GameLogic.Regicide;
using GameProto.Regicide;
using TEngine;
using UnityEngine.UI;

namespace GameLogic
{
    [Window(UILayer.UI, location: "ResultUI")]
    public sealed class ResultUI : UIWindow
    {
        private const int LeaveRoomTimeoutMs = 800;
        private const string ReplayTag = "-REPLAY-";
        private const string LocalReplaySessionId = "LOCAL-REPLAY";

        private Button _btnBackLobby;
        private Button _btnReplay;
        private Text _txtResult;
        private Text _txtBackLobbyLabel;
        private Text _txtReplayLabel;

        private bool _isHandlingAction;

        protected override void ScriptGenerator()
        {
            _btnBackLobby = FindChildComponent<Button>("m_btnBackLobby");
            _btnReplay = FindChildComponent<Button>("m_btnReplay");
            _txtResult = FindChildComponent<Text>("m_txtResult");
            _txtBackLobbyLabel = FindChildComponent<Text>("m_btnBackLobby/m_txtLabel");
            _txtReplayLabel = FindChildComponent<Text>("m_btnReplay/m_txtLabel");
        }

        protected override void RegisterEvent()
        {
            if (_btnBackLobby != null)
            {
                _btnBackLobby.onClick.AddListener(OnBtnBackLobbyClicked);
            }

            if (_btnReplay != null)
            {
                _btnReplay.onClick.AddListener(OnBtnReplayClicked);
            }
        }

        protected override void OnCreate()
        {
            if (_txtBackLobbyLabel != null)
            {
                _txtBackLobbyLabel.text = "返回大厅";
            }

            if (_txtReplayLabel != null)
            {
                _txtReplayLabel.text = "再来一局";
            }

            if (_txtResult == null)
            {
                return;
            }

            RegicideBattleState state = GameModule.RegicideBattle.State;
            if (state == null)
            {
                _txtResult.text = "对局结束";
                return;
            }

            _txtResult.text = state.IsVictory ? "胜利" : "失败";
        }

        private void OnBtnBackLobbyClicked()
        {
            if (_isHandlingAction)
            {
                return;
            }

            _isHandlingAction = true;
            SetButtonsInteractable(false);

            Close();
            GameEvent.Send(RegicideEventIds.UiNavigateLobby);
            LeaveCurrentRoomInBackgroundAsync().Forget();
        }

        private void OnBtnReplayClicked()
        {
            if (_isHandlingAction)
            {
                return;
            }

            ReplayAsync().Forget();
        }

        private async UniTaskVoid ReplayAsync()
        {
            _isHandlingAction = true;
            SetButtonsInteractable(false);
            SetResultText("正在创建再来一局房间...");

            RegicideRuntimeConfig runtime = RegicideBootstrap.RuntimeConfig;
            bool isNetwork = runtime != null && runtime.Environment == RegicideClientEnvironment.NetworkPlay;
            if (!isNetwork)
            {
                bool localOk = GameModule.RegicideBattle.TryStartLocalSinglePlayer(LocalReplaySessionId);
                if (localOk)
                {
                    Close();
                    GameEvent.Send(RegicideEventIds.UiNavigateBattle);
                    return;
                }

                SetResultText("再来一局失败。");
                ResetActionState();
                return;
            }

            string playerId = GetLocalPlayerId(runtime);
            if (string.IsNullOrEmpty(playerId))
            {
                SetResultText("玩家标识为空。");
                ResetActionState();
                return;
            }

            string currentSessionId = GetCurrentSessionId();
            if (string.IsNullOrEmpty(currentSessionId))
            {
                SetResultText("当前房间无效。");
                ResetActionState();
                return;
            }

            string replaySessionId = BuildReplaySessionId(currentSessionId);
            await LeaveRoomWithTimeoutAsync(currentSessionId, playerId);

            bool joined = await GameModule.RegicideNetwork.JoinRoomAsync(replaySessionId, playerId);
            if (joined)
            {
                Close();
                GameEvent.Send(RegicideEventIds.UiNavigateRoom);
                return;
            }

            SetResultText("创建再来一局房间失败。");
            ResetActionState();
        }

        private async UniTaskVoid LeaveCurrentRoomInBackgroundAsync()
        {
            RegicideRuntimeConfig runtime = RegicideBootstrap.RuntimeConfig;
            string playerId = GetLocalPlayerId(runtime);
            string sessionId = GetCurrentSessionId();
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            await LeaveRoomWithTimeoutAsync(sessionId, playerId);
        }

        private async UniTask LeaveRoomWithTimeoutAsync(string sessionId, string playerId)
        {
            UniTask<bool> leaveTask = GameModule.RegicideNetwork.LeaveRoomAsync(sessionId, playerId);
            UniTask timeoutTask = UniTask.Delay(LeaveRoomTimeoutMs);
            (bool hasResult, bool leaveOk) = await UniTask.WhenAny(leaveTask, timeoutTask);
            if (!hasResult || !leaveOk)
            {
                Log.Warning($"ResultUI leave room timeout or failed. session={sessionId} player={playerId}");
            }
        }

        private static string GetLocalPlayerId(RegicideRuntimeConfig runtime)
        {
            if (!string.IsNullOrEmpty(GameModule.RegicideBattle.LocalPlayerId))
            {
                return GameModule.RegicideBattle.LocalPlayerId;
            }

            return runtime != null ? runtime.PlayerId : string.Empty;
        }

        private string GetCurrentSessionId()
        {
            RegicideRoomSnapshot room = GameModule.RegicideNetwork.RoomSnapshot;
            if (room != null && !string.IsNullOrEmpty(room.SessionId))
            {
                return room.SessionId;
            }

            RegicideBattleState state = GameModule.RegicideBattle.State;
            return state != null ? state.SessionId : string.Empty;
        }

        private static string BuildReplaySessionId(string currentSessionId)
        {
            string baseSessionId = string.IsNullOrEmpty(currentSessionId) ? "REGICIDE-ROOM" : currentSessionId;
            string token = BuildReplayToken();
            return $"{baseSessionId}{ReplayTag}{token}";
        }

        private static string BuildReplayToken()
        {
            RegicideStateSnapshotPayload snapshot = GameModule.RegicideNetwork.StateSnapshot;
            if (snapshot != null && !string.IsNullOrEmpty(snapshot.StateHash))
            {
                return NormalizeToken(snapshot.StateHash);
            }

            RegicideBattleState state = GameModule.RegicideBattle.State;
            if (state != null)
            {
                return NormalizeToken(RegicideStateHasher.ComputeHash(state));
            }

            return RegicideClock.NowUnixMilliseconds().ToString();
        }

        private static string NormalizeToken(string rawToken)
        {
            if (string.IsNullOrEmpty(rawToken))
            {
                return "NEXT";
            }

            StringBuilder builder = new StringBuilder(12);
            for (int i = 0; i < rawToken.Length; i++)
            {
                char c = rawToken[i];
                if (!char.IsLetterOrDigit(c))
                {
                    continue;
                }

                builder.Append(char.ToUpperInvariant(c));
                if (builder.Length >= 12)
                {
                    break;
                }
            }

            return builder.Length > 0 ? builder.ToString() : "NEXT";
        }

        private void SetButtonsInteractable(bool interactable)
        {
            if (_btnBackLobby != null)
            {
                _btnBackLobby.interactable = interactable;
            }

            if (_btnReplay != null)
            {
                _btnReplay.interactable = interactable;
            }
        }

        private void SetResultText(string text)
        {
            if (_txtResult != null)
            {
                _txtResult.text = text;
            }
        }

        private void ResetActionState()
        {
            _isHandlingAction = false;
            SetButtonsInteractable(true);
        }
    }
}

using Cysharp.Threading.Tasks;
using GameLogic.Regicide;
using GameProto.Regicide;
using TEngine;
using UnityEngine.UI;

namespace GameLogic
{
    [Window(UILayer.UI, location: "LobbyUI")]
    public sealed class LobbyUI : UIWindow
    {
        private const string DefaultSessionId = "REGICIDE-ROOM-001";

        private Button _btnJoin;
        private Button _btnSingle;
        private Text _txtStatus;
        private Text _txtJoinLabel;
        private Text _txtSingleLabel;

        protected override void ScriptGenerator()
        {
            _btnJoin = FindChildComponent<Button>("m_btnJoin");
            _btnSingle = FindChildComponent<Button>("m_btnSingle");
            _txtStatus = FindChildComponent<Text>("m_txtStatus");
            _txtJoinLabel = FindChildComponent<Text>("m_btnJoin/m_txtLabel");
            _txtSingleLabel = FindChildComponent<Text>("m_btnSingle/m_txtLabel");
        }

        protected override void RegisterEvent()
        {
            AddUIEvent<RegicideConnectionState>(RegicideEventIds.ConnectionStateChanged, OnConnectionChanged);
            AddUIEvent<RegicideRoomSnapshot>(RegicideEventIds.RoomSnapshotUpdated, OnRoomSnapshotUpdated);

            if (_btnJoin != null)
            {
                _btnJoin.onClick.AddListener(OnBtnJoinClicked);
            }

            if (_btnSingle != null)
            {
                _btnSingle.onClick.AddListener(OnBtnSingleClicked);
            }
        }

        protected override void OnCreate()
        {
            if (_txtStatus != null)
            {
                _txtStatus.text = "弑君者大厅";
            }

            if (_txtJoinLabel != null)
            {
                _txtJoinLabel.text = "进入房间";
            }

            if (_txtSingleLabel != null)
            {
                _txtSingleLabel.text = "单人练习";
            }
        }

        private void OnBtnJoinClicked()
        {
            JoinRoomAsync().Forget();
        }

        private async UniTaskVoid JoinRoomAsync()
        {
            RegicideRuntimeConfig config = RegicideBootstrap.RuntimeConfig;
            if (config == null)
            {
                return;
            }

            bool joined = await GameModule.RegicideNetwork.JoinRoomAsync(DefaultSessionId, config.PlayerId);
            if (joined)
            {
                GameEvent.Send(RegicideEventIds.UiNavigateRoom);
            }
            else if (_txtStatus != null)
            {
                _txtStatus.text = "进入房间失败。";
            }
        }

        private void OnBtnSingleClicked()
        {
            bool ok = GameModule.RegicideBattle.TryStartLocalSinglePlayer();
            if (ok)
            {
                GameEvent.Send(RegicideEventIds.UiNavigateBattle);
            }
        }

        private void OnConnectionChanged(RegicideConnectionState state)
        {
            if (_txtStatus == null || state == null)
            {
                return;
            }

            _txtStatus.text = state.IsConnected
                ? $"已连接：{state.Address}:{state.Port}"
                : "连接已断开";
        }

        private void OnRoomSnapshotUpdated(RegicideRoomSnapshot room)
        {
            if (_txtStatus == null || room == null)
            {
                return;
            }

            _txtStatus.text = $"房间：{room.SessionId}，玩家：{room.ConnectedPlayers}/{room.TargetPlayers}（上限{room.MaxPlayers}）";
            if (room.Started)
            {
                GameEvent.Send(RegicideEventIds.UiNavigateBattle);
            }
        }
    }
}

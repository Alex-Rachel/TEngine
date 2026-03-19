using Cysharp.Threading.Tasks;
using GameLogic.Regicide;
using GameProto.Regicide;
using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    [Window(UILayer.UI, location: "RoomUI")]
    public sealed class RoomUI : UIWindow
    {
        private const int MinPlayerCount = 1;
        private const int MaxPlayerCount = 4;

        private Button _btnReady;
        private Button _btnStart;
        private Button _btnBack;
        private Button _btnPlayerMinus;
        private Button _btnPlayerPlus;

        private Text _txtRoom;
        private Text _txtReadyLabel;
        private Text _txtPlayerCount;
        private Text _txtStartLabel;
        private Text _txtBackLabel;

        private bool _isReady;
        private bool _isLeavingRoom;

        protected override void ScriptGenerator()
        {
            _btnReady = FindChildComponent<Button>("m_btnReady");
            _btnStart = FindChildComponent<Button>("m_btnStart");
            _btnBack = FindChildComponent<Button>("m_btnBack");
            _txtRoom = FindChildComponent<Text>("m_txtRoom");
            _txtReadyLabel = FindChildComponent<Text>("m_btnReady/m_txtLabel");
            _txtStartLabel = FindChildComponent<Text>("m_btnStart/m_txtLabel");
            _txtBackLabel = FindChildComponent<Text>("m_btnBack/m_txtLabel");

            _btnPlayerMinus = FindChildComponent<Button>("m_btnPlayerMinus");
            _btnPlayerPlus = FindChildComponent<Button>("m_btnPlayerPlus");
            _txtPlayerCount = FindChildComponent<Text>("m_txtPlayerCount");

            EnsurePlayerCountControls();
        }

        protected override void RegisterEvent()
        {
            AddUIEvent<RegicideRoomSnapshot>(RegicideEventIds.RoomSnapshotUpdated, OnRoomSnapshotUpdated);
            AddUIEvent<RegicideErrorPayload>(RegicideEventIds.BattleErrorReceived, OnErrorReceived);

            if (_btnReady != null)
            {
                _btnReady.onClick.AddListener(OnBtnReadyClicked);
            }

            if (_btnStart != null)
            {
                _btnStart.onClick.AddListener(OnBtnStartClicked);
            }

            if (_btnBack != null)
            {
                _btnBack.onClick.AddListener(OnBtnBackClicked);
            }

            if (_btnPlayerMinus != null)
            {
                _btnPlayerMinus.onClick.AddListener(OnBtnPlayerMinusClicked);
            }

            if (_btnPlayerPlus != null)
            {
                _btnPlayerPlus.onClick.AddListener(OnBtnPlayerPlusClicked);
            }
        }

        protected override void OnCreate()
        {
            if (_txtRoom != null)
            {
                _txtRoom.text = "等待房间状态...";
            }

            if (_txtStartLabel != null)
            {
                _txtStartLabel.text = "开始对局";
            }

            if (_txtBackLabel != null)
            {
                _txtBackLabel.text = "返回大厅";
            }

            UpdateReadyButton(false);
            UpdatePlayerCountText(null);
            RefreshFromSnapshot(GameModule.RegicideNetwork.RoomSnapshot);
        }

        private void OnBtnReadyClicked()
        {
            SetReadyAsync(!_isReady).Forget();
        }

        private async UniTaskVoid SetReadyAsync(bool ready)
        {
            RegicideRoomSnapshot room = GameModule.RegicideNetwork.RoomSnapshot;
            if (room == null || string.IsNullOrEmpty(room.SessionId))
            {
                return;
            }

            string playerId = GetLocalPlayerId();
            if (string.IsNullOrEmpty(playerId))
            {
                if (_txtRoom != null)
                {
                    _txtRoom.text = "玩家标识为空。";
                }
                return;
            }

            bool ok = await GameModule.RegicideNetwork.SetReadyAsync(room.SessionId, playerId, ready);
            if (!ok && _txtRoom != null)
            {
                _txtRoom.text = ready ? "准备失败。" : "取消准备失败。";
            }
        }

        private void OnBtnStartClicked()
        {
            StartMatchAsync().Forget();
        }

        private async UniTaskVoid StartMatchAsync()
        {
            RegicideRoomSnapshot room = GameModule.RegicideNetwork.RoomSnapshot;
            if (room == null || string.IsNullOrEmpty(room.SessionId))
            {
                return;
            }

            string playerId = GetLocalPlayerId();
            if (string.IsNullOrEmpty(playerId))
            {
                if (_txtRoom != null)
                {
                    _txtRoom.text = "玩家标识为空。";
                }
                return;
            }

            bool ok = await GameModule.RegicideNetwork.StartMatchAsync(room.SessionId, playerId);
            if (!ok && _txtRoom != null)
            {
                _txtRoom.text = "开始对局失败。";
            }
        }

        private void OnBtnBackClicked()
        {
            if (_isLeavingRoom)
            {
                return;
            }

            _isLeavingRoom = true;
            _btnBack.interactable = false;

            // 先跳大厅，离房异步后台处理，避免点击一次无响应。
            GameEvent.Send(RegicideEventIds.UiNavigateLobby);
            Close();
            LeaveRoomInBackgroundAsync().Forget();
        }

        private async UniTaskVoid LeaveRoomInBackgroundAsync()
        {
            RegicideRoomSnapshot room = GameModule.RegicideNetwork.RoomSnapshot;
            string playerId = GetLocalPlayerId();
            if (room != null && !string.IsNullOrEmpty(room.SessionId) && !string.IsNullOrEmpty(playerId))
            {
                UniTask<bool> leaveTask = GameModule.RegicideNetwork.LeaveRoomAsync(room.SessionId, playerId);
                UniTask timeoutTask = UniTask.Delay(800);
                (bool hasLeaveResult, bool leaveResult) = await UniTask.WhenAny(leaveTask, timeoutTask);
                if (!hasLeaveResult || !leaveResult)
                {
                    Log.Warning($"RoomUI leave room timeout or failed. session={room.SessionId} player={playerId}");
                }
            }
        }

        private void OnBtnPlayerMinusClicked()
        {
            ChangeTargetPlayersAsync(-1).Forget();
        }

        private void OnBtnPlayerPlusClicked()
        {
            ChangeTargetPlayersAsync(1).Forget();
        }

        private async UniTaskVoid ChangeTargetPlayersAsync(int delta)
        {
            RegicideRoomSnapshot room = GameModule.RegicideNetwork.RoomSnapshot;
            if (room == null || string.IsNullOrEmpty(room.SessionId))
            {
                return;
            }

            string playerId = GetLocalPlayerId();
            if (string.IsNullOrEmpty(playerId))
            {
                if (_txtRoom != null)
                {
                    _txtRoom.text = "玩家标识为空。";
                }
                return;
            }

            int maxPlayers = Mathf.Clamp(room.MaxPlayers, MinPlayerCount, MaxPlayerCount);
            int currentTarget = Mathf.Clamp(room.TargetPlayers > 0 ? room.TargetPlayers : maxPlayers, MinPlayerCount, maxPlayers);
            int nextTarget = Mathf.Clamp(currentTarget + delta, MinPlayerCount, maxPlayers);
            if (nextTarget == currentTarget)
            {
                return;
            }

            bool ok = await GameModule.RegicideNetwork.SetRoomTargetPlayersAsync(room.SessionId, playerId, nextTarget);
            if (!ok && _txtRoom != null)
            {
                _txtRoom.text = "设置人数失败。";
            }
        }

        private void OnRoomSnapshotUpdated(RegicideRoomSnapshot room)
        {
            RefreshFromSnapshot(room);
            if (room != null && room.Started)
            {
                Close();
                GameEvent.Send(RegicideEventIds.UiNavigateBattle);
            }
        }

        private void RefreshFromSnapshot(RegicideRoomSnapshot room)
        {
            if (room == null)
            {
                return;
            }

            string localPlayerId = GetLocalPlayerId();
            bool ready = false;
            bool inRoom = false;
            for (int i = 0; i < room.Seats.Count; i++)
            {
                RegicideRoomSeatSnapshot seat = room.Seats[i];
                if (seat != null && seat.PlayerId == localPlayerId)
                {
                    ready = seat.IsReady;
                    inRoom = true;
                    break;
                }
            }

            _isReady = inRoom && ready;
            UpdateReadyButton(_isReady);
            UpdatePlayerCountText(room);

            if (_txtRoom != null)
            {
                _txtRoom.text =
                    $"房间号：{room.SessionId}\n" +
                    $"玩家人数：{room.ConnectedPlayers}/{room.TargetPlayers}（上限{room.MaxPlayers}）\n" +
                    $"准备人数：{room.ReadyPlayers}/{room.TargetPlayers}";
            }

            if (_btnStart != null)
            {
                _btnStart.interactable = room.ConnectedPlayers >= room.TargetPlayers && room.ReadyPlayers >= room.TargetPlayers && !room.Started;
            }

            if (_btnPlayerMinus != null)
            {
                _btnPlayerMinus.interactable = !room.Started && room.TargetPlayers > MinPlayerCount && room.TargetPlayers > room.ConnectedPlayers;
            }

            if (_btnPlayerPlus != null)
            {
                _btnPlayerPlus.interactable = !room.Started && room.TargetPlayers < Mathf.Clamp(room.MaxPlayers, MinPlayerCount, MaxPlayerCount);
            }
        }

        private void OnErrorReceived(RegicideErrorPayload error)
        {
            if (_txtRoom != null && error != null)
            {
                _txtRoom.text = error.Message;
            }
        }

        private void UpdateReadyButton(bool ready)
        {
            if (_txtReadyLabel != null)
            {
                _txtReadyLabel.text = ready ? "取消准备" : "准备";
            }
        }

        private void UpdatePlayerCountText(RegicideRoomSnapshot room)
        {
            if (_txtPlayerCount == null)
            {
                return;
            }

            if (room == null)
            {
                _txtPlayerCount.text = "目标人数：1";
                return;
            }

            _txtPlayerCount.text = $"目标人数：{room.TargetPlayers}";
        }

        private string GetLocalPlayerId()
        {
            string playerId = GameModule.RegicideBattle.LocalPlayerId;
            if (!string.IsNullOrEmpty(playerId))
            {
                return playerId;
            }

            RegicideRuntimeConfig runtime = RegicideBootstrap.RuntimeConfig;
            return runtime != null ? runtime.PlayerId : string.Empty;
        }

        private void EnsurePlayerCountControls()
        {
            if (_btnPlayerMinus != null && _btnPlayerPlus != null && _txtPlayerCount != null)
            {
                return;
            }

            RectTransform root = rectTransform;
            if (root == null)
            {
                return;
            }

            GameObject panelGo = new GameObject("m_goPlayerCountPanel", typeof(RectTransform));
            RectTransform panelRect = panelGo.GetComponent<RectTransform>();
            panelRect.SetParent(root, false);
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = new Vector2(0f, 130f);
            panelRect.sizeDelta = new Vector2(560f, 70f);

            if (_btnReady != null && _txtRoom != null)
            {
                _btnPlayerMinus = CreateButtonFromTemplate(_btnReady, panelRect, "m_btnPlayerMinus", new Vector2(-200f, 0f), new Vector2(90f, 58f), "-");
                _btnPlayerPlus = CreateButtonFromTemplate(_btnReady, panelRect, "m_btnPlayerPlus", new Vector2(200f, 0f), new Vector2(90f, 58f), "+");
                _txtPlayerCount = CreateTextFromTemplate(_txtRoom, panelRect, "m_txtPlayerCount", new Vector2(0f, 0f), new Vector2(260f, 58f), 28, TextAnchor.MiddleCenter);
            }

            if (_btnPlayerMinus == null || _btnPlayerPlus == null || _txtPlayerCount == null)
            {
                Log.Warning("RoomUI 人数控件创建失败，缺少模板节点。");
                return;
            }

            _txtPlayerCount.text = "目标人数：1";
        }

        private static Button CreateButtonFromTemplate(Button template, Transform parent, string name, Vector2 anchoredPos, Vector2 size, string label)
        {
            GameObject go = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
            go.name = name;

            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect == null)
            {
                return null;
            }

            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;

            Button button = go.GetComponent<Button>();
            if (button == null)
            {
                return null;
            }

            Text txt = go.transform.Find("m_txtLabel")?.GetComponent<Text>();
            if (txt != null)
            {
                txt.text = label;
            }

            return button;
        }

        private static Text CreateTextFromTemplate(Text template, Transform parent, string name, Vector2 anchoredPos, Vector2 size, int fontSize, TextAnchor alignment)
        {
            GameObject go = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
            go.name = name;

            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect == null)
            {
                return null;
            }

            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;

            Text text = go.GetComponent<Text>();
            if (text == null)
            {
                return null;
            }

            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }
}

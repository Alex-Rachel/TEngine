using Cysharp.Threading.Tasks;
using Game.UI;
using UnityEngine;
using UnityEngine.UI;
using TEngine;

namespace GameLogic
{
    [Window(UILayer.UI, fullScreen: false, -1)]
    class BattleMainUI : UIWindow<gen_BattleMainUI>
    {
        private UIRoleInfoWidget _uiRoleInfoWidget;
        private UIMonsterInfoWidget _uiMonsterInfoWidget;

        protected override async UniTask OnInitializeAsync()
        {
            baseui.UIMonsterInfoWidget.RectInfo.gameObject.SetActive(true);

            Debug.Log("BattleMainUI OnInitialize");
        }

        protected override async UniTask OnOpenAsync()
        {
            _uiMonsterInfoWidget = await CreateWidgetAsync<UIMonsterInfoWidget>(baseui.UIMonsterInfoWidget);
            _uiRoleInfoWidget = await CreateWidgetAsync<UIRoleInfoWidget>(baseui.RectTopInfo);
        }

        protected override void OnRegisterEvent(GameEventMgr proxy)
        {
            base.OnRegisterEvent(proxy);
            proxy.AddEvent(ILoginUI_Event.ShowLoginUI, OnShowLoginUI);
            Debug.Log("BattleMainUI OnRegisterEvent");
        }

        private void OnShowLoginUI()
        {
            Debug.Log("OnShowLoginUI");
        }

        protected override void OnUpdate()
        {
            Debug.Log("Update BattleMainUI");
        }
    }
}
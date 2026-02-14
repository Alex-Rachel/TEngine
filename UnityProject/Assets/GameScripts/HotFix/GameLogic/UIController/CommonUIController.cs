using TEngine;

namespace GameLogic
{
    public class CommonUIController : IUIController
    {
        public void RegUIMessage()
        {
            GameEvent.AddEventListener(ILoginUI_Event.OnLoginSuccess, OnLoginSuccess);
        }

        private void OnLoginSuccess()
        {
            UIModule.Instance.ShowUIAsync<BattleMainUI>();
        }
    }
}
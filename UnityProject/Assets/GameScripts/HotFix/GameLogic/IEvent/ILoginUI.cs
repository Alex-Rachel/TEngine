using TEngine;

namespace GameLogic
{
    [EventInterface(EEventGroup.GroupUI)]
    public interface ILoginUI
    {
        void OnLoginSuccess();

        void ShowLoginUI();

        void CloseLoginUI();
    }
}
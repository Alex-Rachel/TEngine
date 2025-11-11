using Game.UI;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    public class UIRoleInfoWidget:UIWidget<gen_UIRoleInfoWidget>
    {
        protected override void OnInitialize()
        {
            base.OnInitialize();
            Debug.Log("UIRoleInfoWidget OnInitialize");
        }
    }
}
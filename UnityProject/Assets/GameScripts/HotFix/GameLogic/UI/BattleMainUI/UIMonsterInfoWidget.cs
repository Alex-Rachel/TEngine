using Game.UI;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    public class UIMonsterInfoWidget:UIWidget<gen_UIMonsterInfoWidget>
    {
        protected override void OnInitialize()
        {
            base.OnInitialize();
            Debug.Log("MonsterInfoWidget OnInitialize");
        }
    }
}
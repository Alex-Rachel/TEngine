using UnityEngine;
using UnityEngine.UI;
using TEngine;

namespace GameLogic
{
    [Window(UILayer.UI,location:"BattleMainUI")]
    class BattleMainUI : UIWindow
    {
        #region 脚本工具生成的代码
        private RectTransform _rectContainer;
        private GameObject _itemTouch;
        private GameObject _goTopInfo;
        private GameObject _itemRoleInfo;
        private GameObject _itemMonsterInfo;
        protected override void ScriptGenerator()
        {
            _rectContainer = FindChildComponent<RectTransform>("m_rectContainer");
            _itemTouch = FindChild("m_rectContainer/m_itemTouch").gameObject;
            _goTopInfo = FindChild("m_goTopInfo").gameObject;
            _itemRoleInfo = FindChild("m_goTopInfo/m_itemRoleInfo").gameObject;
            _itemMonsterInfo = FindChild("m_goTopInfo/m_itemMonsterInfo").gameObject;
        }
        #endregion

        #region 事件
        #endregion

    }
}
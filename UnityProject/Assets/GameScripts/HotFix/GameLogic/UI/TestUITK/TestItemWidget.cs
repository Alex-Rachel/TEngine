using System;
using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// 测试用 Widget 子组件。
    /// </summary>
    public class TestItemWidget : UITKWidget
    {
        private Label _lblIndex;
        private Label _lblItemName;
        private Button _btnRemove;

        private int _index;
        public Action<int> OnRemoveClicked;

        protected override void OnCreate()
        {
            _lblIndex = RootElement.Q<Label>("lbl-index");
            _lblItemName = RootElement.Q<Label>("lbl-item-name");
            _btnRemove = RootElement.Q<Button>("btn-remove");

            _btnRemove.clicked += HandleRemove;
        }

        public override void OnBindData(object data, int index)
        {
            _index = index;
            string itemName = data as string ?? "";
            _lblIndex.text = $"#{index + 1}";
            _lblItemName.text = itemName;
        }

        private void HandleRemove()
        {
            OnRemoveClicked?.Invoke(_index);
        }

        protected override void OnDestroy()
        {
            if (_btnRemove != null)
                _btnRemove.clicked -= HandleRemove;
        }
    }
}

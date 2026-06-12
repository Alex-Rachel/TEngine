using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace GameLogic
{
    /// <summary>
    /// 泛型虚拟化列表。封装 Unity ListView，对齐 Widget 生命周期。
    /// </summary>
    public class UITKListView<T> : UITKWidget where T : UITKWidget, new()
    {
        private ListView _listView;
        private readonly List<T> _activeWidgets = new();
        private readonly Queue<T> _widgetPool = new();
        private IList _dataSource;
        private Action _unbindListAction;

        /// <summary>
        /// 列表项选中事件。
        /// </summary>
        public event Action<object, int> OnItemSelected;

        /// <summary>
        /// 设置固定行高（启用后性能更优）。
        /// </summary>
        public int FixedItemHeight
        {
            set => _listView.fixedItemHeight = value;
        }

        protected override void OnCreate()
        {
            _listView = RootElement.Q<ListView>();
            if (_listView == null)
            {
                _listView = RootElement as ListView;
            }
            if (_listView == null)
            {
                TEngine.Log.Error($"UITKListView<{typeof(T).Name}>: No ListView found in root element");
                return;
            }

            _listView.makeItem = MakeItem;
            _listView.bindItem = BindItem;
            _listView.unbindItem = UnbindItem;
            _listView.selectionChanged += OnSelectionChanged;
        }

        /// <summary>
        /// 手动设置数据源（非 MVVM 场景）。
        /// </summary>
        public void SetData(IList data)
        {
            _dataSource = data;
            _listView.itemsSource = data;
            _listView.Rebuild();
        }

        /// <summary>
        /// 绑定 BindableList，自动监听增删改刷新。
        /// </summary>
        public void BindList<TData>(BindableList<TData> source)
        {
            _dataSource = source.InternalList;
            _listView.itemsSource = source.InternalList;

            Action onChanged = () => _listView.Rebuild();
            source.OnListChanged += onChanged;

            _unbindListAction = () =>
            {
                source.OnListChanged -= onChanged;
            };

            _listView.Rebuild();
        }

        /// <summary>
        /// 解绑列表数据源。
        /// </summary>
        public void UnbindList()
        {
            _unbindListAction?.Invoke();
            _unbindListAction = null;
        }

        public void RefreshItem(int index) => _listView.RefreshItem(index);
        public void RefreshAll() => _listView.Rebuild();

        private VisualElement MakeItem()
        {
            T widget;
            if (_widgetPool.Count > 0)
            {
                widget = _widgetPool.Dequeue();
                widget.RootElement.style.display = DisplayStyle.Flex;
            }
            else
            {
                widget = new T();
                var asset = UITKModule.Resource.LoadVisualTreeAsset(typeof(T).Name);
                if (asset == null)
                {
                    TEngine.Log.Error($"UITKListView<{typeof(T).Name}>: 列表项 UXML 加载失败 ({typeof(T).Name})");
                    return new VisualElement();  // 占位，避免 ListView 内部因 null 崩溃
                }
                var root = asset.CloneTree();
                widget.Create(this, root, true);
            }
            _activeWidgets.Add(widget);
            return widget.RootElement;
        }

        private void BindItem(VisualElement element, int index)
        {
            var widget = FindWidgetByElement(element);
            if (widget != null && _dataSource != null && index < _dataSource.Count)
            {
                widget.OnBindData(_dataSource[index], index);
            }
        }

        private void UnbindItem(VisualElement element, int index)
        {
            var widget = FindWidgetByElement(element);
            widget?.OnUnbindData();
        }

        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            foreach (var item in selection)
            {
                int idx = _dataSource?.IndexOf(item) ?? -1;
                OnItemSelected?.Invoke(item, idx);
            }
        }

        private T FindWidgetByElement(VisualElement element)
        {
            for (int i = 0; i < _activeWidgets.Count; i++)
            {
                if (_activeWidgets[i].RootElement == element)
                    return _activeWidgets[i];
            }
            return null;
        }

        protected override void OnDestroy()
        {
            UnbindList();
            _activeWidgets.Clear();
            _widgetPool.Clear();
        }
    }
}

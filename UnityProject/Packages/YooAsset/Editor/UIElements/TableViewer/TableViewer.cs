#if UNITY_2019_4_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace YooAsset.Editor
{
    /// <summary>
    /// Unity2022版本以上推荐官方类：MultiColumnListView组件
    /// </summary>
    public class TableViewer : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<TableViewer, UxmlTraits>
        {
        }

        private readonly Toolbar _toolbar;
        private readonly ListView _listView;

        private readonly List<TableColumn> _columns = new List<TableColumn>(10);
        private List<ITableData> _itemsSource;
        private List<ITableData> _sortingDatas;

        // 排序相关
        private string _sortingHeader;
        private bool _descendingSort = true;

        /// <summary>
        /// 数据源
        /// </summary>
        public List<ITableData> itemsSource
        {
            get
            {
                return _itemsSource;
            }
            set
            {
                if (CheckItemsSource(value))
                {
                    _itemsSource = value;
                    _sortingDatas = value;
                }
            }
        }

        /// <summary>
        /// 选中的数据列表
        /// </summary>
        public List<ITableData> selectedItems
        {
            get
            {
#if UNITY_2020_3_OR_NEWER
                return _listView.selectedItems.Cast<ITableData>().ToList();
#else
                List<ITableData> result = new List<ITableData>();
                result.Add(_listView.selectedItem as ITableData);
                return result;
#endif
            }
        }

        /// <summary>
        /// 单元视图交互事件
        /// </summary>
        public Action<PointerDownEvent, ITableData> ClickTableDataEvent;

        /// <summary>
        /// 单元视图交互事件
        /// </summary>
        public Action<TableColumn> ClickTableHeadEvent;

        /// <summary>
        /// 单元视图变化事件
        /// </summary>
        public Action<ITableData> SelectionChangedEvent;


        public TableViewer()
        {
            this.style.flexShrink = 1f;
            this.style.flexGrow = 1f;

            // 定义标题栏
            _toolbar = new Toolbar();

            // 定义列表视图
            _listView = new ListView();
            _listView.style.flexShrink = 1f;
            _listView.style.flexGrow = 1f;
            _listView.makeItem = MakeListViewElement;
            _listView.bindItem = BindListViewElement;
            _listView.selectionType = SelectionType.Multiple;
            _listView.RegisterCallback<PointerDownEvent>(OnClickListItem);

#if UNITY_2022_3_OR_NEWER
            _listView.selectionChanged += OnSelectionChanged;
#elif UNITY_2020_1_OR_NEWER
            _listView.onSelectionChange += OnSelectionChanged;
#else
            _listView.onSelectionChanged += OnSelectionChanged;
#endif

            this.Add(_toolbar);
            this.Add(_listView);
        }

        /// <summary>
        /// 获取标题UI元素
        /// </summary>
        public VisualElement GetHeaderElement(string elementName)
        {
            return _toolbar.Q<ToolbarButton>(elementName);
        }

        /// <summary>
        /// 添加单元列
        /// </summary>
        public void AddColumn(TableColumn column)
        {
            var toolbarBtn = new ToolbarButton();
            toolbarBtn.userData = column;
            toolbarBtn.name = column.ElementName;
            toolbarBtn.text = column.HeaderTitle;
            toolbarBtn.style.flexGrow = 0;
            toolbarBtn.style.width = column.ColumnStyle.Width;
            toolbarBtn.style.minWidth = column.ColumnStyle.Width;
            toolbarBtn.style.maxWidth = column.ColumnStyle.Width;
            toolbarBtn.clickable.clickedWithEventInfo += OnClickTableHead;
            SetCellElementStyle(toolbarBtn);
            _toolbar.Add(toolbarBtn);
            _columns.Add(column);

            // 可伸缩控制柄
            if (column.ColumnStyle.Stretchable)
            {
                int handleWidth = 3;
                int minWidth = (int)column.ColumnStyle.MinWidth.value;
                int maxWidth = (int)column.ColumnStyle.MaxWidth.value;
                var resizeHandle = new ResizeHandle(handleWidth, toolbarBtn, minWidth, maxWidth);
                resizeHandle.ResizeChanged += (float value) =>
                {
                    float width = Mathf.Clamp(value, column.ColumnStyle.MinWidth.value, column.ColumnStyle.MaxWidth.value);
                    column.ColumnStyle.Width = width;

                    foreach (var element in column.CellElements)
                    {
                        element.style.width = width;
                        element.style.minWidth = width;
                        element.style.maxWidth = width;
                    }
                };
                _toolbar.Add(resizeHandle);
            }

            // 计算索引值
            column.ColumnIndex = _columns.Count - 1;

            if (column.ColumnStyle.Sortable == false)
                toolbarBtn.SetEnabled(false);
        }

        /// <summary>
        /// 添加单元列集合
        /// </summary>
        public void AddColumns(IList<TableColumn> columns)
        {
            foreach (var column in columns)
            {
                AddColumn(column);
            }
        }

        /// <summary>
        /// 重建表格视图
        /// </summary>
        public void RebuildView()
        {
            if (_itemsSource == null)
                return;

            var itemsSource = _sortingDatas.Where(row => row.Visible);

            _listView.Clear();
            _listView.ClearSelection();
            _listView.itemsSource = itemsSource.ToList();
            _listView.Rebuild();

            // 刷新标题栏
            RefreshToobar();
        }
        private void RefreshToobar()
        {
            // 设置为原始标题
            foreach (var column in _columns)
            {
                var toobarButton = _toolbar.Q<ToolbarButton>(column.ElementName);
                toobarButton.text = column.HeaderTitle;
            }

            // 设置元素数量
            foreach (var column in _columns)
            {
                if (column.ColumnStyle.Counter)
                {
                    var toobarButton = GetHeaderElement(column.ElementName) as ToolbarButton;
                    toobarButton.text = $"{toobarButton.text} ({itemsSource.Count()})";
                }
            }

            // 设置展示单位
            foreach (var column in _columns)
            {
                if (string.IsNullOrEmpty(column.ColumnStyle.Units) == false)
                {
                    var toobarButton = GetHeaderElement(column.ElementName) as ToolbarButton;
                    toobarButton.text = $"{toobarButton.text} ({column.ColumnStyle.Units})";
                }
            }

            // 设置升降符号
            if (string.IsNullOrEmpty(_sortingHeader) == false)
            {
                var _toobarButton = _toolbar.Q<ToolbarButton>(_sortingHeader);
                if (_descendingSort)
                    _toobarButton.text = $"{_toobarButton.text} ↓";
                else
                    _toobarButton.text = $"{_toobarButton.text} ↑";
            }
        }

        /// <summary>
        /// 清空所有数据
        /// </summary>
        public void ClearAll(bool clearColumns, bool clearSource)
        {
            if (clearColumns)
            {
                _columns.Clear();
                _toolbar.Clear();
            }

            if (clearSource)
            {
                if (_itemsSource != null)
                    _itemsSource.Clear();
                if (_sortingDatas != null)
                    _sortingDatas.Clear();
                _listView.Clear();
                _listView.ClearSelection();
            }
        }

        private void OnClickListItem(PointerDownEvent evt)
        {
            var selectData = _listView.selectedItem as ITableData;
            if (selectData == null)
                return;

            ClickTableDataEvent?.Invoke(evt, selectData);
        }
        private void OnClickTableHead(EventBase eventBase)
        {
            if (_itemsSource == null)
                return;

            ToolbarButton toolbarBtn = eventBase.target as ToolbarButton;
            var clickedColumn = toolbarBtn.userData as TableColumn;
            if (clickedColumn == null)
                return;

            ClickTableHeadEvent?.Invoke(clickedColumn);
            if (clickedColumn.ColumnStyle.Sortable == false)
                return;

            if (_sortingHeader != clickedColumn.ElementName)
            {
                _sortingHeader = clickedColumn.ElementName;
                _descendingSort = false;
            }
            else
            {
                _descendingSort = !_descendingSort;
            }

            // 升降排序
            if (_descendingSort)
                _sortingDatas = _itemsSource.OrderByDescending(tableData => tableData.Cells[clickedColumn.ColumnIndex]).ToList();
            else
                _sortingDatas = _itemsSource.OrderBy(tableData => tableData.Cells[clickedColumn.ColumnIndex]).ToList();

            // 刷新数据表
            RebuildView();
        }
        private void OnSelectionChanged(IEnumerable<object> items)
        {
            foreach (var item in items)
            {
                var tableData = item as ITableData;
                SelectionChangedEvent?.Invoke(tableData);
                break;
            }
        }

        private bool CheckItemsSource(List<ITableData> itemsSource)
        {
            if (itemsSource == null)
                return false;

            if (itemsSource.Count > 0)
            {
                int cellCount = itemsSource[0].Cells.Count;
                for (int i = 0; i < itemsSource.Count; i++)
                {
                    var tableData = itemsSource[i];
                    if (tableData == null)
                    {
                        Debug.LogWarning($"Items source has null instance !");
                        return false;
                    }
                    if (tableData.Cells == null || tableData.Cells.Count == 0)
                    {
                        Debug.LogWarning($"Items source data has empty cells !");
                        return false;
                    }
                    if (tableData.Cells.Count != cellCount)
                    {
                        Debug.LogWarning($"Items source data has inconsisten cells count ! Item index {i}");
                        return false;
                    }
                }
            }

            return true;
        }
        private VisualElement MakeListViewElement()
        {
            VisualElement listViewElement = new VisualElement();
            listViewElement.style.flexDirection = FlexDirection.Row;
            foreach (var column in _columns)
            {
                var cellElement = column.MakeCell.Invoke();
                cellElement.name = column.ElementName;
                cellElement.style.flexGrow = 0f;
                cellElement.style.width = column.ColumnStyle.Width;
                cellElement.style.minWidth = column.ColumnStyle.Width;
                cellElement.style.maxWidth = column.ColumnStyle.Width;
                SetCellElementStyle(cellElement);
                listViewElement.Add(cellElement);
                column.CellElements.Add(cellElement);
            }
            return listViewElement;
        }
        private void BindListViewElement(VisualElement listViewElement, int index)
        {
            var sourceDatas = _listView.itemsSource as List<ITableData>;
            var tableData = sourceDatas[index];
            foreach (var colum in _columns)
            {
                var cellElement = listViewElement.Q(colum.ElementName);
                var tableCell = tableData.Cells[colum.ColumnIndex];
                colum.BindCell.Invoke(cellElement, tableData, tableCell);
            }
        }
        private void SetCellElementStyle(VisualElement element)
        {
            StyleLength defaultStyle = new StyleLength(1f);
            element.style.paddingTop = defaultStyle;
            element.style.paddingBottom = defaultStyle;
            element.style.marginTop = defaultStyle;
            element.style.marginBottom = defaultStyle;

            element.style.paddingLeft = 1;
            element.style.paddingRight = 1;
            element.style.marginLeft = 0;
            element.style.marginRight = 0;
        }
    }
}
#endif
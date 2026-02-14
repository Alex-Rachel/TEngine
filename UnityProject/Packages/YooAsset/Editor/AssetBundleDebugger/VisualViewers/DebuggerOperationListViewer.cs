#if UNITY_2019_4_OR_NEWER
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace YooAsset.Editor
{
    internal class DebuggerOperationListViewer
    {
        private class OperationTableData : DefaultTableData
        {
            public DebugPackageData PackageData;
            public DebugOperationInfo OperationInfo;
        }

        private VisualTreeAsset _visualAsset;
        private TemplateContainer _root;

        private TableViewer _operationTableView;
        private Toolbar _bottomToolbar;
        private TreeViewer _childTreeView;

        private List<ITableData> _sourceDatas;


        /// <summary>
        /// 初始化页面
        /// </summary>
        public void InitViewer()
        {
            // 加载布局文件		
            _visualAsset = UxmlLoader.LoadWindowUXML<DebuggerOperationListViewer>();
            if (_visualAsset == null)
                return;

            _root = _visualAsset.CloneTree();
            _root.style.flexGrow = 1f;

            // 任务列表
            _operationTableView = _root.Q<TableViewer>("TopTableView");
            _operationTableView.SelectionChangedEvent = OnOperationTableViewSelectionChanged;
            CreateOperationTableViewColumns();

            // 底部标题栏
            _bottomToolbar = _root.Q<Toolbar>("BottomToolbar");
            CreateBottomToolbarHeaders();

            // 子列表
            _childTreeView = _root.Q<TreeViewer>("BottomTreeView");
            _childTreeView.makeItem = MakeTreeViewItem;
            _childTreeView.bindItem = BindTreeViewItem;

            // 面板分屏
            var topGroup = _root.Q<VisualElement>("TopGroup");
            var bottomGroup = _root.Q<VisualElement>("BottomGroup");
            topGroup.style.minHeight = 100;
            bottomGroup.style.minHeight = 100f;
            UIElementsTools.SplitVerticalPanel(_root, topGroup, bottomGroup);
        }
        private void CreateOperationTableViewColumns()
        {
            // PackageName
            {
                var columnStyle = new ColumnStyle(200);
                columnStyle.Searchable = true;
                columnStyle.Sortable = true;
                var column = new TableColumn("PackageName", "Package Name", columnStyle);
                column.MakeCell = () =>
                {
                    var label = new Label();
                    label.style.unityTextAlign = TextAnchor.MiddleLeft;
                    return label;
                };
                column.BindCell = (VisualElement element, ITableData data, ITableCell cell) =>
                {
                    var infoLabel = element as Label;
                    infoLabel.text = (string)cell.GetDisplayObject();
                };
                _operationTableView.AddColumn(column);
            }

            // OperationName
            {
                var columnStyle = new ColumnStyle(300, 300, 600);
                columnStyle.Stretchable = true;
                columnStyle.Searchable = true;
                columnStyle.Sortable = true;
                columnStyle.Counter = true;
                var column = new TableColumn("OperationName", "Operation Name", columnStyle);
                column.MakeCell = () =>
                {
                    var label = new Label();
                    label.style.unityTextAlign = TextAnchor.MiddleLeft;
                    return label;
                };
                column.BindCell = (VisualElement element, ITableData data, ITableCell cell) =>
                {
                    var infoLabel = element as Label;
                    infoLabel.text = (string)cell.GetDisplayObject();
                };
                _operationTableView.AddColumn(column);
            }

            // Priority
            {
                var columnStyle = new ColumnStyle(100);
                columnStyle.Stretchable = false;
                columnStyle.Searchable = false;
                columnStyle.Sortable = true;
                var column = new TableColumn("Priority", "Priority", columnStyle);
                column.MakeCell = () =>
                {
                    var label = new Label();
                    label.style.unityTextAlign = TextAnchor.MiddleLeft;
                    return label;
                };
                column.BindCell = (VisualElement element, ITableData data, ITableCell cell) =>
                {
                    var infoLabel = element as Label;
                    infoLabel.text = (string)cell.GetDisplayObject();
                };
                _operationTableView.AddColumn(column);
            }

            // Progress
            {
                var columnStyle = new ColumnStyle(100);
                columnStyle.Stretchable = false;
                columnStyle.Searchable = false;
                columnStyle.Sortable = false;
                var column = new TableColumn("Progress", "Progress", columnStyle);
                column.MakeCell = () =>
                {
                    var label = new Label();
                    label.style.unityTextAlign = TextAnchor.MiddleLeft;
                    return label;
                };
                column.BindCell = (VisualElement element, ITableData data, ITableCell cell) =>
                {
                    var infoLabel = element as Label;
                    infoLabel.text = (string)cell.GetDisplayObject();
                };
                _operationTableView.AddColumn(column);
            }

            // BeginTime
            {
                var columnStyle = new ColumnStyle(100);
                columnStyle.Stretchable = false;
                columnStyle.Searchable = false;
                columnStyle.Sortable = true;
                var column = new TableColumn("BeginTime", "Begin Time", columnStyle);
                column.MakeCell = () =>
                {
                    var label = new Label();
                    label.style.unityTextAlign = TextAnchor.MiddleLeft;
                    return label;
                };
                column.BindCell = (VisualElement element, ITableData data, ITableCell cell) =>
                {
                    var infoLabel = element as Label;
                    infoLabel.text = (string)cell.GetDisplayObject();
                };
                _operationTableView.AddColumn(column);
            }

            // ProcessTime
            {
                var columnStyle = new ColumnStyle(130);
                columnStyle.Stretchable = false;
                columnStyle.Searchable = false;
                columnStyle.Sortable = true;
                columnStyle.Units = "ms";
                var column = new TableColumn("ProcessTime", "Process Time", columnStyle);
                column.MakeCell = () =>
                {
                    var label = new Label();
                    label.style.unityTextAlign = TextAnchor.MiddleLeft;
                    return label;
                };
                column.BindCell = (VisualElement element, ITableData data, ITableCell cell) =>
                {
                    var infoLabel = element as Label;
                    infoLabel.text = (string)cell.GetDisplayObject();
                };
                _operationTableView.AddColumn(column);
            }

            // Status
            {
                var columnStyle = new ColumnStyle(100);
                columnStyle.Stretchable = false;
                columnStyle.Searchable = false;
                columnStyle.Sortable = true;
                var column = new TableColumn("Status", "Status", columnStyle);
                column.MakeCell = () =>
                {
                    var label = new Label();
                    label.style.unityTextAlign = TextAnchor.MiddleLeft;
                    return label;
                };
                column.BindCell = (VisualElement element, ITableData data, ITableCell cell) =>
                {
                    StyleColor textColor;
                    var operationTableData = data as OperationTableData;
                    if (operationTableData.OperationInfo.Status == EOperationStatus.Failed.ToString())
                        textColor = new StyleColor(Color.yellow);
                    else
                        textColor = new StyleColor(Color.white);

                    var infoLabel = element as Label;
                    infoLabel.text = (string)cell.GetDisplayObject();
                    infoLabel.style.color = textColor;
                };
                _operationTableView.AddColumn(column);
            }

            // Desc
            {
                var columnStyle = new ColumnStyle(500, 500, 1000);
                columnStyle.Stretchable = true;
                columnStyle.Searchable = true;
                var column = new TableColumn("Desc", "Desc", columnStyle);
                column.MakeCell = () =>
                {
                    var label = new Label();
                    label.style.unityTextAlign = TextAnchor.MiddleLeft;
                    return label;
                };
                column.BindCell = (VisualElement element, ITableData data, ITableCell cell) =>
                {
                    var infoLabel = element as Label;
                    infoLabel.text = (string)cell.GetDisplayObject();
                };
                _operationTableView.AddColumn(column);
            }
        }
        private void CreateBottomToolbarHeaders()
        {
            // OperationName
            {
                ToolbarButton button = new ToolbarButton();
                button.text = "OperationName";
                button.style.flexGrow = 0;
                button.style.width = 315;
                _bottomToolbar.Add(button);
            }

            // Progress
            {
                ToolbarButton button = new ToolbarButton();
                button.text = "Progress";
                button.style.flexGrow = 0;
                button.style.width = 100;
                _bottomToolbar.Add(button);
            }

            // BeginTime
            {
                ToolbarButton button = new ToolbarButton();
                button.text = "BeginTime";
                button.style.flexGrow = 0;
                button.style.width = 100;
                _bottomToolbar.Add(button);
            }

            // ProcessTime
            {
                ToolbarButton button = new ToolbarButton();
                button.text = "ProcessTime (ms)";
                button.style.flexGrow = 0;
                button.style.width = 130;
                _bottomToolbar.Add(button);
            }

            // Status
            {
                ToolbarButton button = new ToolbarButton();
                button.text = "Status";
                button.style.flexGrow = 0;
                button.style.width = 100;
                _bottomToolbar.Add(button);
            }

            // Desc
            {
                ToolbarButton button = new ToolbarButton();
                button.text = "Desc";
                button.style.flexGrow = 0;
                button.style.width = 500;
                _bottomToolbar.Add(button);
            }
        }

        /// <summary>
        /// 填充页面数据
        /// </summary>
        public void FillViewData(DebugReport debugReport)
        {
            // 清空旧数据
            _operationTableView.ClearAll(false, true);
            _childTreeView.ClearAll();
            _childTreeView.RebuildView();

            // 填充数据源
            _sourceDatas = new List<ITableData>(1000);
            foreach (var packageData in debugReport.PackageDatas)
            {
                foreach (var operationInfo in packageData.OperationInfos)
                {
                    var rowData = new OperationTableData();
                    rowData.PackageData = packageData;
                    rowData.OperationInfo = operationInfo;
                    rowData.AddStringValueCell("PackageName", packageData.PackageName);
                    rowData.AddStringValueCell("OperationName", operationInfo.OperationName);
                    rowData.AddLongValueCell("Priority", operationInfo.Priority);
                    rowData.AddDoubleValueCell("Progress", operationInfo.Progress);
                    rowData.AddStringValueCell("BeginTime", operationInfo.BeginTime);
                    rowData.AddLongValueCell("LoadingTime", operationInfo.ProcessTime);
                    rowData.AddStringValueCell("Status", operationInfo.Status.ToString());
                    rowData.AddStringValueCell("Desc", operationInfo.OperationDesc);
                    _sourceDatas.Add(rowData);
                }
            }
            _operationTableView.itemsSource = _sourceDatas;

            // 重建视图
            RebuildView(null);
        }

        /// <summary>
        /// 清空页面
        /// </summary>
        public void ClearView()
        {
            _operationTableView.ClearAll(false, true);
            _operationTableView.RebuildView();

            _childTreeView.ClearAll();
            _childTreeView.RebuildView();
        }

        /// <summary>
        /// 重建视图
        /// </summary>
        public void RebuildView(string searchKeyWord)
        {
            // 搜索匹配
            if(_sourceDatas != null)
                DefaultSearchSystem.Search(_sourceDatas, searchKeyWord);

            // 重建视图
            _operationTableView.RebuildView();
            _childTreeView.RebuildView();
        }

        /// <summary>
        /// 挂接到父类页面上
        /// </summary>
        public void AttachParent(VisualElement parent)
        {
            parent.Add(_root);
        }

        /// <summary>
        /// 从父类页面脱离开
        /// </summary>
        public void DetachParent()
        {
            _root.RemoveFromHierarchy();
        }

        private void OnOperationTableViewSelectionChanged(ITableData data)
        {
            var operationTableData = data as OperationTableData;
            DebugPackageData packageData = operationTableData.PackageData;
            DebugOperationInfo operationInfo = operationTableData.OperationInfo;

            TreeNode rootNode = new TreeNode(operationInfo);
            FillTreeData(operationInfo, rootNode);
            _childTreeView.ClearAll();
            _childTreeView.SetRootItem(rootNode);
            _childTreeView.RebuildView();
        }
        private void MakeTreeViewItem(VisualElement container)
        {
            // OperationName
            {
                Label label = new Label();
                label.name = "OperationName";
                label.style.flexGrow = 0f;
                label.style.width = 300;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                container.Add(label);
            }

            // Progress
            {
                var label = new Label();
                label.name = "Progress";
                label.style.flexGrow = 0f;
                label.style.width = 100;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                container.Add(label);
            }

            // BeginTime
            {
                var label = new Label();
                label.name = "BeginTime";
                label.style.flexGrow = 0f;
                label.style.width = 100;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                container.Add(label);
            }

            // ProcessTime
            {
                var label = new Label();
                label.name = "ProcessTime";
                label.style.flexGrow = 0f;
                label.style.width = 130;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                container.Add(label);
            }

            // Status
            {
                var label = new Label();
                label.name = "Status";
                label.style.flexGrow = 0f;
                label.style.width = 100;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                container.Add(label);
            }

            // Desc
            {
                Label label = new Label();
                label.name = "Desc";
                label.style.flexGrow = 1f;
                label.style.width = 500;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                container.Add(label);
            }
        }
        private void BindTreeViewItem(VisualElement container, object userData)
        {
            var operationInfo = (DebugOperationInfo)userData;

            // OperationName
            {
                var label = container.Q<Label>("OperationName");
                label.text = operationInfo.OperationName;
            }

            // Progress
            {
                var label = container.Q<Label>("Progress");
                label.text = operationInfo.Progress.ToString();
            }

            // BeginTime
            {
                var label = container.Q<Label>("BeginTime");
                label.text = operationInfo.BeginTime;
            }

            // ProcessTime
            {
                var label = container.Q<Label>("ProcessTime");
                label.text = operationInfo.ProcessTime.ToString();
            }

            // Status
            {
                StyleColor textColor;
                if (operationInfo.Status == EOperationStatus.Failed.ToString())
                    textColor = new StyleColor(Color.yellow);
                else
                    textColor = new StyleColor(Color.white);

                var label = container.Q<Label>("Status");
                label.text = operationInfo.Status;
                label.style.color = textColor;
            }

            // Desc
            {
                var label = container.Q<Label>("Desc");
                label.text = operationInfo.OperationDesc;
            }
        }
        private void FillTreeData(DebugOperationInfo parentOperation, TreeNode rootNode)
        {
            foreach (var childOperation in parentOperation.Childs)
            {
                var childNode = new TreeNode(childOperation);
                rootNode.AddChild(childNode);
                FillTreeData(childOperation, childNode);
            }
        }
    }
}
#endif
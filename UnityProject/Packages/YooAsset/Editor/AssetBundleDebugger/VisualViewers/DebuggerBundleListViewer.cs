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
    internal class DebuggerBundleListViewer
    {
        private class BundleTableData : DefaultTableData
        {
            public DebugPackageData PackageData;
            public DebugBundleInfo BundleInfo;
        }
        private class UsingTableData : DefaultTableData
        {
            public DebugProviderInfo ProviderInfo;
        }
        private class ReferenceTableData : DefaultTableData
        {
            public DebugBundleInfo BundleInfo;
        }

        private VisualTreeAsset _visualAsset;
        private TemplateContainer _root;

        private TableViewer _bundleTableView;
        private TableViewer _usingTableView;
        private TableViewer _referenceTableView;

        private List<ITableData> _sourceDatas;

        /// <summary>
        /// 初始化页面
        /// </summary>
        public void InitViewer()
        {
            // 加载布局文件
            _visualAsset = UxmlLoader.LoadWindowUXML<DebuggerBundleListViewer>();
            if (_visualAsset == null)
                return;

            _root = _visualAsset.CloneTree();
            _root.style.flexGrow = 1f;

            // 资源包列表
            _bundleTableView = _root.Q<TableViewer>("BundleTableView");
            _bundleTableView.SelectionChangedEvent = OnBundleTableViewSelectionChanged;
            CreateBundleTableViewColumns();

            // 使用列表
            _usingTableView = _root.Q<TableViewer>("UsingTableView");
            CreateUsingTableViewColumns();

            // 引用列表
            _referenceTableView = _root.Q<TableViewer>("ReferenceTableView");
            CreateReferenceTableViewColumns();

            // 面板分屏
            var topGroup = _root.Q<VisualElement>("TopGroup");
            var bottomGroup = _root.Q<VisualElement>("BottomGroup");
            topGroup.style.minHeight = 100;
            bottomGroup.style.minHeight = 100f;
            UIElementsTools.SplitVerticalPanel(_root, topGroup, bottomGroup);
            UIElementsTools.SplitVerticalPanel(bottomGroup, _usingTableView, _referenceTableView);
        }
        private void CreateBundleTableViewColumns()
        {
            // PackageName
            {
                var columnStyle = new ColumnStyle(200);
                columnStyle.Stretchable = false;
                columnStyle.Searchable = false;
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
                _bundleTableView.AddColumn(column);
            }

            // BundleName
            {
                var columnStyle = new ColumnStyle(600, 500, 1000);
                columnStyle.Stretchable = true;
                columnStyle.Searchable = true;
                columnStyle.Sortable = true;
                columnStyle.Counter = true;
                var column = new TableColumn("BundleName", "Bundle Name", columnStyle);
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
                _bundleTableView.AddColumn(column);
            }

            // RefCount
            {
                var columnStyle = new ColumnStyle(100);
                columnStyle.Stretchable = false;
                columnStyle.Searchable = false;
                columnStyle.Sortable = true;
                var column = new TableColumn("RefCount", "Ref Count", columnStyle);
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
                _bundleTableView.AddColumn(column);
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
                    var bundleTableData = data as BundleTableData;
                    if (bundleTableData.BundleInfo.Status == EOperationStatus.Failed.ToString())
                        textColor = new StyleColor(Color.yellow);
                    else
                        textColor = new StyleColor(Color.white);

                    var infoLabel = element as Label;
                    infoLabel.text = (string)cell.GetDisplayObject();
                    infoLabel.style.color = textColor;
                };
                _bundleTableView.AddColumn(column);
            }
        }
        private void CreateUsingTableViewColumns()
        {
            // UsingAssets
            {
                var columnStyle = new ColumnStyle(600, 500, 1000);
                columnStyle.Stretchable = true;
                columnStyle.Searchable = true;
                columnStyle.Sortable = true;
                columnStyle.Counter = true;
                var column = new TableColumn("UsingAssets", "Using Assets", columnStyle);
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
                _usingTableView.AddColumn(column);
            }

            // SpawnScene
            {
                var columnStyle = new ColumnStyle(150);
                columnStyle.Stretchable = false;
                columnStyle.Searchable = false;
                columnStyle.Sortable = true;
                var column = new TableColumn("SpawnScene", "Spawn Scene", columnStyle);
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
                _usingTableView.AddColumn(column);
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
                _usingTableView.AddColumn(column);
            }

            // RefCount
            {
                var columnStyle = new ColumnStyle(100);
                columnStyle.Stretchable = false;
                columnStyle.Searchable = false;
                columnStyle.Sortable = true;
                var column = new TableColumn("RefCount", "Ref Count", columnStyle);
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
                _usingTableView.AddColumn(column);
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
                    var usingTableData = data as UsingTableData;
                    if (usingTableData.ProviderInfo.Status == EOperationStatus.Failed.ToString())
                        textColor = new StyleColor(Color.yellow);
                    else
                        textColor = new StyleColor(Color.white);

                    var infoLabel = element as Label;
                    infoLabel.text = (string)cell.GetDisplayObject();
                    infoLabel.style.color = textColor;
                };
                _usingTableView.AddColumn(column);
            }
        }
        private void CreateReferenceTableViewColumns()
        {
            // BundleName
            {
                var columnStyle = new ColumnStyle(600, 500, 1000);
                columnStyle.Stretchable = true;
                columnStyle.Searchable = true;
                columnStyle.Sortable = true;
                columnStyle.Counter = true;
                var column = new TableColumn("ReferenceBundle", "Reference Bundle", columnStyle);
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
                _referenceTableView.AddColumn(column);
            }

            // RefCount
            {
                var columnStyle = new ColumnStyle(100);
                columnStyle.Stretchable = false;
                columnStyle.Searchable = false;
                columnStyle.Sortable = true;
                var column = new TableColumn("RefCount", "Ref Count", columnStyle);
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
                _referenceTableView.AddColumn(column);
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
                    var feferenceTableData = data as ReferenceTableData;
                    if (feferenceTableData.BundleInfo.Status == EOperationStatus.Failed.ToString())
                        textColor = new StyleColor(Color.yellow);
                    else
                        textColor = new StyleColor(Color.white);

                    var infoLabel = element as Label;
                    infoLabel.text = (string)cell.GetDisplayObject();
                    infoLabel.style.color = textColor;
                };
                _referenceTableView.AddColumn(column);
            }
        }

        /// <summary>
        /// 填充页面数据
        /// </summary>
        public void FillViewData(DebugReport debugReport)
        {
            // 清空旧数据
            _bundleTableView.ClearAll(false, true);
            _usingTableView.ClearAll(false, true);
            _referenceTableView.ClearAll(false, true);

            // 填充数据源
            _sourceDatas = new List<ITableData>(1000);
            foreach (var packageData in debugReport.PackageDatas)
            {
                foreach (var bundleInfo in packageData.BundleInfos)
                {
                    var rowData = new BundleTableData();
                    rowData.PackageData = packageData;
                    rowData.BundleInfo = bundleInfo;
                    rowData.AddAssetPathCell("PackageName", packageData.PackageName);
                    rowData.AddStringValueCell("BundleName", bundleInfo.BundleName);
                    rowData.AddLongValueCell("RefCount", bundleInfo.RefCount);
                    rowData.AddStringValueCell("Status", bundleInfo.Status.ToString());
                    _sourceDatas.Add(rowData);
                }
            }
            _bundleTableView.itemsSource = _sourceDatas;

            // 重建视图
            RebuildView(null);
        }

        /// <summary>
        /// 清空页面
        /// </summary>
        public void ClearView()
        {
            _bundleTableView.ClearAll(false, true);
            _bundleTableView.RebuildView();

            _usingTableView.ClearAll(false, true);
            _usingTableView.RebuildView();

            _referenceTableView.ClearAll(false, true);
            _referenceTableView.RebuildView();
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
            _bundleTableView.RebuildView();
            _usingTableView.RebuildView();
            _referenceTableView.RebuildView();
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

        private void OnBundleTableViewSelectionChanged(ITableData data)
        {
            var bundleTableData = data as BundleTableData;
            var packageData = bundleTableData.PackageData;
            var selectBundleInfo = bundleTableData.BundleInfo;

            // 填充UsingTableView
            {
                var sourceDatas = new List<ITableData>(1000);
                foreach (var providerInfo in packageData.ProviderInfos)
                {
                    foreach (var dependBundleName in providerInfo.DependBundles)
                    {
                        if (dependBundleName == selectBundleInfo.BundleName)
                        {
                            var rowData = new UsingTableData();
                            rowData.ProviderInfo = providerInfo;
                            rowData.AddStringValueCell("UsingAssets", providerInfo.AssetPath);
                            rowData.AddStringValueCell("SpawnScene", providerInfo.SpawnScene);
                            rowData.AddStringValueCell("BeginTime", providerInfo.BeginTime);
                            rowData.AddLongValueCell("RefCount", providerInfo.RefCount);
                            rowData.AddStringValueCell("Status", providerInfo.Status);
                            sourceDatas.Add(rowData);
                            break;
                        }
                    }
                }
                _usingTableView.itemsSource = sourceDatas;
                _usingTableView.RebuildView();
            }

            // 填充ReferenceTableView
            {
                var sourceDatas = new List<ITableData>(1000);
                foreach (string referenceBundleName in selectBundleInfo.ReferenceBundles)
                {
                    var bundleInfo = packageData.GetBundleInfo(referenceBundleName);
                    var rowData = new ReferenceTableData();
                    rowData.BundleInfo = bundleInfo;
                    rowData.AddStringValueCell("BundleName", bundleInfo.BundleName);
                    rowData.AddLongValueCell("RefCount", bundleInfo.RefCount);
                    rowData.AddStringValueCell("Status", bundleInfo.Status.ToString());
                    sourceDatas.Add(rowData);
                }
                _referenceTableView.itemsSource = sourceDatas;
                _referenceTableView.RebuildView();
            }
        }
    }
}
#endif
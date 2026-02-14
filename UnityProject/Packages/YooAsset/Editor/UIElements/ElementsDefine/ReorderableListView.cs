#if UNITY_2021_3_OR_NEWER
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
    public class ReorderableListView : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<ReorderableListView, UxmlTraits>
        {
        }
        
        /// <summary>
        /// 制作元素委托
        /// </summary>
        /// <returns></returns>
        public delegate VisualElement MakeElementDelegate();

        /// <summary>
        /// 绑定元素委托
        /// </summary>
        public delegate void BindElementDelegqate(VisualElement element, int index);

        private Foldout _foldout;
        private ListView _listView;
        private Label _headerLabel;
        private Button _addButton;
        private Button _removeButton;
        private string _headerName = nameof(ReorderableListView);

        /// <summary>
        /// 源数据
        /// </summary>
        public IList SourceData
        {
            set
            {
                if (value is ArrayList)
                    throw new Exception($"{nameof(SourceData)} not support {nameof(ArrayList)}");

                _listView.Clear();
                _listView.ClearSelection();
                _listView.itemsSource = value;
                _listView.Rebuild();
                RefreshFoldoutName();
                RefreshRemoveButton();
            }
            get
            {
                return _listView.itemsSource;
            }
        }

        /// <summary>
        /// 元素固定高度
        /// </summary>
        public float ElementHeight
        {
            set
            {
                _listView.fixedItemHeight = value;
                _listView.Rebuild();
            }
            get
            {
                return _listView.fixedItemHeight;
            }
        }

        /// <summary>
        /// 增加按钮显隐
        /// </summary>
        public bool DisplayAdd
        {
            set
            {
                UIElementsTools.SetElementVisible(_addButton, value);
            }
            get
            {
                return _addButton.style.visibility == Visibility.Visible;
            }
        }

        /// <summary>
        /// 移除按钮显隐
        /// </summary>
        public bool DisplayRemove
        {
            set
            {
                UIElementsTools.SetElementVisible(_removeButton, value);
            }
            get
            {
                return _removeButton.style.visibility == Visibility.Visible;
            }
        }

        /// <summary>
        /// 标题名称
        /// </summary>
        public string HeaderName
        {
            set
            {
                _headerName = value;
                RefreshFoldoutName();
            }
            get
            {
                return _headerName;
            }
        }

        /// <summary>
        /// 制作元素的回调
        /// </summary>
        public MakeElementDelegate MakeElementCallback;

        /// <summary>
        /// 绑定元素的回调
        /// </summary>
        public BindElementDelegqate BindElementCallback;


        public ReorderableListView()
        {
            CreateView(true);
        }
        public ReorderableListView(bool foldout)
        {
            CreateView(foldout);
        }
        private void CreateView(bool foldout)
        {
            this.style.flexGrow = 1;
            this.style.flexShrink = 1;

            // 折叠栏
            if (foldout)
            {
                _foldout = new Foldout();
                _foldout.style.flexGrow = 1f;
                _foldout.style.flexShrink = 1f;
                _foldout.text = $"{nameof(ReorderableListView)}";
            }
            else
            {
                _headerLabel = new Label();
            }

            // 列表视图
            _listView = new ListView();
            _listView.style.flexGrow = 1;
            _listView.style.flexShrink = 1;
            _listView.reorderable = true;
            _listView.reorderMode = ListViewReorderMode.Animated;
            _listView.makeItem = MakeListViewElement;
            _listView.bindItem = BindListViewElement;
#if UNITY_2022_3_OR_NEWER
            _listView.selectionChanged += OnSelectionChanged;
#elif UNITY_2020_1_OR_NEWER
            _listView.onSelectionChange += OnSelectionChanged;
#else
            _listView.onSelectionChanged += OnSelectionChanged;
#endif

            // 按钮组
            var buttonContainer = new VisualElement();
            buttonContainer.style.flexDirection = FlexDirection.RowReverse;

            // 移除按钮
            _removeButton = new Button();
            _removeButton.text = " - ";
            _removeButton.clicked += OnClickRemoveButton;
            _removeButton.SetEnabled(false);
            buttonContainer.Add(_removeButton);

            // 增加按钮
            _addButton = new Button();
            _addButton.text = " + ";
            _addButton.clicked += OnClickAddButton;
            buttonContainer.Add(_addButton);

            // 组织页面
            if (foldout)
            {
                _foldout.Add(_listView);
                _foldout.Add(buttonContainer);
                this.Add(_foldout);
            }
            else
            {
                this.Add(_headerLabel);
                this.Add(_listView);
                this.Add(buttonContainer);
            }
        }
        private void OnClickAddButton()
        {
            if (_listView.itemsSource != null)
            {
                object defaultValue = GetElementDefaultValue();
                _listView.itemsSource.Add(defaultValue);
                _listView.Rebuild();
                RefreshFoldoutName();
                RefreshRemoveButton();
            }
            else
            {
                Debug.LogWarning("The source data is null !");
            }
        }
        private void OnClickRemoveButton()
        {
            if (_listView.itemsSource != null)
            {
                if (_listView.selectedIndex >= 0)
                {
                    _listView.itemsSource.RemoveAt(_listView.selectedIndex);
                    _listView.Rebuild();
                    RefreshFoldoutName();
                    RefreshRemoveButton();
                }
            }
            else
            {
                Debug.LogWarning("The source data is null !");
            }
        }
        private void OnSelectionChanged(IEnumerable<object> objs)
        {
            RefreshRemoveButton();
        }

        /// <summary>
        /// 生成元素
        /// </summary>
        private VisualElement MakeListViewElement()
        {
            if (MakeElementCallback != null)
            {
                return MakeElementCallback.Invoke();
            }

            Type elementType = GetElementType();
            if (elementType == typeof(string))
            {
                TextField textField = new TextField();
                textField.RegisterValueChangedCallback(evt =>
                {
                    int itemIndex = (int)textField.userData;
                    _listView.itemsSource[itemIndex] = textField.value;
                });
                return textField;
            }
            else if (elementType == typeof(int))
            {
                IntegerField intField = new IntegerField();
                intField.RegisterValueChangedCallback(evt =>
                {
                    int itemIndex = (int)intField.userData;
                    _listView.itemsSource[itemIndex] = intField.value;
                });
                return intField;
            }
            else if (elementType == typeof(long))
            {
                LongField longField = new LongField();
                longField.RegisterValueChangedCallback(evt =>
                {
                    int itemIndex = (int)longField.userData;
                    _listView.itemsSource[itemIndex] = longField.value;
                });
                return longField;
            }
            else if (elementType == typeof(float))
            {
                FloatField floatField = new FloatField();
                floatField.RegisterValueChangedCallback(evt =>
                {
                    int itemIndex = (int)floatField.userData;
                    _listView.itemsSource[itemIndex] = floatField.value;
                });
                return floatField;
            }
            else if (elementType == typeof(double))
            {
                DoubleField doubleField = new DoubleField();
                doubleField.RegisterValueChangedCallback(evt =>
                {
                    int itemIndex = (int)doubleField.userData;
                    _listView.itemsSource[itemIndex] = doubleField.value;
                });
                return doubleField;
            }
            else if (elementType == typeof(bool))
            {
                Toggle toggle = new Toggle();
                toggle.RegisterValueChangedCallback(evt =>
                {
                    int itemIndex = (int)toggle.userData;
                    _listView.itemsSource[itemIndex] = toggle.value;
                });
                return toggle;
            }
            else if (elementType == typeof(Hash128))
            {
                Hash128Field hash128Field = new Hash128Field();
                hash128Field.RegisterValueChangedCallback(evt =>
                {
                    int itemIndex = (int)hash128Field.userData;
                    _listView.itemsSource[itemIndex] = hash128Field.value;
                });
                return hash128Field;
            }
            else if (elementType == typeof(Vector2))
            {
                Vector2Field vector2Field = new Vector2Field();
                vector2Field.RegisterValueChangedCallback(evt =>
                {
                    int itemIndex = (int)vector2Field.userData;
                    _listView.itemsSource[itemIndex] = vector2Field.value;
                });
                return vector2Field;
            }
            else if (elementType == typeof(Vector3))
            {
                Vector3Field vector3Field = new Vector3Field();
                vector3Field.RegisterValueChangedCallback(evt =>
                {
                    int itemIndex = (int)vector3Field.userData;
                    _listView.itemsSource[itemIndex] = vector3Field.value;
                });
                return vector3Field;
            }
            else if (elementType == typeof(Vector4))
            {
                Vector4Field vector4Field = new Vector4Field();
                vector4Field.RegisterValueChangedCallback(evt =>
                {
                    int itemIndex = (int)vector4Field.userData;
                    _listView.itemsSource[itemIndex] = vector4Field.value;
                });
                return vector4Field;
            }
            else if (elementType == typeof(Rect))
            {
                RectField rectField = new RectField();
                rectField.RegisterValueChangedCallback(evt =>
                {
                    int itemIndex = (int)rectField.userData;
                    _listView.itemsSource[itemIndex] = rectField.value;
                });
                return rectField;
            }
            else if (elementType == typeof(Bounds))
            {
                BoundsField boundsField = new BoundsField();
                boundsField.RegisterValueChangedCallback(evt =>
                {
                    int itemIndex = (int)boundsField.userData;
                    _listView.itemsSource[itemIndex] = boundsField.value;
                });
                return boundsField;
            }
            else if (elementType == typeof(Color))
            {
                ColorField colorField = new ColorField();
                colorField.RegisterValueChangedCallback(evt =>
                {
                    int itemIndex = (int)colorField.userData;
                    _listView.itemsSource[itemIndex] = colorField.value;
                });
                return colorField;
            }
            else if (elementType == typeof(Gradient))
            {
                GradientField gradientField = new GradientField();
                gradientField.RegisterValueChangedCallback(evt =>
                {
                    int itemIndex = (int)gradientField.userData;
                    _listView.itemsSource[itemIndex] = gradientField.value;
                });
                return gradientField;
            }
            else if (elementType == typeof(AnimationCurve))
            {
                CurveField curveField = new CurveField();
                curveField.RegisterValueChangedCallback(evt =>
                {
                    int itemIndex = (int)curveField.userData;
                    _listView.itemsSource[itemIndex] = curveField.value;
                });
                return curveField;
            }
            else if (elementType == typeof(UnityEngine.Object))
            {
                ObjectField objectField = new ObjectField();
                objectField.objectType = typeof(UnityEngine.Object);
                objectField.RegisterValueChangedCallback(evt =>
                {
                    int itemIndex = (int)objectField.userData;
                    _listView.itemsSource[itemIndex] = objectField.value;
                });
                return objectField;
            }
            else if (elementType.IsEnum)
            {
                EnumField enumField = new EnumField();
                enumField.RegisterValueChangedCallback(evt =>
                {
                    int itemIndex = (int)enumField.userData;
                    _listView.itemsSource[itemIndex] = enumField.value;
                });
                return enumField;
            }
            else
            {
                Label label = new Label();
                label.text = $"Not support element type : {elementType.Name}";
                return label;
            }
        }

        /// <summary>
        /// 绑定元素
        /// </summary>
        private void BindListViewElement(VisualElement listViewElement, int index)
        {
            if (BindElementCallback != null)
            {
                BindElementCallback.Invoke(listViewElement, index);
                return;
            }

            var elementValue = _listView.itemsSource[index];
            string elementName = GetElementName(index);
            Type elementType = GetElementType();
            if (elementType == typeof(string))
            {
                TextField textField = listViewElement as TextField;
                textField.userData = index;
                textField.label = elementName;
                textField.SetValueWithoutNotify(elementValue as string);
            }
            else if (elementType == typeof(int))
            {
                IntegerField intField = listViewElement as IntegerField;
                intField.userData = index;
                intField.label = elementName;
                intField.SetValueWithoutNotify((int)elementValue);
            }
            else if (elementType == typeof(long))
            {
                LongField longField = listViewElement as LongField;
                longField.userData = index;
                longField.label = elementName;
                longField.SetValueWithoutNotify((long)elementValue);
            }
            else if (elementType == typeof(float))
            {
                FloatField floatField = listViewElement as FloatField;
                floatField.userData = index;
                floatField.label = elementName;
                floatField.SetValueWithoutNotify((float)elementValue);
            }
            else if (elementType == typeof(double))
            {
                DoubleField doubleField = listViewElement as DoubleField;
                doubleField.userData = index;
                doubleField.label = elementName;
                doubleField.SetValueWithoutNotify((double)elementValue);
            }
            else if (elementType == typeof(bool))
            {
                Toggle toggle = listViewElement as Toggle;
                toggle.userData = index;
                toggle.label = elementName;
                toggle.SetValueWithoutNotify((bool)elementValue);
            }
            else if (elementType == typeof(Hash128))
            {
                Hash128Field hash128Field = listViewElement as Hash128Field;
                hash128Field.userData = index;
                hash128Field.label = elementName;
                hash128Field.SetValueWithoutNotify((Hash128)elementValue);
            }
            else if (elementType == typeof(Vector2))
            {
                Vector2Field vector2Field = listViewElement as Vector2Field;
                vector2Field.userData = index;
                vector2Field.label = elementName;
                vector2Field.SetValueWithoutNotify((Vector2)elementValue);
            }
            else if (elementType == typeof(Vector3))
            {
                Vector3Field vector3Field = listViewElement as Vector3Field;
                vector3Field.userData = index;
                vector3Field.label = elementName;
                vector3Field.SetValueWithoutNotify((Vector3)elementValue);
            }
            else if (elementType == typeof(Vector4))
            {
                Vector4Field vector4Field = listViewElement as Vector4Field;
                vector4Field.userData = index;
                vector4Field.label = elementName;
                vector4Field.SetValueWithoutNotify((Vector4)elementValue);
            }
            else if (elementType == typeof(Rect))
            {
                RectField rectField = listViewElement as RectField;
                rectField.userData = index;
                rectField.label = elementName;
                rectField.SetValueWithoutNotify((Rect)elementValue);
            }
            else if (elementType == typeof(Bounds))
            {
                BoundsField boundsField = listViewElement as BoundsField;
                boundsField.userData = index;
                boundsField.label = elementName;
                boundsField.SetValueWithoutNotify((Bounds)elementValue);
            }
            else if (elementType == typeof(Color))
            {
                ColorField colorField = listViewElement as ColorField;
                colorField.userData = index;
                colorField.label = elementName;
                colorField.SetValueWithoutNotify((Color)elementValue);
            }
            else if (elementType == typeof(Gradient))
            {
                GradientField gradientField = listViewElement as GradientField;
                gradientField.userData = index;
                gradientField.label = elementName;
                gradientField.SetValueWithoutNotify((Gradient)elementValue);
            }
            else if (elementType == typeof(AnimationCurve))
            {
                CurveField curveField = listViewElement as CurveField;
                curveField.userData = index;
                curveField.label = elementName;
                curveField.SetValueWithoutNotify((AnimationCurve)elementValue);
            }
            else if (elementType == typeof(UnityEngine.Object))
            {
                ObjectField objectField = listViewElement as ObjectField;
                objectField.userData = index;
                objectField.label = elementName;
                objectField.SetValueWithoutNotify(elementValue as UnityEngine.Object);
            }
            else if (elementType.IsEnum)
            {
                EnumField enumField = listViewElement as EnumField;
                enumField.userData = index;
                enumField.label = elementName;
                enumField.Init((Enum)elementValue);
                enumField.SetValueWithoutNotify((Enum)elementValue);
            }
            else
            {
            }
        }

        private Type GetElementType()
        {
            Type elementType = _listView.itemsSource.GetType().GetGenericArguments()[0];
            return elementType;
        }
        private object GetElementDefaultValue()
        {
            Type type = GetElementType();
            if (type.IsValueType)
            {
                return Activator.CreateInstance(type);
            }
            return null;
        }
        private string GetElementName(int index)
        {
            return $"Element {index}";
        }

        private void RefreshRemoveButton()
        {
            if (_listView.itemsSource == null)
            {
                _removeButton.SetEnabled(false);
                return;
            }

            // 注意：数据列表移除元素的时候有可能会越界！
            if (_listView.selectedIndex >= _listView.itemsSource.Count)
                _listView.ClearSelection();

            if (_listView.selectedIndex >= 0)
                _removeButton.SetEnabled(true);
            else
                _removeButton.SetEnabled(false);
        }
        private void RefreshFoldoutName()
        {
            if (_listView.itemsSource == null)
            {
                if (_foldout != null)
                    _foldout.text = _headerName;
                if (_headerLabel != null)
                    _headerLabel.text = _headerName;
            }
            else
            {
                if (_foldout != null)
                    _foldout.text = _headerName + $" ({_listView.itemsSource.Count}) ";
                if (_headerLabel != null)
                    _headerLabel.text = _headerName + $" ({_listView.itemsSource.Count}) ";
            }
        }
    }
}
#endif
#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GameLogic;
using TEngine.Editor.UI;
using UnityEngine.Events;

[InitializeOnLoad]
public static class UnityEditorInspectorDrawHelper
{
    private static readonly Dictionary<EditorWindow, VisualElement> m_bindButtonsByWindow = new();
    private static readonly List<EditorWindow> m_windowsToRemove = new();
    private static readonly System.Type m_inspectorWindowType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.InspectorWindow");
    // 复用 Unity 维护的窗口列表，窗口开关时由 Unity 更新，不缓存窗口快照。
    private static readonly IList m_inspectorWindows = (IList)m_inspectorWindowType
        .GetField("m_AllInspectors", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    public static UnityAction<EditorWindow> OnCreateButton;
    public static UnityAction<EditorWindow> OnDestroyButton;

    static UnityEditorInspectorDrawHelper()
    {
        EditorApplication.update -= UpdateBindButtons;
        EditorApplication.update += UpdateBindButtons;
    }

    static void UpdateBindButtons()
    {
        var shouldShowButton = ShouldShowBindButton();
        foreach (EditorWindow window in m_inspectorWindows)
        {
            if (window != null && window.GetType() == m_inspectorWindowType && window.rootVisualElement != null)
            {
                UpdateButtonForWindow(window, shouldShowButton);
            }
        }

        // 清理不存在的窗口引用
        m_windowsToRemove.Clear();
        foreach (var kvp in m_bindButtonsByWindow)
        {
            if (kvp.Key == null)
            {
                m_windowsToRemove.Add(kvp.Key);
            }
        }
        foreach (var window in m_windowsToRemove)
        {
            m_bindButtonsByWindow.Remove(window);
        }
        m_windowsToRemove.Clear();
    }

    static void UpdateButtonForWindow(EditorWindow window, bool shouldShowButton)
    {
        if (window == null || window.rootVisualElement == null) return;

        var hasButton = m_bindButtonsByWindow.ContainsKey(window);

        if (shouldShowButton && !hasButton)
        {
            if (CreateButton(window))
                OnCreateButton?.Invoke(window);
        }
        else if (!shouldShowButton && hasButton)
        {
            DestroyButton(window);
            OnDestroyButton?.Invoke(window);
        }
    }

    static bool ShouldShowBindButton()
    {
        if (!ScriptGeneratorSetting.Instance.UseBindComponent) return false;

        var selectedGameObjects = Selection.gameObjects;
        if (selectedGameObjects.Length == 0) return false;

        foreach (var go in selectedGameObjects)
        {
            if (go == null || !go.TryGetComponent<RectTransform>(out _) || go.TryGetComponent<UIBindComponent>(out _))
                return false;
        }
        return true;
    }

    static bool CreateButton(EditorWindow window)
    {
        if (window.rootVisualElement == null) return false;

        // 查找Add Component按钮
        var addComponentButton = window.rootVisualElement.Q(className: "unity-inspector-add-component-button");
        // UI 尚未构建完成时由下一次 Update 重试，避免堆积 delayCall。
        if (addComponentButton == null) return false;

        // 检查是否已经存在我们的按钮
        var existingHolder = window.rootVisualElement.Q("bind-ui-component-button-holder");
        if (existingHolder != null)
        {
            m_bindButtonsByWindow[window] = existingHolder;
            return true;
        }

        // 创建按钮容器
        var buttonHolder = new VisualElement();
        buttonHolder.name = "bind-ui-component-button-holder";
        buttonHolder.style.flexDirection = FlexDirection.Row;
        buttonHolder.style.justifyContent = Justify.Center;
        buttonHolder.style.marginTop = 5f;
        buttonHolder.style.marginBottom = 5f;

        // 创建按钮
        var button = new Button(OnBindButtonClicked);
        button.name = "bind-ui-component-button";
        button.text = "Bind UI Component";
        button.style.height = 24f;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;

        button.style.width = 230f;
        button.style.height = 25f;
        button.style.marginLeft = 2f;
        button.style.marginRight = 2f;
        button.style.marginTop = -3f;
        button.style.marginBottom = 15f;

        buttonHolder.Add(button);

        // 找到Add Component按钮的父容器，并在其后插入我们的按钮
        var addComponentParent = addComponentButton.parent;
        if (addComponentParent != null)
        {
            // 找到Add Component按钮在父容器中的索引
            int addComponentIndex = addComponentParent.IndexOf(addComponentButton);

            // 在Add Component按钮后面插入我们的按钮
            if (addComponentIndex >= 0)
            {
                addComponentParent.Insert(addComponentIndex + 1, buttonHolder);
            }
            else
            {
                addComponentParent.Add(buttonHolder);
            }

            m_bindButtonsByWindow[window] = buttonHolder;
            return true;
        }
        return false;
    }

    static void DestroyButton(EditorWindow window)
    {
        if (m_bindButtonsByWindow.TryGetValue(window, out var buttonHolder))
        {
            buttonHolder.RemoveFromHierarchy();
            m_bindButtonsByWindow.Remove(window);
        }
    }

    static void OnBindButtonClicked()
    {
        var selectedGameObjects = Selection.gameObjects;
        if (selectedGameObjects.Length <= 0) return;

        for (int i = 0; i < selectedGameObjects.Length; i++)
        {
            var go = selectedGameObjects[i];
            if (go != null && !go.TryGetComponent<UIBindComponent>(out _))
            {
                ScriptGenerator.GenerateUIComponentScript();
                ScriptGenerator.GenerateCSharpScript(false);
                Debug.Log($"{go.name}: Bind UI Component");
            }
        }
    }
}
#endif

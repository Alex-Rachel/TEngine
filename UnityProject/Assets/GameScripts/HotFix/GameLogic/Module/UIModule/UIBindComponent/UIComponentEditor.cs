#if UNITY_EDITOR

using UnityEngine;

namespace GameLogic
{
    public partial class UIBindComponent
    {
        [SerializeField, HideInInspector] private string genCodePath;
        [SerializeField, HideInInspector] private string className;
        [SerializeField, HideInInspector] private string impCodePath;
        [SerializeField, HideInInspector] private bool isGenImpClass;
        [SerializeField, HideInInspector] private string uiType;

        public void AddComponent(Component component)
        {
            if (m_components != null && !m_components.Contains(component))
            {
                m_components.Add(component);
            }
        }

        public void Clear()
        {
            m_components?.Clear();
        }
    }
}

#endif
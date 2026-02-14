#if UNITY_2019_4_OR_NEWER

namespace YooAsset.Editor
{
    /// <summary>
    /// 引擎图标名称
    /// </summary>
    public class UIElementsIcon
    {
        public const string RecordOn = "d_Record On@2x";
        public const string RecordOff = "d_Record Off@2x";

#if UNITY_2019
        public const string FoldoutOn = "IN foldout on";
        public const string FoldoutOff = "IN foldout";
#else
        public const string FoldoutOn = "d_IN_foldout_on@2x";
        public const string FoldoutOff = "d_IN_foldout@2x";
#endif

        public const string VisibilityToggleOff = "animationvisibilitytoggleoff@2x";
        public const string VisibilityToggleOn = "animationvisibilitytoggleon@2x";
    }
}
#endif
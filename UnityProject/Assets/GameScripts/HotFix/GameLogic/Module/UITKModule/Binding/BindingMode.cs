namespace GameLogic
{
    /// <summary>
    /// 数据绑定方向。
    /// </summary>
    public enum BindingMode
    {
        /// <summary>ViewModel → View</summary>
        OneWay,
        /// <summary>ViewModel ↔ View</summary>
        TwoWay,
        /// <summary>View → ViewModel</summary>
        OneWayToSource,
    }
}

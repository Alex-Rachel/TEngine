public enum UILayerTest
{
    /// <summary>
    /// 最低层
    /// 要显示的这个就是最低的层级
    /// </summary>
    Background = 0,

    /// <summary>
    /// 场景层
    /// 比如 血条飘字不是做在3D时 用2D实现时的层
    /// 比如 头像 ...
    /// </summary>
    Scene = 1,

    /// <summary>
    /// 普通面板层
    /// 全屏界面
    /// </summary>
    UI = 2,

    /// <summary>
    /// 弹窗层
    /// 一般是非全屏界面,可同时存在的
    /// </summary>
    Popup = 3,

    /// <summary>
    /// 提示层
    /// 一般 提示飘字 确认弹窗  跑马灯之类的
    /// </summary>
    Tips = 4,

    /// <summary>
    /// 最高层
    /// 一般新手引导之类的
    /// </summary>
    Top = 5,

    All = 6,
}
internal enum EUIResLoadTypeTest : byte
{
    Resources,
    AssetBundle
}
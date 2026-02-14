
namespace YooAsset.Editor
{
    public struct GroupData
    {
        public string GroupName;

        public GroupData(string groupName)
        {
            GroupName = groupName;
        }
    }

    /// <summary>
    /// 资源分组激活规则接口
    /// </summary>
    public interface IActiveRule
    {
        /// <summary>
        /// 是否激活分组
        /// </summary>
        bool IsActiveGroup(GroupData data);
    }
}
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
    public class TreeNode
    {
        /// <summary>
        /// 子节点集合
        /// </summary>
        public List<TreeNode> Children = new List<TreeNode>(10);

        /// <summary>
        /// 父节点
        /// </summary>
        public TreeNode Parent { get; set; }

        /// <summary>
        /// 用户数据
        /// </summary>
        public object UserData { get; set; }

        /// <summary>
        /// 是否展开
        /// </summary>
        public bool IsExpanded { get; set; } = false;


        public TreeNode(object userData)
        {
            UserData = userData;
        }

        /// <summary>
        /// 添加子节点
        /// </summary>
        public void AddChild(TreeNode child)
        {
            child.Parent = this;
            Children.Add(child);
        }

        /// <summary>
        /// 清理所有子节点
        /// </summary>
        public void ClearChildren()
        {
            foreach(var child in Children)
            {
                child.Parent = null;
            }
            Children.Clear();
        }

        /// <summary>
        /// 计算节点的深度
        /// </summary>
        public int GetDepth()
        {
            int depth = 0;
            TreeNode current = this;
            while (current.Parent != null)
            {
                depth++;
                current = current.Parent;
            }
            return depth;
        }
    }
}
#endif
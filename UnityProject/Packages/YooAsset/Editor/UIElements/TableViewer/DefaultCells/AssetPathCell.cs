#if UNITY_2019_4_OR_NEWER
using UnityEditor;

namespace YooAsset.Editor
{
    public class AssetPathCell : StringValueCell
    {
        public AssetPathCell(string searchTag, object cellValue) : base(searchTag, cellValue)
        {
        }

        /// <summary>
        /// 检视资源对象
        /// Ping an asset object in the Scene like clicking it in an inspector.
        /// </summary>
        public bool PingAssetObject()
        {
            var assetPath = StringValue;
            var assetGUID = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(assetGUID))
                return false;

            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null)
                return false;

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            return true;
        }
    }
}
#endif
using UnityEditor;
using UnityEngine;

namespace TEngine.Localization
{
	[CustomEditor(typeof(ResourceManager))]
	public class ResourceManagerInspector : UnityEditor.Editor 
	{
		SerializedProperty mAssets;

		void OnEnable()
		{
			UpgradeManager.EnablePlugins();
			mAssets = serializedObject.FindProperty("Assets");
		}

		public override void OnInspectorGUI()
		{
			GUILayout.Space(5);
			GUITools.DrawHeader("Assets:", true);
			GUITools.BeginContents();
				///GUILayout.Label ("Assets:");
				GUITools.DrawObjectsArray( mAssets );
			GUITools.EndContents();

			serializedObject.ApplyModifiedProperties();
		}
	}
}
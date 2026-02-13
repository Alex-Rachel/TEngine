using UnityEditor;
using UnityEditor.Compilation;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Serialization;

namespace Fantasy
{
    [ScriptableObjectPath("ProjectSettings/FantasySettings.asset")]
    public class FantasySettingsScriptableObject : ScriptableObjectSingleton<FantasySettingsScriptableObject>, ISerializationCallbackReceiver
    {
        [FormerlySerializedAs("AutoCopyAssembly")]
        public bool autoCopyAssembly = false;
        [FormerlySerializedAs("HotUpdatePath")]
        public string hotUpdatePath;
        [FormerlySerializedAs("HotUpdateAssemblyDefinitions")]
        public AssemblyDefinitionAsset[] hotUpdateAssemblyDefinitions;
        [FormerlySerializedAs("LinkAssemblyDefinitions")]
        public AssemblyDefinitionAsset[] linkAssemblyDefinitions;
        [FormerlySerializedAs("IncludeAssembly")]
        public string[] includeAssembly = new[] { "Assembly-CSharp", "Fantasy.Unity" };
        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() { }
    }
}
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using HybridCLR.Editor.Settings;

namespace TEngine.Editor
{
    [CustomEditor(typeof(UpdateSetting), true)]
    public class UpdateSettingEditor : UnityEditor.Editor
    {
#if ENABLE_HYBRIDCLR
        public List<string> HotUpdateAssemblies = new() {};
        public List<string> AOTMetaAssemblies = new() {};
        
        private void OnEnable()
        {
            // 获取当前编辑的 ScriptableObject 实例
            UpdateSetting updateSetting = (UpdateSetting)target;
            if (updateSetting != null)
            {
                HotUpdateAssemblies.AddRange(updateSetting.HotUpdateAssemblies);
                AOTMetaAssemblies.AddRange(updateSetting.AOTMetaAssemblies);
            }
        }

        public override void OnInspectorGUI()
        {
            // 记录对象修改前的状态
            EditorGUI.BeginChangeCheck();

            // 绘制默认的 Inspector 界面
            base.OnInspectorGUI();

            // 检测是否有字段被修改
            if (EditorGUI.EndChangeCheck())
            {
                // 获取当前编辑的 ScriptableObject 实例
                UpdateSetting updateSetting = (UpdateSetting)target;

                // 标记对象为“已修改”，确保修改能被保存
                EditorUtility.SetDirty(updateSetting);
                
                bool isHotChanged = !HotUpdateAssemblies.SequenceEqual(updateSetting.HotUpdateAssemblies);
                bool isAOTChanged = !AOTMetaAssemblies.SequenceEqual(updateSetting.AOTMetaAssemblies);
                if (isHotChanged)
                {
                    HybridCLRSettings.Instance.hotUpdateAssemblies = updateSetting.HotUpdateAssemblies.ToArray();
                    for (int i = 0; i < updateSetting.HotUpdateAssemblies.Count; i++)
                    {
                        var assemblyName = updateSetting.HotUpdateAssemblies[i];
                        string assemblyNameWithoutExtension = assemblyName.Substring(0, assemblyName.LastIndexOf('.'));
                        HybridCLRSettings.Instance.hotUpdateAssemblies[i] = assemblyNameWithoutExtension;
                    }
                    Debug.Log("HotUpdateAssemblies changed");
                }
                if (isAOTChanged)
                {
                    HybridCLRSettings.Instance.patchAOTAssemblies = updateSetting.AOTMetaAssemblies.ToArray();
                    Debug.Log("AOTMetaAssemblies changed");
                }

                if (isAOTChanged || isHotChanged)
                {
                    // 在修改HybridCLRSettings后添加
                    EditorUtility.SetDirty(HybridCLRSettings.Instance);
                    HybridCLRSettings.Save();
                    AssetDatabase.SaveAssets();
                }
            }
        }
#endif

        public static void ForceUpdateAssemblies()
        {
            UpdateSetting updateSetting = null;
            string[] guids = AssetDatabase.FindAssets("t:UpdateSetting");
            if (guids.Length >= 1)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                updateSetting = AssetDatabase.LoadAssetAtPath<UpdateSetting>(path);
            }

            if (updateSetting == null)
            {
                Log.Error("Can not find UpdateSetting");
                return;
            }
            
            HybridCLRSettings.Instance.hotUpdateAssemblies = updateSetting.HotUpdateAssemblies.ToArray();
            for (int i = 0; i < updateSetting.HotUpdateAssemblies.Count; i++)
            {
                var assemblyName = updateSetting.HotUpdateAssemblies[i];
                string assemblyNameWithoutExtension = assemblyName.Substring(0, assemblyName.LastIndexOf('.'));
                HybridCLRSettings.Instance.hotUpdateAssemblies[i] = assemblyNameWithoutExtension;
            }
            
            HybridCLRSettings.Instance.patchAOTAssemblies = updateSetting.AOTMetaAssemblies.ToArray();
            HybridCLRSettings.Save();
            EditorUtility.SetDirty(HybridCLRSettings.Instance);
            AssetDatabase.SaveAssets();
            
            Debug.Log("HotUpdateAssemblies changed");
        }
    }
}
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace TEngine
{
    public partial class Utility
    {
        public static partial class MaterialHelper
        {
#if UNITY_EDITOR&&EditorFixedMaterialShader
            public static void FixedMaterialShader_All(Transform transform)
            {
                FixedMaterialShader_GameObject(transform);
                FixedMaterialShader_UI(transform);
                FixedMaterialShader_Tmp(transform);
            }


            /// <summary>
            /// 修复对象材质shader
            /// </summary>
            public static void FixedMaterialShader_GameObject(Transform transform)
            {
                Renderer[] renderer = transform.GetComponentsInChildren<Renderer>();
                for (int i = 0; i < renderer.Length; i++)
                {
                    Material[] mats = renderer[i].sharedMaterials;
                    for (int j = 0; j < mats.Length; j++)
                    {
                        mats[j].shader = Shader.Find(mats[j].shader.name);
                    }

                    renderer[i].sharedMaterials = mats;
                }
            }

            /// <summary>
            /// 修复ui上带的材质
            /// </summary>
            public static void FixedMaterialShader_UI(Transform transform)
            {
                UnityEngine.UI.Graphic[] graphics = transform.GetComponentsInChildren<UnityEngine.UI.Graphic>();
                for (int i = 0; i < graphics.Length; i++)
                {
                    Material mat = graphics[i].material;
                    if (mat != null)
                    {
                        mat.shader = Shader.Find(mat.shader.name);
                    }
                }
            }

            /// <summary>
            /// 修复TMP字体
            /// </summary>
            public static void FixedMaterialShader_Tmp(Transform transform)
            {
#if TextMeshPro
                TMPro.TextMeshProUGUI[] tmp = transform.GetComponentsInChildren<TMPro.TextMeshProUGUI>();
                for (int i = 0; i < tmp.Length; i++)
                {
                    Material mat = tmp[i].fontSharedMaterial;
                    if (mat != null)
                    {
                        mat.shader = Shader.Find(mat.shader.name);
                    }
                }
#endif
            }

            /// <summary>
            /// 修复场景材质shader和天空盒shader
            /// </summary>
            public static void FixedMaterialShader_Scenne(GameObject[] obj)
            {
                for (int i = 0; i < obj.Length; i++)
                {
                    FixedMaterialShader_GameObject(obj[i].transform);
                }

                //修复天空盒材质
                if (RenderSettings.skybox != null)
                {
                    RenderSettings.skybox.shader = Shader.Find(RenderSettings.skybox.shader.name);
                }
            }

            /// <summary>
            /// 等待能获取到场景物体时，修复材质shader
            /// </summary>
            public static async UniTaskVoid WaitGetRootGameObjects(SceneHandle sceneHandle)
            {
                await UniTask.WaitUntil(() => sceneHandle.SceneObject.GetRootGameObjects().Length > 0);
                FixedMaterialShader_Scenne(sceneHandle.SceneObject.GetRootGameObjects());
            }
#endif
        }
    }
}
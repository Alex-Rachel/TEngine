// using System.Reflection;
// using Fantasy.Assembly;
// using Fantasy.Async;
// // using UnityEngine;
// #pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
// #pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
// #pragma warning disable CS8603 // Possible null reference return.
// #pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
// #pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
//
// namespace Fantasy.Platform.Unity
// {
//     public class MonoBehaviour
//     {
//     
//     }
//
//     public class GameObject
//     {
//         public GameObject(string name)
//         {
//         
//         }
//     }
//
//     internal enum RuntimeInitializeLoadType
//     {
//         BeforeSceneLoad = 1,
//     }
//
//     internal class RuntimeInitializeOnLoadMethodAttribute : Attribute
//     {
//         public RuntimeInitializeLoadType RuntimeInitializeLoadType;
//
//         public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType)
//         {
//         
//         }
//     }
//     
//     public sealed class FantasyObject : MonoBehaviour
//     {
//         public static GameObject FantasyObjectGameObject { get; private set; }
//         // 这个方法将在游戏启动时自动调用
//         [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
//         static void OnRuntimeMethodLoad()
//         {
//             FantasyObjectGameObject = new GameObject("Fantasy.Net");
//             // DontDestroyOnLoad(FantasyObjectGameObject);
//         }
//         private void OnApplicationQuit()
//         {
//             // Destroy(FantasyObjectGameObject);
//         }
//     }
//     
//     public struct OnFantasyInit
//     {
//         public Scene Scene;
//     }
//     
//     public class Entry : MonoBehaviour
//     {
//         private static bool _isInit;
//         public static Scene Scene { get; private set; }
//         /// <summary>
//         /// 初始化框架
//         /// </summary>
//         public static async FTask<Scene> Initialize(params System.Reflection.Assembly[] assemblies)
//         {
//             Scene?.Dispose();
//             // 初始化程序集管理系统
//             AssemblySystem.Initialize(assemblies);
//             if (!_isInit)
//             {
// #if FANTASY_WEBGL
//                 ThreadSynchronizationContext.Initialize();
// #endif
//                 _isInit = true;
//                 // FantasyObject.FantasyObjectGameObject.AddComponent<Entry>();
//             }
//             // Scene = await Scene.Create(SceneRuntimeType.MainThread);
//             // await Scene.EventComponent.PublishAsync(new OnFantasyInit()
//             // {
//             //     Scene = Scene
//             // });
//             // return Scene;
//             await FTask.CompletedTask;
//             return null;
//         }
//         
//         private void Update()
//         {
//             ThreadScheduler.Update();
//         }
//
//         private void OnDestroy()
//         {
//             AssemblySystem.Dispose();
//             if (Scene != null)
//             {
//                 Scene?.Dispose();
//                 Scene = null;
//             }
//             _isInit = false;
//         }
//     }
// }

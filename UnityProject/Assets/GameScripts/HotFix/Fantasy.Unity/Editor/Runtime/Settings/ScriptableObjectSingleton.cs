using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditorInternal;
using UnityEngine;
// ReSharper disable AssignNullToNotNullAttribute

namespace Fantasy
{
    public class ScriptableObjectSingleton<T> : ScriptableObject where T : ScriptableObject
    {
        private static T _instance;

        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Load();
                }

                return _instance;
            }
        }

        private static T Load()
        {
            var scriptableObjectPath = GetScriptableObjectPath();

            if (string.IsNullOrEmpty(scriptableObjectPath))
            {
                return null; 
            }
            
            var loadSerializedFileAndForget = InternalEditorUtility.LoadSerializedFileAndForget(scriptableObjectPath);

            if (loadSerializedFileAndForget.Length <= 0)
            {
                return CreateInstance<T>();
            }
            
            return loadSerializedFileAndForget[0] as T;
        }

        public static void Save(bool saveAsText = true)
        {
            if (_instance == null)
            {
                Debug.LogError("Cannot save ScriptableObjectSingleton: no instance!");
                return;
            }

            var scriptableObjectPath = GetScriptableObjectPath();

            if (string.IsNullOrEmpty(scriptableObjectPath))
            {
                return;
            }

            var directoryName = Path.GetDirectoryName(scriptableObjectPath);

            if (!Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }
            
            UnityEngine.Object[] obj = { _instance };
            InternalEditorUtility.SaveToSerializedFileAndForget(obj, scriptableObjectPath, saveAsText);
        }

        private static string GetScriptableObjectPath()
        {
            var scriptableObjectPathAttribute = typeof(T).GetCustomAttribute(typeof(ScriptableObjectPathAttribute)) as ScriptableObjectPathAttribute;
            return scriptableObjectPathAttribute?.ScriptableObjectPath;
        }
    }
    
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class ScriptableObjectPathAttribute : Attribute
    {
        internal readonly string ScriptableObjectPath;

        public ScriptableObjectPathAttribute(string scriptableObjectPath)
        {
            if (string.IsNullOrEmpty(scriptableObjectPath))
            {
                throw new ArgumentException("Invalid relative path (it is empty)");
            }
            
            if (scriptableObjectPath[0] == '/')
            {
                scriptableObjectPath = scriptableObjectPath.Substring(1);
            }
            
            ScriptableObjectPath = scriptableObjectPath;
        }
    }
}
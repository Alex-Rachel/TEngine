using System;
using System.IO;
using System.Linq;
using UnityEditorInternal;
using UnityEngine;

namespace TEngine.Editor
{
    public class EditorScriptableSingleton<T> : ScriptableObject where T : ScriptableObject
    {
        private static T _instance;

        public static T Instance
        {
            get
            {
                if (!_instance)
                {
                    LoadOrCreate();
                }

                return _instance;
            }
        }

        public static T LoadOrCreate()
        {
            string filePath = GetFilePath();
            if (!string.IsNullOrEmpty(filePath))
            {
                var arr = InternalEditorUtility.LoadSerializedFileAndForget(filePath);
                _instance = arr.Length > 0 ? arr[0] as T : _instance ?? CreateInstance<T>();
            }
            else
            {
                Debug.LogError($"save location of {nameof(EditorScriptableSingleton<T>)} is invalid");
            }

            return _instance;
        }

        public static void Save(bool saveAsText = true)
        {
            if (!_instance)
            {
                Debug.LogError("Cannot save ScriptableSingleton: no instance!");
                return;
            }

            string filePath = GetFilePath();
            if (!string.IsNullOrEmpty(filePath))
            {
                string directoryName = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directoryName))
                {
                    if (directoryName != null)
                    {
                        Directory.CreateDirectory(directoryName);
                    }
                }

                UnityEngine.Object[] obj = { _instance };
                InternalEditorUtility.SaveToSerializedFileAndForget(obj, filePath, saveAsText);
            }
        }

        protected static string GetFilePath()
        {
            return typeof(T).GetCustomAttributes(inherit: true)
                .Where(v => v is FilePathAttribute)
                .Cast<FilePathAttribute>()
                .FirstOrDefault()
                ?.Filepath;
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class FilePathAttribute : Attribute
    {
        internal readonly string Filepath;

        /// <summary>
        /// 单例存放路径。
        /// </summary>
        /// <param name="path">相对 Project 路径。</param>
        public FilePathAttribute(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Invalid relative path (it is empty)");
            }

            if (path[0] == '/')
            {
                path = path.Substring(1);
            }

            Filepath = path;
        }
    }
}
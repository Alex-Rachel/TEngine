using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fantasy
{
    public class LinkXmlGenerator
    {
        private const string LinkPath = "Assets/link.xml";
        // 在Unity编辑器中运行该方法来生成link.xml文件
        public static void GenerateLinkXml()
        {
            using (var writer = new StreamWriter("Assets/link.xml"))
            {
                var assemblyHashSet = new HashSet<string>();
                
                foreach (var assembly in FantasySettingsScriptableObject.Instance.includeAssembly)
                {
                    assemblyHashSet.Add(assembly);
                }

                if (FantasySettingsScriptableObject.Instance?.linkAssemblyDefinitions != null)
                {
                    foreach (var assemblyDefinition in FantasySettingsScriptableObject.Instance.linkAssemblyDefinitions)
                    {
                        assemblyHashSet.Add(assemblyDefinition.name);
                    }
                }

                if (assemblyHashSet.Count == 0)
                {
                    return;
                }
                
                writer.WriteLine("<linker>");
                
                foreach (var assembly in assemblyHashSet)
                {
                    GenerateLinkXml(writer, assembly, LinkPath);
                    Debug.Log($"{assembly} Link generation completed");
                }
                
                writer.WriteLine("</linker>");
            }

            AssetDatabase.Refresh();
            Debug.Log("link.xml generated successfully!");
        }

        private static void GenerateLinkXml(StreamWriter writer, string assemblyName, string outputPath)
        {
            var assembly = System.Reflection.Assembly.Load(assemblyName);
            var types = assembly.GetTypes();
            writer.WriteLine($"  <assembly fullname=\"{assembly.GetName().Name}\">");
            foreach (var type in types)
            {
                var typeName = type.FullName.Replace('<', '+').Replace('>', '+');
                writer.WriteLine($"    <type fullname=\"{typeName}\" preserve=\"all\"/>");
            }
            writer.WriteLine("  </assembly>");
        }
    }
}
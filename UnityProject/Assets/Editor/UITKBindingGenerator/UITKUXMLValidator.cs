using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEngine;

namespace TEngine.Editor.UITK
{
    /// <summary>
    /// UXML 文件解析与校验。
    /// </summary>
    public static class UITKUXMLValidator
    {
        public struct UXMLElement
        {
            public string Name;
            public string TypeName;
        }

        /// <summary>
        /// 解析 UXML 文件，提取所有带 name 属性的元素。
        /// </summary>
        public static List<UXMLElement> ParseUXML(string uxmlPath)
        {
            var elements = new List<UXMLElement>();
            if (!File.Exists(uxmlPath)) return elements;

            try
            {
                string content = File.ReadAllText(uxmlPath);
                using var reader = XmlReader.Create(new StringReader(content));
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element) continue;
                    string name = reader.GetAttribute("name");
                    if (string.IsNullOrEmpty(name)) continue;

                    string typeName = MapElementType(reader.LocalName);
                    elements.Add(new UXMLElement { Name = name, TypeName = typeName });
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[UITKUXMLValidator] Failed to parse {uxmlPath}: {e.Message}");
            }
            return elements;
        }

        /// <summary>
        /// 校验元素名是否存在于 UXML 中。
        /// </summary>
        public static bool ValidateElement(List<UXMLElement> elements, string uxmlName, out string foundType)
        {
            foundType = null;
            foreach (var e in elements)
            {
                if (e.Name == uxmlName)
                {
                    foundType = e.TypeName;
                    return true;
                }
            }
            return false;
        }

        private static string MapElementType(string xmlLocalName)
        {
            return xmlLocalName switch
            {
                "Button" => "Button",
                "Label" => "Label",
                "TextField" => "TextField",
                "Toggle" => "Toggle",
                "Slider" => "Slider",
                "SliderInt" => "SliderInt",
                "DropdownField" => "DropdownField",
                "RadioButton" => "RadioButton",
                "RadioButtonGroup" => "RadioButtonGroup",
                "Foldout" => "Foldout",
                "ScrollView" => "ScrollView",
                "ListView" => "ListView",
                "TreeView" => "TreeView",
                "ProgressBar" => "ProgressBar",
                "MinMaxSlider" => "MinMaxSlider",
                "Image" => "Image",
                "VisualElement" => "VisualElement",
                "TemplateContainer" => "TemplateContainer",
                "GroupBox" => "GroupBox",
                _ => "VisualElement",
            };
        }
    }
}

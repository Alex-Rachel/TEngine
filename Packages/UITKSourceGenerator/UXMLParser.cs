using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace UITKSourceGenerator
{
    public struct UXMLElement
    {
        public string Name;
        public string TypeName;
    }

    public static class UXMLParser
    {
        public static List<UXMLElement> Parse(string uxmlContent)
        {
            var elements = new List<UXMLElement>();
            if (string.IsNullOrEmpty(uxmlContent)) return elements;

            try
            {
                using var reader = XmlReader.Create(new StringReader(uxmlContent));
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element) continue;

                    string name = reader.GetAttribute("name");
                    if (string.IsNullOrEmpty(name)) continue;

                    string typeName = MapElementType(reader.LocalName);
                    elements.Add(new UXMLElement { Name = name, TypeName = typeName });
                }
            }
            catch
            {
                // Silently fail on malformed UXML
            }
            return elements;
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

using System;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.Pipeline.Editor.Commands.Scripts
{
    /// <summary>
    /// Bridges agent-supplied JSON values and Unity's <see cref="SerializedProperty"/> model for the
    /// set_serialized_field / get_serialized_fields commands (CLI-195).
    ///
    /// Supported property kinds: integers, floats, bools, strings, enums, Vector2/3/4, Color,
    /// Quaternion, Rect, Bounds, and object references. Object references accept an <see cref="ObjectRef"/>
    /// handle (resolved via <see cref="ObjectResolver"/>) — assets by GUID/fileId or path, and scene
    /// objects by instanceId/hierarchyPath. Arrays are addressed element-by-element via a path like
    /// "myArray.Array.data[2]" (Unity's native SerializedProperty path syntax) — see the commands.
    /// </summary>
    static class SerializedPropertyConverter
    {
        /// <summary>
        /// Assign a JSON value to a serialized property, converting by the property's type. Throws
        /// <see cref="ArgumentException"/> with a clear message when the value can't be coerced or the
        /// property type isn't supported. The caller is responsible for ApplyModifiedProperties.
        /// </summary>
        public static void SetValue(SerializedProperty prop, JToken value)
        {
            // Whole-array assignment: an array/list field (Generic + isArray) set from a JSON array —
            // resize, then recurse per element (handles primitive and object-reference arrays alike).
            // Element-by-element addressing via "field.Array.data[i]" / "field.Array.size" still works.
            if (prop.isArray && prop.propertyType != SerializedPropertyType.String)
            {
                if (!(value is JArray array))
                    throw new ArgumentException(
                        $"Field '{prop.propertyPath}' is an array; provide a JSON array of elements.");

                prop.arraySize = array.Count;
                for (int i = 0; i < array.Count; i++)
                    SetValue(prop.GetArrayElementAtIndex(i), array[i]);
                return;
            }

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    prop.longValue = value.ToObject<long>();
                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = value.ToObject<bool>();
                    break;
                case SerializedPropertyType.Float:
                    prop.doubleValue = value.ToObject<double>();
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = value.ToObject<string>();
                    break;
                case SerializedPropertyType.Enum:
                    SetEnum(prop, value);
                    break;
                case SerializedPropertyType.Vector2:
                    prop.vector2Value = ToVector2(value);
                    break;
                case SerializedPropertyType.Vector3:
                    prop.vector3Value = ToVector3(value);
                    break;
                case SerializedPropertyType.Vector4:
                    prop.vector4Value = ToVector4(value);
                    break;
                case SerializedPropertyType.Quaternion:
                    var v4 = ToVector4(value);
                    prop.quaternionValue = new Quaternion(v4.x, v4.y, v4.z, v4.w);
                    break;
                case SerializedPropertyType.Color:
                    prop.colorValue = ToColor(value);
                    break;
                case SerializedPropertyType.Rect:
                    prop.rectValue = ToRect(value);
                    break;
                case SerializedPropertyType.Bounds:
                    prop.boundsValue = ToBounds(value);
                    break;
                case SerializedPropertyType.ObjectReference:
                    prop.objectReferenceValue = ResolveObjectReference(value);
                    break;
                case SerializedPropertyType.ArraySize:
                    // Resizing an array via "<field>.Array.size" — the property is an int under the hood.
                    prop.intValue = value.ToObject<int>();
                    break;
                default:
                    throw new ArgumentException(
                        $"Field '{prop.propertyPath}' has unsupported type '{prop.propertyType}'. " +
                        "Supported: int, float, bool, string, enum, Vector2/3/4, Quaternion, Color, Rect, Bounds, object reference, array size.");
            }
        }

        /// <summary>
        /// Read a serialized property into a plain object suitable for JSON serialization. Object
        /// references are described via <see cref="ObjectResolver.Describe"/> so the agent gets a
        /// re-usable handle; arrays are returned as a length plus per-element values.
        /// </summary>
        public static object GetValue(SerializedProperty prop)
        {
            // Array/list fields are returned as a JSON array of their elements so they round-trip with
            // SetValue. (String also reports isArray, hence the exclusion.)
            if (prop.isArray && prop.propertyType != SerializedPropertyType.String)
            {
                var elements = new JArray();
                for (int i = 0; i < prop.arraySize; i++)
                {
                    var v = GetValue(prop.GetArrayElementAtIndex(i));
                    elements.Add(v == null ? JValue.CreateNull() : JToken.FromObject(v));
                }

                return elements;
            }

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return prop.longValue;
                case SerializedPropertyType.Boolean:
                    return prop.boolValue;
                case SerializedPropertyType.Float:
                    return prop.doubleValue;
                case SerializedPropertyType.String:
                    return prop.stringValue;
                case SerializedPropertyType.Enum:
                    return prop.enumValueIndex >= 0 && prop.enumValueIndex < prop.enumNames.Length
                        ? prop.enumNames[prop.enumValueIndex]
                        : (object)prop.intValue;
                case SerializedPropertyType.Vector2:
                    return new { x = prop.vector2Value.x, y = prop.vector2Value.y };
                case SerializedPropertyType.Vector3:
                    return new { x = prop.vector3Value.x, y = prop.vector3Value.y, z = prop.vector3Value.z };
                case SerializedPropertyType.Vector4:
                    return new { x = prop.vector4Value.x, y = prop.vector4Value.y, z = prop.vector4Value.z, w = prop.vector4Value.w };
                case SerializedPropertyType.Quaternion:
                    return new { x = prop.quaternionValue.x, y = prop.quaternionValue.y, z = prop.quaternionValue.z, w = prop.quaternionValue.w };
                case SerializedPropertyType.Color:
                    return new { r = prop.colorValue.r, g = prop.colorValue.g, b = prop.colorValue.b, a = prop.colorValue.a };
                case SerializedPropertyType.Rect:
                    return new { x = prop.rectValue.x, y = prop.rectValue.y, width = prop.rectValue.width, height = prop.rectValue.height };
                case SerializedPropertyType.Bounds:
                    var b = prop.boundsValue;
                    return new { center = new { x = b.center.x, y = b.center.y, z = b.center.z }, size = new { x = b.size.x, y = b.size.y, z = b.size.z } };
                case SerializedPropertyType.ObjectReference:
                    return ObjectResolver.Describe(prop.objectReferenceValue);
                case SerializedPropertyType.ArraySize:
                    return prop.intValue;
                default:
                    // Unknown/unsupported types are reported by kind rather than failing a whole read.
                    return new { unsupported = prop.propertyType.ToString() };
            }
        }

        private static void SetEnum(SerializedProperty prop, JToken value)
        {
            // Accept either the enum name (preferred, stable across reordering) or the numeric index.
            if (value.Type == JTokenType.String)
            {
                var name = value.ToObject<string>();
                var idx = Array.IndexOf(prop.enumNames, name);
                if (idx < 0)
                    idx = Array.IndexOf(prop.enumDisplayNames, name);
                if (idx < 0)
                    throw new ArgumentException(
                        $"Enum '{name}' is not a valid value for '{prop.propertyPath}'. Valid: {string.Join(", ", prop.enumNames)}.");
                prop.enumValueIndex = idx;
            }
            else
            {
                var idx = value.ToObject<int>();
                if (idx < 0 || idx >= prop.enumNames.Length)
                    throw new ArgumentException(
                        $"Enum index {idx} is out of range for '{prop.propertyPath}' (valid 0..{prop.enumNames.Length - 1}). " +
                        $"Valid values: {string.Join(", ", prop.enumNames)}.");
                prop.enumValueIndex = idx;
            }
        }

        private static Object ResolveObjectReference(JToken value)
        {
            // An explicit null clears the reference; anything else MUST resolve — never silently no-op
            // (an unresolved handle used to be dropped, so the command reported success while assigning
            // nothing).
            if (value == null || value.Type == JTokenType.Null)
                return null;

            var handle = value.ToObject<ObjectRef>();
            if (handle == null || handle.IsEmpty)
                throw new ArgumentException(
                    $"'{value}' is not a recognized object reference handle. " +
                    "Provide a path, guid, globalId, or instanceId (or null to clear).");

            if (!ObjectResolver.TryResolve(handle, out var obj, out var error))
                throw new ArgumentException($"Could not resolve object reference: {error}");

            return obj;
        }

        private static float F(JToken t, string key, float fallback = 0f)
        {
            // Indexing a non-object token (e.g. the agent passed a number or array for a Vector3/Color)
            // with a string key throws an opaque InvalidOperationException — validate first so the
            // caller gets a clear, actionable ArgumentException instead.
            if (!(t is JObject obj))
                throw new ArgumentException(
                    $"Expected a JSON object with named components (e.g. {{ \"{key}\": 0 }}) " +
                    $"but received a '{t?.Type.ToString() ?? "null"}'.");

            var token = obj[key];
            return token != null ? token.ToObject<float>() : fallback;
        }

        private static Vector2 ToVector2(JToken t) => new Vector2(F(t, "x"), F(t, "y"));
        private static Vector3 ToVector3(JToken t) => new Vector3(F(t, "x"), F(t, "y"), F(t, "z"));
        private static Vector4 ToVector4(JToken t) => new Vector4(F(t, "x"), F(t, "y"), F(t, "z"), F(t, "w"));

        private static Color ToColor(JToken t) => new Color(F(t, "r"), F(t, "g"), F(t, "b"), F(t, "a", 1f));

        private static Rect ToRect(JToken t) => new Rect(F(t, "x"), F(t, "y"), F(t, "width"), F(t, "height"));

        private static Bounds ToBounds(JToken t)
        {
            if (!(t is JObject obj))
                throw new ArgumentException(
                    $"Expected a JSON object with 'center' and 'size' components but received a " +
                    $"'{t?.Type.ToString() ?? "null"}'.");

            var center = obj["center"] != null ? ToVector3(obj["center"]) : Vector3.zero;
            var size = obj["size"] != null ? ToVector3(obj["size"]) : Vector3.zero;
            return new Bounds(center, size);
        }
    }
}

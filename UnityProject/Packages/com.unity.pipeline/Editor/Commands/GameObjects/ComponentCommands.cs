using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Models;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor.Commands.GameObjects
{
    /// <summary>
    /// Component authoring commands (CLI-192): add/remove a component on a GameObject and get/set its
    /// serialized properties.
    ///
    /// WHY property get/set goes exclusively through <see cref="SerializedObject"/> /
    /// <see cref="SerializedProperty"/>: it is the only mutation path that dirties the object, records
    /// prefab overrides, and registers a single Undo step (via <see cref="Undo.RecordObject"/> +
    /// <see cref="SerializedObject.ApplyModifiedProperties"/>) the way the Inspector does. Direct
    /// reflection on backing fields would silently desync the Editor. Component types are resolved by
    /// name across all loaded assemblies (<see cref="TypeResolver"/>) so agents can use short or
    /// fully-qualified names.
    /// </summary>
    static class ComponentCommands
    {
        const string Group = "component";

        /// <summary>
        /// Add a component (resolved by type name) to a GameObject. Registered with
        /// <see cref="Undo.AddComponent{T}(GameObject)"/> via the non-generic overload so the addition
        /// is reversible. Returns the new component's identity so its properties can be set next.
        /// </summary>
        [CliGroupCommand(Group, "Add a component (by type name) to a GameObject.", Tags = new[] { "gameobjects/components" })]
        public static AuthoringResult Add(
            [CliArg("target", "Handle of the GameObject.", Required = true)] ObjectRef target,
            [CliArg("type", "Component type name (e.g. 'Rigidbody' or 'UnityEngine.Camera').", Required = true)] string type)
        {
            var go = GameObjectCommands.ResolveGameObject(target, "target");
            var componentType = TypeResolver.ResolveComponentType(type);
            if (componentType == null)
                throw new ArgumentException($"Could not resolve component type '{type}'.");

            using (new AuthoringUndoScope("Add Component"))
            {
                var component = Undo.AddComponent(go, componentType);
                if (component == null)
                    throw new InvalidOperationException(
                        $"Failed to add component '{componentType.Name}' to '{go.name}' (it may be disallowed on this GameObject).");

                GameObjectCommands.MarkDirty(go);
                return ObjectResolver.Describe(component);
            }
        }

        /// <summary>
        /// Remove a component from a GameObject. The component is addressed directly by handle (an
        /// <see cref="ObjectRef"/> resolving to a Component), or by GameObject handle plus a type name.
        /// Uses <see cref="Undo.DestroyObjectImmediate"/> so the removal is reversible.
        /// </summary>
        [CliGroupCommand(Group,
            "Remove a component from a GameObject. Provide either a component handle (target) or a GameObject handle (target) plus a type name.",
            Tags = new[] { "gameobjects/components" })]
        public static AuthoringResult Remove(
            [CliArg("target", "Handle of the component to remove, OR of the GameObject when 'type' is given.", Required = true)] ObjectRef target,
            [CliArg("type", "Component type name to remove from the target GameObject (omit when 'target' already points at a component).")] string type = null)
        {
            var component = ResolveComponent(target, type);
            var go = component.gameObject;
            var described = ObjectResolver.Describe(component);

            using (new AuthoringUndoScope("Remove Component"))
            {
                Undo.DestroyObjectImmediate(component);
                GameObjectCommands.MarkDirty(go);
            }

            return described;
        }

        /// <summary>
        /// Read the serialized properties of a component as a JSON map. Iterates the visible serialized
        /// surface (skipping the script reference) and converts each property with
        /// <see cref="SerializedPropertyConverter.Read"/>.
        /// </summary>
        [CliGroupCommand(Group,
            "Get a component's serialized properties as a JSON map. Address the component by handle, or by GameObject handle + type.",
            Tags = new[] { "gameobjects/components" })]
        public static ComponentPropertiesResult GetProperties(
            [CliArg("target", "Handle of the component, OR of the GameObject when 'type' is given.", Required = true)] ObjectRef target,
            [CliArg("type", "Component type name on the target GameObject (omit when 'target' is a component handle).")] string type = null)
        {
            var component = ResolveComponent(target, type);
            var so = new SerializedObject(component);

            var properties = new Dictionary<string, JToken>();
            var iterator = so.GetIterator();
            var enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false; // only iterate top-level visible properties
                if (iterator.name == "m_Script")
                    continue;

                try
                {
                    properties[iterator.name] = SerializedPropertyConverter.Read(iterator.Copy());
                }
                catch (Exception ex)
                {
                    properties[iterator.name] = new JValue($"<error:{ex.Message}>");
                }
            }

            return new ComponentPropertiesResult
            {
                Component = ObjectResolver.Describe(component),
                Properties = properties
            };
        }

        /// <summary>
        /// Set one or more serialized properties on a component. Each entry in <paramref name="properties"/>
        /// maps a property path (e.g. "m_Mass" or "myField") to a JSON value. The whole batch is one
        /// Undo step: <see cref="Undo.RecordObject"/> snapshots the component, every property is written
        /// through <see cref="SerializedPropertyConverter.Write"/>, and
        /// <see cref="SerializedObject.ApplyModifiedProperties"/> commits + dirties as one operation.
        /// An unknown property name fails the whole batch with a clear error (no partial apply).
        /// </summary>
        [CliGroupCommand(Group,
            "Set serialized properties on a component (one Undo step). 'properties' maps property name -> value; object references accept an ObjectRef handle.",
            Tags = new[] { "gameobjects/components" })]
        public static ComponentPropertiesResult SetProperties(
            [CliArg("target", "Handle of the component, OR of the GameObject when 'type' is given.", Required = true)] ObjectRef target,
            [CliArg("properties", "Map of serialized property name to value. Vectors/colors are arrays; object refs are handle objects.", Required = true)] JObject properties,
            [CliArg("type", "Component type name on the target GameObject (omit when 'target' is a component handle).")] string type = null)
        {
            if (properties == null || properties.Count == 0)
                throw new ArgumentException("'properties' must contain at least one property to set.");

            var component = ResolveComponent(target, type);

            using (new AuthoringUndoScope("Set Component Properties"))
            {
                Undo.RecordObject(component, "Set Component Properties");
                var so = new SerializedObject(component);

                foreach (var pair in properties)
                {
                    var property = so.FindProperty(pair.Key);
                    if (property == null)
                        throw new ArgumentException(
                            $"Component '{component.GetType().Name}' has no serialized property '{pair.Key}'.");

                    SerializedPropertyConverter.Write(property, pair.Value);
                }

                // Applies the changes, registers Undo, and dirties the object/scene.
                so.ApplyModifiedProperties();
                GameObjectCommands.MarkDirty(component.gameObject);
            }

            // Re-read so the caller sees the committed values.
            return GetProperties(target, type);
        }

        #region Helpers

        /// <summary>
        /// Resolve a component either directly (the handle points at a Component) or by GameObject
        /// handle + type name. Throws a descriptive error when neither resolves.
        /// </summary>
        internal static Component ResolveComponent(ObjectRef handle, string type)
        {
            if (!ObjectResolver.TryResolve(handle, out var obj, out var error))
                throw new ArgumentException($"Could not resolve 'target': {error}");

            // The handle already points at a specific Component: honour it directly so we never
            // mutate/remove the wrong instance when the GameObject has several components of the same
            // type. A 'type' here is only a constraint to validate against, NOT a reason to re-fetch
            // GetComponent(type) (which returns the first match and would ignore the handle).
            if (obj is Component directComponent)
            {
                if (!string.IsNullOrEmpty(type))
                {
                    var requestedType = TypeResolver.ResolveComponentType(type);
                    if (requestedType == null)
                        throw new ArgumentException($"Could not resolve component type '{type}'.");
                    if (!requestedType.IsInstanceOfType(directComponent))
                        throw new ArgumentException(
                            $"'target' resolves to a {directComponent.GetType().Name}, which is not a '{requestedType.Name}'.");
                }

                return directComponent;
            }

            var go = obj as GameObject;
            if (go == null)
                throw new ArgumentException($"'target' did not resolve to a GameObject or Component (got {obj.GetType().Name}).");

            if (string.IsNullOrEmpty(type))
                throw new ArgumentException("'type' is required when 'target' is a GameObject.");

            var componentType = TypeResolver.ResolveComponentType(type);
            if (componentType == null)
                throw new ArgumentException($"Could not resolve component type '{type}'.");

            var component = go.GetComponent(componentType);
            if (component == null)
                throw new ArgumentException($"GameObject '{go.name}' has no component of type '{componentType.Name}'.");

            return component;
        }

        #endregion
    }
}

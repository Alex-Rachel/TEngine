using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Unity.Pipeline
{
    /// <summary>
    /// Version-stable handle to a Unity object id. In 6000.4 Unity replaced the int instance id with
    /// <see cref="EntityId"/> (a ulong-backed handle), made <c>Object.GetInstanceID()</c> /
    /// <c>EditorUtility.InstanceIDToObject(int)</c> obsolete-as-error, and is removing the
    /// <c>EntityId</c>&lt;-&gt;<c>int</c> conversions. This struct stores the concrete id type for the
    /// running editor — <see cref="EntityId"/> on 6000.4+, <c>int</c> below — and converts implicitly to
    /// it so it can be passed straight to Unity APIs. All version branching for object ids is confined
    /// here. It (de)serializes as a single JSON integer (the raw ulong on 6000.4+, the int below).
    /// </summary>
    [JsonConverter(typeof(ObjectIdConverter))]
    public readonly struct ObjectId : System.IEquatable<ObjectId>
    {
#if UNITY_6000_4_OR_NEWER
        readonly EntityId m_Value;
        /// <summary>Wrap an <see cref="EntityId"/>.</summary>
        /// <param name="value">The entity id to wrap.</param>
        public ObjectId(EntityId value) { m_Value = value; }
        /// <summary>Unwrap to the underlying <see cref="EntityId"/>.</summary>
        /// <param name="id">The id to unwrap.</param>
        /// <returns>The underlying entity id.</returns>
        public static implicit operator EntityId(ObjectId id) => id.m_Value;
        /// <summary>Wrap an <see cref="EntityId"/>.</summary>
        /// <param name="value">The entity id to wrap.</param>
        /// <returns>The wrapped id.</returns>
        public static implicit operator ObjectId(EntityId value) => new ObjectId(value);
        /// <summary>Raw 64-bit id — the canonical wire / serialization form.</summary>
        public ulong RawValue => EntityId.ToULong(m_Value);
        /// <summary>Reconstruct an <see cref="ObjectId"/> from its raw wire value.</summary>
        /// <param name="raw">The raw wire value.</param>
        /// <returns>The reconstructed id.</returns>
        public static ObjectId FromRaw(ulong raw) => new ObjectId(EntityId.FromULong(raw));
        /// <summary>Value-equality on the underlying id.</summary>
        /// <param name="other">The id to compare against.</param>
        /// <returns>True if the underlying ids are equal.</returns>
        public bool Equals(ObjectId other) => m_Value.Equals(other.m_Value);
        /// <summary>Hash of the underlying id.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode() => m_Value.GetHashCode();
#else
        readonly int m_Value;
        /// <summary>Wrap a raw instance id.</summary>
        /// <param name="value">The instance id to wrap.</param>
        public ObjectId(int value) { m_Value = value; }
        /// <summary>Unwrap to the underlying instance id.</summary>
        /// <param name="id">The id to unwrap.</param>
        /// <returns>The underlying instance id.</returns>
        public static implicit operator int(ObjectId id) => id.m_Value;
        /// <summary>Wrap a raw instance id.</summary>
        /// <param name="value">The instance id to wrap.</param>
        /// <returns>The wrapped id.</returns>
        public static implicit operator ObjectId(int value) => new ObjectId(value);
        /// <summary>Raw id — the canonical wire / serialization form.</summary>
        public long RawValue => m_Value;
        /// <summary>Reconstruct an <see cref="ObjectId"/> from its raw wire value.</summary>
        /// <param name="raw">The raw wire value.</param>
        /// <returns>The reconstructed id.</returns>
        public static ObjectId FromRaw(long raw) => new ObjectId((int)raw);
        /// <summary>Value-equality on the underlying id.</summary>
        /// <param name="other">The id to compare against.</param>
        /// <returns>True if the underlying ids are equal.</returns>
        public bool Equals(ObjectId other) => m_Value == other.m_Value;
        /// <summary>Hash of the underlying id.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode() => m_Value;
#endif
        /// <summary>Value-equality on the underlying id.</summary>
        /// <param name="obj">The object to compare against.</param>
        /// <returns>True if <paramref name="obj"/> is an equal <see cref="ObjectId"/>.</returns>
        public override bool Equals(object obj) => obj is ObjectId other && Equals(other);
        /// <summary>The canonical numeric wire form (see <see cref="RawValue"/>).</summary>
        /// <returns>The numeric string form.</returns>
        public override string ToString() => RawValue.ToString();

        /// <summary>Parse the canonical numeric form produced by <see cref="ToString"/>.</summary>
        /// <param name="s">The numeric string to parse.</param>
        /// <returns>The parsed id.</returns>
        public static ObjectId Parse(string s)
        {
#if UNITY_6000_4_OR_NEWER
            return FromRaw(ulong.Parse(s));
#else
            return FromRaw(long.Parse(s));
#endif
        }

        /// <summary>Try to parse the canonical numeric form produced by <see cref="ToString"/>.</summary>
        /// <param name="s">The numeric string to parse.</param>
        /// <param name="id">The parsed id, if successful.</param>
        /// <returns>True if <paramref name="s"/> was a valid numeric form.</returns>
        public static bool TryParse(string s, out ObjectId id)
        {
#if UNITY_6000_4_OR_NEWER
            if (ulong.TryParse(s, out var raw)) { id = FromRaw(raw); return true; }
#else
            if (long.TryParse(s, out var raw)) { id = FromRaw(raw); return true; }
#endif
            id = default;
            return false;
        }
    }

    /// <summary>JSON converter: an <see cref="ObjectId"/> is a single integer on the wire.</summary>
    sealed class ObjectIdConverter : JsonConverter<ObjectId>
    {
        /// <summary>Write an <see cref="ObjectId"/> as its raw numeric wire value.</summary>
        /// <param name="writer">The JSON writer.</param>
        /// <param name="value">The id to write.</param>
        /// <param name="serializer">The active serializer.</param>
        public override void WriteJson(JsonWriter writer, ObjectId value, JsonSerializer serializer)
        {
            writer.WriteValue(value.RawValue);
        }

        /// <summary>Read an <see cref="ObjectId"/> from either its numeric or string wire form.</summary>
        /// <param name="reader">The JSON reader.</param>
        /// <param name="objectType">The declared property type.</param>
        /// <param name="existingValue">The existing value, if any.</param>
        /// <param name="hasExistingValue">Whether <paramref name="existingValue"/> is valid.</param>
        /// <param name="serializer">The active serializer.</param>
        /// <returns>The parsed id, or <c>default</c> for an unrecognized token type.</returns>
        public override ObjectId ReadJson(JsonReader reader, System.Type objectType, ObjectId existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);
            switch (token.Type)
            {
                case JTokenType.Integer:
#if UNITY_6000_4_OR_NEWER
                    return ObjectId.FromRaw(token.Value<ulong>());
#else
                    return ObjectId.FromRaw(token.Value<long>());
#endif
                case JTokenType.String:
                    return ObjectId.Parse((string)token);
                default:
                    return default;
            }
        }
    }

    /// <summary>Small cross-Unity-version compatibility shims used across the package.</summary>
    public static class PipelineUtils
    {
        /// <summary>The on-disk path Unity loaded an assembly from.</summary>
        /// <param name="a">The assembly to query.</param>
        /// <returns>The assembly's loaded path.</returns>
        public static string GetLoadedAssemblyPath(System.Reflection.Assembly a)
        {
#if UNITY_6000_5_OR_NEWER
            return a.GetLoadedAssemblyPath();
#else
            return a.Location;
#endif
        }

        /// <summary>Load a compiled assembly from raw bytes (used by the method-reload/eval compilers).</summary>
        /// <param name="bytes">The assembly image.</param>
        /// <param name="pdb">The matching portable PDB, if any.</param>
        /// <returns>The loaded assembly.</returns>
        public static System.Reflection.Assembly LoadFromBytes(byte[] bytes, byte[] pdb = null)
        {
#if UNITY_6000_5_OR_NEWER
            return UnityEngine.Assemblies.CurrentAssemblies.LoadFromBytes(bytes, pdb);
#else
            return System.Reflection.Assembly.Load(bytes, pdb);
#endif
        }

        /// <summary>Find every loaded object of type <typeparamref name="T"/>, including inactive ones.</summary>
        /// <typeparam name="T">The object type to search for.</typeparam>
        /// <returns>All matching objects.</returns>
        public static T[] FindObjectsByType<T>() where T : Object
        {
#if UNITY_6000_4_OR_NEWER
            return GameObject.FindObjectsByType<T>();
#else
            return GameObject.FindObjectsByType<T>(FindObjectsSortMode.None) ;
#endif
        }

        /// <summary>All assemblies currently loaded in the domain.</summary>
        /// <returns>The loaded assemblies.</returns>
        public static IReadOnlyList<System.Reflection.Assembly> GetLoadedAssemblies()
        {
#if UNITY_6000_5_OR_NEWER
            return UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies();
#else
            return System.AppDomain.CurrentDomain.GetAssemblies();
#endif
        }

        /// <summary>The object's id as a version-stable <see cref="ObjectId"/> (EntityId on 6000.4+, int below).</summary>
        /// <param name="obj">The object to get an id for.</param>
        /// <returns>The object's version-stable id.</returns>
        public static ObjectId GetObjectId(Object obj)
        {
#if UNITY_6000_4_OR_NEWER
            return new ObjectId(obj.GetEntityId());
#else
            return new ObjectId(obj.GetInstanceID());
#endif
        }

#if UNITY_EDITOR
        /// <summary>Resolve a loaded/scene object from its <see cref="ObjectId"/>, or null if not found.</summary>
        public static Object IdToObject(ObjectId id)
        {
#if UNITY_6000_4_OR_NEWER
            return UnityEditor.EditorUtility.EntityIdToObject(id);
#elif UNITY_6000_3_OR_NEWER
            // EntityIdToObject is the non-obsolete resolver from 6000.3, but the ulong-backed ObjectId
            // (GetRawData/FromULong) doesn't exist until 6000.4 — so ObjectId is still int-backed here and
            // we route the int through the (non-obsolete on 6.3) int->EntityId conversion.
            return UnityEditor.EditorUtility.EntityIdToObject((int)id);
#else
            return UnityEditor.EditorUtility.InstanceIDToObject((int)id);
#endif
        }
#endif
    }
}

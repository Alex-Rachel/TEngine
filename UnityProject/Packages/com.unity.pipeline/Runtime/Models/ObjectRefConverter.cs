using System;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.Pipeline.Models
{
    /// <summary>
    /// JSON converter for <see cref="ObjectRef"/> that accepts BOTH a structured object
    /// (all six fields via their JSON property names) AND a single canonical string handle.
    ///
    /// The MCP tool schemas intentionally expose object-reference parameters as plain strings, so an
    /// agent typically passes e.g. <c>"target": "/Player"</c> or <c>"target": "GlobalObjectId_V1-..."</c>.
    /// Without this converter, Newtonsoft's <c>ToObject&lt;ObjectRef&gt;()</c> (used by
    /// <c>BasePipelineServer.ExtractCommandParameters</c>) gets a JValue(string), returns null, and the
    /// command fails Required-parameter validation. This converter coerces the string into the matching
    /// field, mirroring the priority order of <c>ObjectResolver.TryResolve</c> and <c>ObjectRef.ToString()</c>.
    ///
    /// Registered on the class itself via <c>[JsonConverter(typeof(ObjectRefConverter))]</c> so it applies
    /// everywhere an <see cref="ObjectRef"/> is (de)serialized — no server or command changes required.
    /// </summary>
    class ObjectRefConverter : JsonConverter<ObjectRef>
    {
        private static readonly Regex s_GuidWithFileId = new Regex(@"^guid:([0-9a-fA-F]+):([0-9]+)$", RegexOptions.Compiled);
        private static readonly Regex s_GuidOnly = new Regex(@"^guid:([0-9a-fA-F]+)$", RegexOptions.Compiled);
        private static readonly Regex s_InstanceId = new Regex(@"^instanceId:(-?[0-9]+)$", RegexOptions.Compiled);
        private static readonly Regex s_PlainInt = new Regex(@"^-?[0-9]+$", RegexOptions.Compiled);
        private static readonly Regex s_Hex32 = new Regex(@"^[0-9a-fA-F]{32}$", RegexOptions.Compiled);

        /// <summary>Read an <see cref="ObjectRef"/> from a structured object, a canonical string, or a bare instance id.</summary>
        /// <param name="reader">The JSON reader.</param>
        /// <param name="objectType">The declared property type.</param>
        /// <param name="existingValue">The existing value, if any.</param>
        /// <param name="hasExistingValue">Whether <paramref name="existingValue"/> is valid.</param>
        /// <param name="serializer">The active serializer.</param>
        /// <returns>The parsed reference, or null for a JSON null.</returns>
        public override ObjectRef ReadJson(JsonReader reader, Type objectType, ObjectRef existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);
            switch (token.Type)
            {
                case JTokenType.Null:
                    return null;
                case JTokenType.String:
                    return FromString((string)token);
                case JTokenType.Integer:
                    // A bare JSON number is treated as an instance id (e.g. "target": 48184).
                    return new ObjectRef { InstanceId = token.ToObject<ObjectId>() };
                case JTokenType.Object:
                    return FromObject((JObject)token);
                default:
                    return null;
            }
        }

        /// <summary>Write an <see cref="ObjectRef"/> as a structured JSON object.</summary>
        /// <param name="writer">The JSON writer.</param>
        /// <param name="value">The reference to write, or null.</param>
        /// <param name="serializer">The active serializer.</param>
        public override void WriteJson(JsonWriter writer, ObjectRef value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            // Write a standard JSON object (matching the [JsonProperty] names on ObjectRef). Done
            // manually rather than via serializer.Serialize to avoid re-entering this converter.
            writer.WriteStartObject();
            writer.WritePropertyName("globalId");
            writer.WriteValue(value.GlobalId);
            writer.WritePropertyName("path");
            writer.WriteValue(value.Path);
            writer.WritePropertyName("guid");
            writer.WriteValue(value.Guid);
            writer.WritePropertyName("fileId");
            if (value.FileId.HasValue) writer.WriteValue(value.FileId.Value); else writer.WriteNull();
            writer.WritePropertyName("instanceId");
            // Route the id through ObjectIdConverter so it serializes as its raw numeric form
            // (the 64-bit EntityId on 6000.4+, the int below). Not this converter — no recursion.
            if (value.InstanceId.HasValue) serializer.Serialize(writer, value.InstanceId.Value); else writer.WriteNull();
            writer.WritePropertyName("hierarchyPath");
            writer.WriteValue(value.HierarchyPath);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Map a structured JSON object to an ObjectRef by reading each field directly (rather than
        /// <c>ToObject&lt;ObjectRef&gt;()</c>, which would recurse back into this converter).
        ///
        /// Keys are matched case-INSENSITIVELY: agents commonly vary the casing (e.g. "instanceID" for
        /// instanceId, "fileID" for fileId), and because this custom converter bypasses Newtonsoft's
        /// default case-insensitive property binding, an exact-case lookup would otherwise drop the
        /// field and yield an empty (silently unresolved) handle.
        /// </summary>
        private static ObjectRef FromObject(JObject jo)
        {
            return new ObjectRef
            {
                GlobalId = Str(jo, "globalId"),
                Path = Str(jo, "path"),
                Guid = Str(jo, "guid"),
                FileId = Long(jo, "fileId"),
                InstanceId = ObjId(jo, "instanceId"),
                HierarchyPath = Str(jo, "hierarchyPath"),
            };
        }

        /// <summary>Find a property value by name, ignoring case (Newtonsoft's JObject lookup is case-sensitive).</summary>
        private static JToken Get(JObject jo, string key)
        {
            foreach (var p in jo.Properties())
            {
                if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))
                    return p.Value;
            }

            return null;
        }

        private static string Str(JObject jo, string key)
        {
            var t = Get(jo, key);
            return t == null || t.Type == JTokenType.Null ? null : t.Value<string>();
        }

        private static ObjectId? ObjId(JObject jo, string key)
        {
            var t = Get(jo, key);
            return t == null || t.Type == JTokenType.Null ? (ObjectId?)null : t.ToObject<ObjectId>();
        }

        private static long? Long(JObject jo, string key)
        {
            var t = Get(jo, key);
            return t == null || t.Type == JTokenType.Null ? (long?)null : t.Value<long?>();
        }

        /// <summary>
        /// Parse a single canonical string handle into the matching field. Priority order matches
        /// <c>ObjectResolver.TryResolve</c> / <c>ObjectRef.ToString()</c>; a bare name falls back to
        /// the hierarchy path (the most common agent input).
        /// </summary>
        internal static ObjectRef FromString(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return null;

            s = s.Trim();

            if (s.StartsWith("GlobalObjectId_V1", StringComparison.Ordinal))
                return new ObjectRef { GlobalId = s };

            // "Assets/..." and "Packages/..." (and the bare roots) are asset paths that
            // AssetDatabase.LoadMainAssetAtPath can resolve; everything else falls through to the
            // hierarchy-path fallback at the end.
            if (s.StartsWith("Assets/", StringComparison.Ordinal)
                || s.StartsWith("Packages/", StringComparison.Ordinal)
                || s == "Assets" || s == "Packages")
                return new ObjectRef { Path = s };

            var guidFile = s_GuidWithFileId.Match(s);
            if (guidFile.Success && long.TryParse(guidFile.Groups[2].Value, out var fileId))
                return new ObjectRef { Guid = guidFile.Groups[1].Value, FileId = fileId };

            var guidOnly = s_GuidOnly.Match(s);
            if (guidOnly.Success)
                return new ObjectRef { Guid = guidOnly.Groups[1].Value };

            var inst = s_InstanceId.Match(s);
            if (inst.Success && ObjectId.TryParse(inst.Groups[1].Value, out var prefixedId))
                return new ObjectRef { InstanceId = prefixedId };

            // A plain integer is an instance id. On 6000.0-6000.3 negative ids are valid (unsaved scene
            // objects); on 6000.4+ the id is the unsigned 64-bit EntityId, so a leading '-' won't parse
            // and falls through to the hierarchy-path handling below.
            if (s_PlainInt.IsMatch(s) && ObjectId.TryParse(s, out var plainId))
                return new ObjectRef { InstanceId = plainId };

            // Exactly 32 hex chars with no other prefix is a bare asset GUID.
            if (s_Hex32.IsMatch(s))
                return new ObjectRef { Guid = s };

            // A relative string with a file extension (e.g. "Materials/Floor.mat", "Enemy.prefab") is
            // most likely an authoring-root-relative asset path — route it to Path so the resolver can
            // normalize it under the authoring root. A leading "/" always means a scene hierarchy path,
            // and an extension-less string ("Player", "Root/Child") stays one. Dotted GameObject names
            // ("Cube.001") land here too, but the resolver falls back to a hierarchy lookup for those.
            if (!s.StartsWith("/", StringComparison.Ordinal) && Path.HasExtension(s))
                return new ObjectRef { Path = s };

            // Leading-"/" canonical paths and bare names resolve as a scene hierarchy path.
            return new ObjectRef { HierarchyPath = s };
        }
    }
}

using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// Deserializes <see cref="IStructuredCommandInput"/> values by the names <c>GET /api/commands</c>
    /// advertises for them. <see cref="JsonSchemaGenerator"/> names a member after its <c>[CliArg]</c>,
    /// but Newtonsoft's default contract binds by member name, so a key like <c>output-path</c> would
    /// never reach <c>OutputPath</c>. This serializer binds by the <c>[CliArg]</c> name instead, so the
    /// schema and the binding cannot disagree.
    /// </summary>
    static class StructuredInputSerializer
    {
        internal static readonly JsonSerializer Instance = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new CliArgContractResolver()
        });

        /// <summary>True when <paramref name="type"/>, or the element type of an array or list of it, is a structured input.</summary>
        internal static bool Applies(Type type)
        {
            if (JsonSchemaGenerator.TryGetEnumerableElementType(type, out var elementType))
                type = elementType;
            return typeof(IStructuredCommandInput).IsAssignableFrom(type);
        }

        /// <summary>
        /// Throws when <paramref name="token"/>, read as <paramref name="type"/>, carries a key the
        /// structured input does not declare, at any depth. Newtonsoft drops such a key, so a misspelled
        /// or renamed one would leave its member at the default: <c>{"fixedDeltaTime": 0.01}</c> sent to
        /// <c>settings.time set</c> changed nothing. Keys match exactly, as top-level parameters do.
        /// </summary>
        internal static void RejectUnknownKeys(JToken token, Type type) => RejectUnknownKeys(token, type, null);

        static void RejectUnknownKeys(JToken token, Type type, string path)
        {
            if (JsonSchemaGenerator.TryGetEnumerableElementType(type, out var elementType))
            {
                if (token is JArray array)
                    for (var i = 0; i < array.Count; i++)
                        RejectUnknownKeys(array[i], elementType, $"{path}[{i}]");
                return;
            }

            if (!typeof(IStructuredCommandInput).IsAssignableFrom(type) || !(token is JObject obj))
                return;

            var contract = (JsonObjectContract)Instance.ContractResolver.ResolveContract(type);
            var members = contract.Properties.Where(p => !p.Ignored && p.Writable).ToList();
            foreach (var key in obj.Properties())
            {
                var keyPath = path == null ? key.Name : $"{path}.{key.Name}";
                var member = members.FirstOrDefault(p => string.Equals(p.PropertyName, key.Name, StringComparison.Ordinal));
                if (member == null)
                {
                    var suggestion = CommandLineBinder.SuggestClosestName(key.Name, members.Select(p => p.PropertyName));
                    throw new ArgumentException(suggestion != null
                        ? $"unknown key '{keyPath}'. Did you mean '{suggestion}'?"
                        : $"unknown key '{keyPath}'. Valid keys: {string.Join(", ", members.Select(p => p.PropertyName))}.");
                }

                RejectUnknownKeys(key.Value, member.PropertyType, keyPath);
            }
        }

        class CliArgContractResolver : DefaultContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var property = base.CreateProperty(member, memberSerialization);
                if (typeof(IStructuredCommandInput).IsAssignableFrom(member.DeclaringType))
                {
                    var cliArg = member.GetCustomAttribute<CliArgAttribute>();
                    if (cliArg != null)
                        property.PropertyName = cliArg.Name;
                }
                return property;
            }
        }
    }
}

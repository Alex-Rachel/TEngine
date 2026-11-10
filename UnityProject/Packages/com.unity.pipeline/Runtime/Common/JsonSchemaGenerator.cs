using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.Pipeline.Commands
{
    /// <summary>
    /// Generates JSON Schema for CLI commands to enable parameter validation and help generation.
    /// Produces standard JSON Schema format that CLI tools can use for validation and auto-completion.
    ///
    /// Primitive, enum, and array parameters map directly. Parameters whose type implements
    /// <see cref="IStructuredCommandInput"/> are emitted as nested object schemas (recursing into
    /// structured members and arrays/lists of them) rather than collapsing to <c>string</c>, so
    /// agents calling the package — directly or as MCP tools whose schemas come from
    /// <c>GET /api/commands</c> — can construct structured arguments reliably.
    /// </summary>
    static class JsonSchemaGenerator
    {
        /// <summary>
        /// Generate JSON Schema for a command.
        /// Returns standard JSON Schema format for CLI validation.
        /// </summary>
        /// <param name="command">The command to generate a schema for.</param>
        /// <returns>The generated JSON Schema document.</returns>
        public static string GenerateCommandSchema(CommandInfo command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            var schema = new JObject
            {
                ["$schema"] = "http://json-schema.org/draft-07/schema#",
                ["type"] = "object",
                ["title"] = command.FullName,
                ["description"] = command.Description,
                ["properties"] = GenerateParameterProperties(command.Parameters),
                ["required"] = new JArray(command.Parameters.Where(p => p.Required).Select(p => p.Name)),
                ["additionalProperties"] = false
            };

            // Add command metadata for CLI tools
            schema["x-command-metadata"] = new JObject
            {
                ["mainThreadRequired"] = command.MainThreadRequired,
                ["methodName"] = $"{command.Method.DeclaringType?.FullName}.{command.Method.Name}"
            };

            return schema.ToString(Formatting.Indented);
        }

        /// <summary>
        /// Generate properties section of JSON Schema from command parameters.
        /// </summary>
        private static JObject GenerateParameterProperties(IEnumerable<CommandParameterInfo> parameters)
        {
            var properties = new JObject();

            foreach (var parameter in parameters)
            {
                properties[parameter.Name] = GenerateParameterSchema(parameter);
            }

            return properties;
        }

        /// <summary>
        /// Generate JSON Schema for a single top-level command parameter.
        /// </summary>
        private static JObject GenerateParameterSchema(CommandParameterInfo parameter)
        {
            var schema = GenerateTypeSchema(parameter.ParameterType, new HashSet<Type>());
            schema["description"] = parameter.Description;

            // Add default value if present (skip for objects/arrays whose default is typically null)
            if (parameter.DefaultValue != null)
            {
                try
                {
                    schema["default"] = JToken.FromObject(parameter.DefaultValue);
                }
                catch
                {
                    // Non-serializable default; omit rather than fail schema generation.
                }
            }

            return schema;
        }

        /// <summary>
        /// Build the schema fragment for a CLR type: scalar/enum, array/list, or — for
        /// <see cref="IStructuredCommandInput"/> types — a nested object schema. <paramref name="visited"/>
        /// tracks structured types currently being expanded so self-referential graphs don't recurse
        /// forever.
        /// </summary>
        private static JObject GenerateTypeSchema(Type type, HashSet<Type> visited)
        {
            // Unwrap Nullable<T> so e.g. int? schemas as "integer".
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
                type = underlying;

            if (typeof(IStructuredCommandInput).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
                return GenerateObjectSchema(type, visited);

            if (TryGetEnumerableElementType(type, out var elementType))
            {
                return new JObject
                {
                    ["type"] = "array",
                    ["items"] = GenerateTypeSchema(elementType, visited)
                };
            }

            var schema = new JObject();
            var (jsonType, format) = MapTypeToJsonSchema(type);
            schema["type"] = jsonType;

            if (!string.IsNullOrEmpty(format))
                schema["format"] = format;

            if (type.IsEnum)
                schema["enum"] = new JArray(Enum.GetNames(type));

            return schema;
        }

        /// <summary>
        /// Emit a nested object schema for an <see cref="IStructuredCommandInput"/> type by reflecting
        /// its public, writable fields and properties.
        /// </summary>
        private static JObject GenerateObjectSchema(Type type, HashSet<Type> visited)
        {
            var schema = new JObject { ["type"] = "object" };

            // Cycle guard: if we're already expanding this type higher in the stack, stop recursing
            // and emit an open object rather than looping.
            if (!visited.Add(type))
            {
                schema["additionalProperties"] = true;
                return schema;
            }

            try
            {
                var properties = new JObject();
                var required = new JArray();

                foreach (var member in GetSchemaMembers(type))
                {
                    var memberSchema = GenerateTypeSchema(member.Type, visited);
                    if (!string.IsNullOrEmpty(member.Description))
                        memberSchema["description"] = member.Description;

                    properties[member.Name] = memberSchema;
                    if (member.Required)
                        required.Add(member.Name);
                }

                schema["properties"] = properties;
                if (required.Count > 0)
                    schema["required"] = required;
                schema["additionalProperties"] = false;
            }
            finally
            {
                visited.Remove(type);
            }

            return schema;
        }

        /// <summary>
        /// Enumerate the schema-bearing members of a structured-input type: public instance fields and
        /// read/write properties. Honors <c>[JsonIgnore]</c>, and reads name/description/required from
        /// <c>[CliArg]</c> (falling back to a Newtonsoft <c>[JsonProperty]</c> name, then the member name).
        /// </summary>
        private static IEnumerable<SchemaMember> GetSchemaMembers(Type type)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

            foreach (var field in type.GetFields(flags))
            {
                if (field.IsInitOnly || field.IsLiteral)
                    continue;
                if (field.GetCustomAttribute<JsonIgnoreAttribute>() != null)
                    continue;

                yield return ToSchemaMember(field, field.FieldType);
            }

            foreach (var property in type.GetProperties(flags))
            {
                if (!property.CanRead || !property.CanWrite)
                    continue;
                if (property.GetIndexParameters().Length > 0)
                    continue;
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() != null)
                    continue;

                yield return ToSchemaMember(property, property.PropertyType);
            }
        }

        private static SchemaMember ToSchemaMember(MemberInfo member, Type memberType)
        {
            var cliArg = member.GetCustomAttribute<CliArgAttribute>();
            var jsonProperty = member.GetCustomAttribute<JsonPropertyAttribute>();

            return new SchemaMember
            {
                Name = cliArg?.Name ?? jsonProperty?.PropertyName ?? member.Name,
                Type = memberType,
                Description = cliArg?.Description,
                Required = cliArg?.Required ?? false
            };
        }

        /// <summary>
        /// Detect a JSON-array-shaped type (arrays and common generic collections) and yield its
        /// element type. <c>string</c> is intentionally excluded (it is <see cref="IEnumerable{T}"/>
        /// of char but maps to a JSON string).
        /// </summary>
        internal static bool TryGetEnumerableElementType(Type type, out Type elementType)
        {
            elementType = null;

            if (type == typeof(string))
                return false;

            if (type.IsArray)
            {
                elementType = type.GetElementType();
                return elementType != null;
            }

            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                if (definition == typeof(List<>) || definition == typeof(IList<>) ||
                    definition == typeof(IEnumerable<>) || definition == typeof(ICollection<>) ||
                    definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>))
                {
                    elementType = type.GetGenericArguments()[0];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Map a scalar/enum C# type to JSON Schema type and format. Object and array shapes are
        /// handled by <see cref="GenerateTypeSchema"/> before this is reached; anything else falls
        /// back to <c>string</c>.
        /// </summary>
        private static (string type, string format) MapTypeToJsonSchema(Type csharpType)
        {
            // Handle nullable types
            if (Nullable.GetUnderlyingType(csharpType) != null)
            {
                csharpType = Nullable.GetUnderlyingType(csharpType);
            }

            // String types
            if (csharpType == typeof(string) || csharpType == typeof(char))
            {
                return ("string", null);
            }

            // Integer types
            if (csharpType == typeof(int) || csharpType == typeof(long) ||
                csharpType == typeof(short) || csharpType == typeof(byte) ||
                csharpType == typeof(uint) || csharpType == typeof(ulong) ||
                csharpType == typeof(ushort) || csharpType == typeof(sbyte))
            {
                return ("integer", null);
            }

            // Number types
            if (csharpType == typeof(float) || csharpType == typeof(double) || csharpType == typeof(decimal))
            {
                return ("number", null);
            }

            // Boolean type
            if (csharpType == typeof(bool))
            {
                return ("boolean", null);
            }

            // Enum types - represent as string with enum constraint (added by the caller)
            if (csharpType.IsEnum)
            {
                return ("string", null);
            }

            // DateTime types
            if (csharpType == typeof(DateTime) || csharpType == typeof(DateTimeOffset))
            {
                return ("string", "date-time");
            }

            // Guid type
            if (csharpType == typeof(Guid))
            {
                return ("string", "uuid");
            }

            // Default to string for unknown types
            return ("string", null);
        }

        /// <summary>A reflected member of a structured-input type, flattened for schema emission.</summary>
        private struct SchemaMember
        {
            public string Name;
            public Type Type;
            public string Description;
            public bool Required;
        }
    }
}

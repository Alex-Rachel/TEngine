using System.Collections.Generic;

namespace Unity.Pipeline
{
    /// <summary>
    /// One way for a client to connect to this instance: a connection type (e.g. <c>http</c>) and the
    /// fields that type needs (e.g. <c>port</c>).
    /// </summary>
    /// <remarks>
    /// Immutable. Registries identify endpoints by reference: unregister the same instance you
    /// registered.
    /// </remarks>
    class PipelineServiceEndpoint
    {
        readonly string m_Type;
        readonly Dictionary<string, string> m_Fields;

        /// <param name="type">The connection type clients match on (case-sensitive).</param>
        /// <param name="fields">
        /// The type-specific fields, copied. They are published beside the type, so no key may be
        /// <c>type</c>.
        /// </param>
        /// <exception cref="System.ArgumentNullException"><paramref name="type"/> or <paramref name="fields"/> is null.</exception>
        /// <exception cref="System.ArgumentException">
        /// <paramref name="type"/> is empty or whitespace, or <paramref name="fields"/> has a duplicate
        /// key, a null key or value, or a key named <c>type</c>.
        /// </exception>
        public PipelineServiceEndpoint(string type, IEnumerable<KeyValuePair<string, string>> fields)
        {
            if (type == null)
                throw new System.ArgumentNullException(nameof(type));
            if (fields == null)
                throw new System.ArgumentNullException(nameof(fields));
            if (string.IsNullOrWhiteSpace(type))
                throw new System.ArgumentException("Connection type must not be empty or whitespace.", nameof(type));

            m_Type = type;
            m_Fields = new Dictionary<string, string>(System.StringComparer.Ordinal);
            foreach (var field in fields)
            {
                if (field.Key == null || field.Value == null)
                    throw new System.ArgumentException("Endpoint fields must not have a null key or value.", nameof(fields));
                if (field.Key == "type")
                    throw new System.ArgumentException("An endpoint field must not be named 'type'.", nameof(fields));
                m_Fields.Add(field.Key, field.Value);
            }
        }

        /// <returns>The connection type.</returns>
        public string GetConnectionType() => m_Type;

        /// <param name="key">The field name (case-sensitive).</param>
        /// <returns>The field value.</returns>
        /// <exception cref="KeyNotFoundException">The endpoint has no field named <paramref name="key"/>.</exception>
        public string GetFieldValue(string key) => m_Fields[key];

        /// <returns>All type-specific fields, unordered.</returns>
        public IReadOnlyDictionary<string, string> GetFields() => m_Fields;
    }
}

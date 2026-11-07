using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.Pipeline
{
    /// <summary>
    /// Reads an optional caller-context field of <see cref="CommandExecutionRequest"/> without
    /// ever failing the request. These fields only feed analytics, so a value of the wrong type,
    /// a fractional id or an id out of range reads as absent instead of rejecting the command.
    /// </summary>
    sealed class LenientAnalyticDataConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(long?) || objectType == typeof(string);

        public override bool CanWrite => false;

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);
            if (objectType == typeof(string))
                return token.Type == JTokenType.String ? (string)token : null;

            return token is JValue { Type: JTokenType.Integer, Value: long id } ? id : (long?)null;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotSupportedException();
        }
    }
}

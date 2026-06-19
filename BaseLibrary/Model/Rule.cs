using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xvirus.Model
{
    [JsonConverter(typeof(RuleTypeJsonConverter))]
    public enum RuleType
    {
        Allow,
        Block
    }

    public class Rule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Path { get; set; } = string.Empty;
        public RuleType? Type { get; set; }
    }

    /// <summary>
    /// Serializes <see cref="RuleType"/> as the lowercase strings the UI expects
    /// (<c>allow</c> / <c>block</c>). Reads legacy numeric values for back-compat.
    /// Reflection-free so it works under Native AOT / source generation.
    /// </summary>
    public sealed class RuleTypeJsonConverter : JsonConverter<RuleType>
    {
        public override RuleType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
                return reader.GetInt32() == (int)RuleType.Block ? RuleType.Block : RuleType.Allow;

            var s = reader.GetString();
            return string.Equals(s, "block", StringComparison.OrdinalIgnoreCase)
                ? RuleType.Block
                : RuleType.Allow;
        }

        public override void Write(Utf8JsonWriter writer, RuleType value, JsonSerializerOptions options)
            => writer.WriteStringValue(value == RuleType.Block ? "block" : "allow");
    }
}

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

    [JsonConverter(typeof(RuleKindJsonConverter))]
    public enum RuleKind
    {
        Program,
        Ip,
        Port,
        Domain
    }

    public class Rule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public RuleKind Kind { get; set; } = RuleKind.Program;
        public string Path { get; set; } = string.Empty;
        public RuleType? Type { get; set; }
        public string Protocol { get; set; } = "any";   // any, tcp, udp
        public string Direction { get; set; } = "both"; // in, out, both
        public string Profile { get; set; } = "any";    // any, private, domain, public
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

    /// <summary>
    /// Serializes <see cref="RuleKind"/> as lowercase strings (<c>program</c>, <c>ip</c>,
    /// <c>port</c>, <c>domain</c>). Reflection-free for Native AOT compatibility.
    /// </summary>
    public sealed class RuleKindJsonConverter : JsonConverter<RuleKind>
    {
        public override RuleKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetInt32() switch
                {
                    (int)RuleKind.Ip => RuleKind.Ip,
                    (int)RuleKind.Port => RuleKind.Port,
                    (int)RuleKind.Domain => RuleKind.Domain,
                    _ => RuleKind.Program
                };
            }

            var s = reader.GetString()?.Trim().ToLowerInvariant();
            return s switch
            {
                "ip" => RuleKind.Ip,
                "port" => RuleKind.Port,
                "domain" => RuleKind.Domain,
                _ => RuleKind.Program
            };
        }

        public override void Write(Utf8JsonWriter writer, RuleKind value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value switch
            {
                RuleKind.Ip => "ip",
                RuleKind.Port => "port",
                RuleKind.Domain => "domain",
                _ => "program"
            });
        }
    }
}

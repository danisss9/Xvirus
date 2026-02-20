using System.Text.Json.Serialization;
using Xvirus.Model;

namespace BaseLibrary.Serializers
{
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
    [JsonSerializable(typeof(ScanResult))]
    [JsonSerializable(typeof(SettingsDTO))]
    [JsonSerializable(typeof(AppSettingsDTO))]
    [JsonSerializable(typeof(DatabaseDTO))]
    public partial class SourceGenerationContext : JsonSerializerContext { }

    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default, WriteIndented = true)]
    [JsonSerializable(typeof(SettingsDTO))]
    [JsonSerializable(typeof(AppSettingsDTO))]
    public partial class SourceGenerationContextIndent : JsonSerializerContext { }

    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(UpdateInfo))]
    public partial class SourceGenerationContextCamelCase : JsonSerializerContext { }
}

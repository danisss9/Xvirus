using System.Text.Json.Serialization;
using Xvirus.Model;

namespace BaseLibrary.Serializers
{
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
    [JsonSerializable(typeof(ScanResult))]
    [JsonSerializable(typeof(UpdateInfo))]
    [JsonSerializable(typeof(SettingsDTO))]
    [JsonSerializable(typeof(DatabaseDTO))]
    public partial class SourceGenerationContext : JsonSerializerContext { }
}

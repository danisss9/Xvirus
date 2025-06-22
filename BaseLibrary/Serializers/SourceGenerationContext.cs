using System.Text.Json.Serialization;
using Xvirus.Model;

namespace BaseLibrary.Serializers
{
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization)]
    [JsonSerializable(typeof(SettingsDTO))]
    [JsonSerializable(typeof(ScanResult))]
    [JsonSerializable(typeof(UpdateInfo))]
    [JsonSerializable(typeof(SettingsDTO))]
    public partial class SourceGenerationContext : JsonSerializerContext { }
}

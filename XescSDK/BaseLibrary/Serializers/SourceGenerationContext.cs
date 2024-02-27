using System.Text.Json.Serialization;
using XescSDK.Model;

namespace BaseLibrary.Serializers
{
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization)]
    [JsonSerializable(typeof(SettingsDTO))]
    internal partial class SourceGenerationContext : JsonSerializerContext
    {
    }

    [JsonSerializable(typeof(ScanResult))]
    public partial class ScanResultGenerationContext : JsonSerializerContext
    {
    }

    [JsonSerializable(typeof(UpdateInfo))]
    public partial class UpdateInfoGenerationContext : JsonSerializerContext
    {
    }

    [JsonSerializable(typeof(SettingsDTO))]
    public partial class SettingsGenerationContext : JsonSerializerContext
    {
    }
}

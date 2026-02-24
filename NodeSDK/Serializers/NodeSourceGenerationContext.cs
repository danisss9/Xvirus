using System.Text.Json.Serialization;

namespace Xvirus.NodeSDK.Serializers;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(ScanResultNode))]
[JsonSerializable(typeof(ScanResultNode[]))]
internal partial class NodeSourceGenerationContext : JsonSerializerContext { }

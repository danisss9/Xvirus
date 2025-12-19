using System.Text.Json.Serialization;

namespace XvirusService;

[JsonSerializable(typeof(Todo[]))]
[JsonSerializable(typeof(ServerEvent))]
[JsonSerializable(typeof(ProcessSpawnEvent))]
internal partial class AppJsonSerializerContext : JsonSerializerContext { }

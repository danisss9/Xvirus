using System.Text.Json.Serialization;
using Xvirus.Model;

namespace XvirusService;

[JsonSerializable(typeof(Todo[]))]
[JsonSerializable(typeof(ServerEvent))]
[JsonSerializable(typeof(ProcessSpawnEvent))]
[JsonSerializable(typeof(ScanHistoryEntry))]
[JsonSerializable(typeof(List<ScanHistoryEntry>))]
[JsonSerializable(typeof(ScanRequest))]
[JsonSerializable(typeof(ScanResult))]
[JsonSerializable(typeof(SettingsDTO))]
internal partial class AppJsonSerializerContext : JsonSerializerContext { }

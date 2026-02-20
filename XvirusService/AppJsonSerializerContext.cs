using System.Text.Json.Serialization;
using Xvirus.Model;
using XvirusService.Model;

namespace XvirusService;

[JsonSerializable(typeof(ServerEvent))]
[JsonSerializable(typeof(ProcessSpawnEvent))]
[JsonSerializable(typeof(ScanHistoryEntry))]
[JsonSerializable(typeof(List<ScanHistoryEntry>))]
[JsonSerializable(typeof(ScanRequest))]
[JsonSerializable(typeof(ScanResult))]
[JsonSerializable(typeof(SettingsDTO))]
[JsonSerializable(typeof(AppSettingsDTO))]
[JsonSerializable(typeof(SettingsResponseDTO))]
[JsonSerializable(typeof(ErrorResponseDTO))]
internal partial class AppJsonSerializerContext : JsonSerializerContext { }

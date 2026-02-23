using System.Text.Json.Serialization;
using Xvirus.Model;
using XvirusService.Model;
using XvirusService.Services;

namespace XvirusService;

[JsonSerializable(typeof(ServerEvent))]
[JsonSerializable(typeof(ProcessSpawnEvent))]
[JsonSerializable(typeof(ScanRequest))]
[JsonSerializable(typeof(ScanResult))]
[JsonSerializable(typeof(SettingsDTO))]
[JsonSerializable(typeof(AppSettingsDTO))]
[JsonSerializable(typeof(SettingsResponseDTO))]
[JsonSerializable(typeof(ErrorResponseDTO))]
[JsonSerializable(typeof(HistoryEntry))]
[JsonSerializable(typeof(List<HistoryEntry>))]
[JsonSerializable(typeof(Rule))]
[JsonSerializable(typeof(List<Rule>))]
internal partial class AppJsonSerializerContext : JsonSerializerContext { }

using System.Text.Json.Serialization;
using Xvirus.Model;
using XvirusService.Model;

namespace XvirusService;

[JsonSerializable(typeof(ScanResult))]
[JsonSerializable(typeof(SettingsDTO))]
[JsonSerializable(typeof(AppSettingsDTO))]
[JsonSerializable(typeof(SettingsResponseDTO))]
[JsonSerializable(typeof(ErrorResponseDTO))]
[JsonSerializable(typeof(HistoryEntry))]
[JsonSerializable(typeof(List<HistoryEntry>))]
[JsonSerializable(typeof(Rule))]
[JsonSerializable(typeof(List<Rule>))]
[JsonSerializable(typeof(QuarantineEntry))]
[JsonSerializable(typeof(List<QuarantineEntry>))]
[JsonSerializable(typeof(UpdateStatusDTO))]
[JsonSerializable(typeof(ProcessSpawnEventArgs))]
[JsonSerializable(typeof(SseMessageDTO))]
internal partial class AppJsonSerializerContext : JsonSerializerContext { }

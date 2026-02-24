using Xvirus.Model;

namespace XvirusService.Model;

public class SettingsResponseDTO
{
    public SettingsDTO Settings { get; set; } = new SettingsDTO();
    public AppSettingsDTO AppSettings { get; set; } = new AppSettingsDTO();
}


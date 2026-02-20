namespace XvirusService.Model;

public class SettingsResponseDTO
{
    public Xvirus.Model.SettingsDTO Settings { get; set; } = new Xvirus.Model.SettingsDTO();
    public Xvirus.Model.AppSettingsDTO AppSettings { get; set; } = new Xvirus.Model.AppSettingsDTO();
}


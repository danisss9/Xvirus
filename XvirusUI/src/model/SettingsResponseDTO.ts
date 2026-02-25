import { SettingsDTO } from './SettingsDTO';
import { AppSettingsDTO } from './AppSettingsDTO';

export interface SettingsResponseDTO {
  settings: SettingsDTO;
  appSettings: AppSettingsDTO;
}

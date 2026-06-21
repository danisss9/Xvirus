export interface AppSettingsDTO {
  // UI
  language: string;
  darkMode: boolean;

  // general
  startWithWindows: boolean;
  enableContextMenu: boolean;
  enableLogs: boolean;

  // scan extras
  autoQuarantine: boolean;
  scheduledScan: 'off' | 'daily' | 'weekly' | 'monthly';

  // protection feature flags
  realTimeProtection: boolean;
  threatAction: 'auto' | 'ask';
  behaviorProtection: boolean;
  networkProtection: boolean;
  selfDefense: boolean;
  showNotifications: boolean;
}

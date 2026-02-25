export interface AppSettingsDTO {
  // UI
  language: string;
  darkMode: boolean;

  // general
  startWithWindows: boolean;
  enableContextMenu: boolean;
  passwordProtection: boolean;
  enableLogs: boolean;

  // scan extras
  onlyScanExecutables: boolean;
  autoQuarantine: boolean;
  scheduledScan: 'off' | 'daily' | 'weekly' | 'monthly';

  // protection feature flags
  realTimeProtection: boolean;
  threatAction: 'auto' | 'ask';
  behaviorProtection: boolean;
  cloudScan: boolean;
  networkProtection: boolean;
  selfDefense: boolean;
  showNotifications: boolean;
}

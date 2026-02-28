export interface InstallConfig {
  installPath: string;
  desktopShortcut: boolean;
  startMenuShortcut: boolean;
}

export interface InstallProgress {
  step: string;
  progress: number;
  done: boolean;
  success?: boolean;
  error?: string;
}

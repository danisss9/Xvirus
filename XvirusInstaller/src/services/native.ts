import { invoke } from '@tauri-apps/api/core';

// Thin wrappers around the Rust commands in src-tauri/src/main.rs.

/** The user's temp directory, without a trailing separator. */
export function getTempDir(): Promise<string> {
  return invoke<string>('temp_dir');
}

/** Write the resources zip embedded in the installer binary to `path`. */
export function writePayload(path: string): Promise<void> {
  return invoke('write_payload', { path });
}

export function writeTextFile(path: string, contents: string): Promise<void> {
  return invoke('write_text_file', { path, contents });
}

export function readTextFile(path: string): Promise<string> {
  return invoke<string>('read_text_file', { path });
}

/** Best-effort removal of temporary files. */
export function removeFiles(paths: string[]): Promise<void> {
  return invoke('remove_files', { paths });
}

/** Run a PowerShell script elevated (UAC). Resolves once the elevated process has started. */
export function runElevatedScript(scriptPath: string): Promise<void> {
  return invoke('run_elevated_script', { scriptPath });
}

/** Start `exePath` after `delaySecs`, detached so it keeps running after the installer exits. */
export function launchApp(exePath: string, delaySecs = 3): Promise<void> {
  return invoke('launch_app', { exePath, delaySecs });
}

export function exitApp(): Promise<void> {
  return invoke('exit_app');
}

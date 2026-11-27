// Hide the console window in release builds on Windows.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::{fs, process::Command};
use tauri::{AppHandle, Manager, RunEvent};

/// Zip of everything to install, produced by scripts/build-resources.mjs (see build.rs).
static PAYLOAD: &[u8] = include_bytes!(concat!(env!("OUT_DIR"), "/payload.zip"));

/// A PowerShell process that never shows a console window.
fn hidden_powershell() -> Command {
    let mut cmd = Command::new("powershell.exe");
    cmd.args([
        "-NoProfile",
        "-NonInteractive",
        "-ExecutionPolicy",
        "Bypass",
        "-WindowStyle",
        "Hidden",
        "-Command",
    ]);
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        cmd.creation_flags(CREATE_NO_WINDOW);
    }
    cmd
}

/// Quote a value as a PowerShell single-quoted string literal.
fn ps_quote(value: &str) -> String {
    format!("'{}'", value.replace('\'', "''"))
}

fn to_err(err: impl std::fmt::Display) -> String {
    err.to_string()
}

/// The user's temp directory, without a trailing separator.
#[tauri::command]
fn temp_dir() -> String {
    std::env::temp_dir()
        .to_string_lossy()
        .trim_end_matches(['\\', '/'])
        .to_string()
}

/// Write the embedded resources zip to `path`.
#[tauri::command]
async fn write_payload(path: String) -> Result<(), String> {
    if PAYLOAD.is_empty() {
        return Err("This installer was built without any resources to install.".into());
    }
    fs::write(path, PAYLOAD).map_err(to_err)
}

#[tauri::command]
async fn write_text_file(path: String, contents: String) -> Result<(), String> {
    fs::write(path, contents).map_err(to_err)
}

#[tauri::command]
async fn read_text_file(path: String) -> Result<String, String> {
    let bytes = fs::read(path).map_err(to_err)?;
    Ok(String::from_utf8_lossy(&bytes)
        .trim_start_matches('\u{feff}')
        .to_string())
}

/// Best-effort removal of temporary files.
#[tauri::command]
async fn remove_files(paths: Vec<String>) {
    for path in paths {
        let _ = fs::remove_file(path);
    }
}

/// Launch a PowerShell script elevated (UAC prompt). Returns once the elevated process has
/// started; the script reports progress through a JSON file that the frontend polls.
#[tauri::command]
async fn run_elevated_script(script_path: String) -> Result<(), String> {
    let arg_list = format!(
        "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script_path}\""
    );
    let command = format!(
        "Start-Process -FilePath powershell.exe -Verb RunAs -WindowStyle Hidden -ErrorAction Stop -ArgumentList {}",
        ps_quote(&arg_list)
    );
    let status = tauri::async_runtime::spawn_blocking(move || hidden_powershell().arg(command).status())
        .await
        .map_err(to_err)?
        .map_err(to_err)?;

    if status.success() {
        Ok(())
    } else {
        Err("Administrator permission is required to install. The request was cancelled or failed.".into())
    }
}

/// Start the installed app after `delay_secs`, detached from the installer so it survives exit.
#[tauri::command]
fn launch_app(exe_path: String, delay_secs: u32) -> Result<(), String> {
    let command = format!(
        "Start-Sleep -Seconds {delay_secs}; Start-Process -FilePath {}",
        ps_quote(&exe_path)
    );
    hidden_powershell().arg(command).spawn().map(|_| ()).map_err(to_err)
}

#[tauri::command]
fn exit_app(app: AppHandle) {
    app.exit(0);
}

/// WebView2 keeps its profile in %LOCALAPPDATA%\<identifier> while the installer runs.
/// Remove it shortly after the installer exits so nothing is left behind.
fn schedule_webview_data_cleanup(app: &AppHandle) {
    let Ok(dir) = app.path().app_local_data_dir() else {
        return;
    };
    if !dir.ends_with(&app.config().identifier) {
        return;
    }
    let command = format!(
        "Start-Sleep -Seconds 2; Remove-Item -LiteralPath {} -Recurse -Force -ErrorAction SilentlyContinue",
        ps_quote(&dir.to_string_lossy())
    );
    let _ = hidden_powershell().arg(command).spawn();
}

fn main() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![
            temp_dir,
            write_payload,
            write_text_file,
            read_text_file,
            remove_files,
            run_elevated_script,
            launch_app,
            exit_app
        ])
        .build(tauri::generate_context!())
        .expect("error while building Xvirus Installer")
        .run(|app, event| {
            if let RunEvent::Exit = event {
                schedule_webview_data_cleanup(app);
            }
        });
}

// Hide the console window in release builds on Windows.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use tauri::{
    menu::{Menu, MenuItem, PredefinedMenuItem},
    tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent},
    AppHandle, Manager, PhysicalPosition, WebviewWindow, WindowEvent,
};

const MAIN_WINDOW: &str = "main";

/// Distance kept between the window and the bottom of the screen so it sits above the taskbar.
const TASKBAR_MARGIN: f64 = 60.0;

/// Show, restore and focus the main window.
fn show_main_window(app: &AppHandle) {
    if let Some(window) = app.get_webview_window(MAIN_WINDOW) {
        let _ = window.unminimize();
        let _ = window.show();
        let _ = window.set_focus();
    }
}

/// Place the window at the bottom-right corner of the primary display.
fn position_bottom_right(window: &WebviewWindow) -> tauri::Result<()> {
    let Some(monitor) = window.primary_monitor()? else {
        return Ok(());
    };
    let win = window.outer_size()?;
    let origin = monitor.position();
    let screen = monitor.size();
    let margin = (TASKBAR_MARGIN * monitor.scale_factor()).round() as i32;

    let x = origin.x + screen.width as i32 - win.width as i32;
    let y = origin.y + screen.height as i32 - win.height as i32 - margin;
    window.set_position(PhysicalPosition::new(x, y))
}

/// Windows only shows toast notifications for an unpackaged app when its AppUserModelID is
/// registered. The notification plugin uses the bundle identifier as the AUMID, so register it
/// for the current user with the product name and icon.
#[cfg(windows)]
fn register_notification_app_id(app: &AppHandle) {
    use winreg::{enums::HKEY_CURRENT_USER, RegKey};

    let identifier = &app.config().identifier;
    let name = app.package_info().name.clone();

    let icon_path = app.path().app_local_data_dir().ok().and_then(|dir| {
        std::fs::create_dir_all(&dir).ok()?;
        let path = dir.join("notification-icon.png");
        std::fs::write(&path, include_bytes!("../icons/128x128.png")).ok()?;
        Some(path)
    });

    let result = RegKey::predef(HKEY_CURRENT_USER)
        .create_subkey(format!(r"Software\Classes\AppUserModelId\{identifier}"))
        .and_then(|(key, _)| {
            key.set_value("DisplayName", &name)?;
            if let Some(icon) = icon_path {
                key.set_value("IconUri", &icon.to_string_lossy().into_owned())?;
            }
            Ok(())
        });
    if let Err(err) = result {
        eprintln!("Failed to register notification AppUserModelID: {err}");
    }
}

/// Command-line arguments the app was launched with (excluding the executable path),
/// e.g. `--scan "<path>"` from the "Scan with Xvirus" context-menu entry.
#[tauri::command]
fn launch_args() -> Vec<String> {
    std::env::args().skip(1).collect()
}

/// Bring the main window to the foreground (used when a threat needs a decision).
#[tauri::command]
fn show_window(app: AppHandle) {
    show_main_window(&app);
}

fn setup_tray(app: &AppHandle) -> tauri::Result<()> {
    let title = app.package_info().name.clone();
    let open = MenuItem::with_id(app, "open", format!("Open {title}"), true, None::<&str>)?;
    let separator = PredefinedMenuItem::separator(app)?;
    let quit = MenuItem::with_id(app, "quit", "Quit", true, None::<&str>)?;
    let menu = Menu::with_items(app, &[&open, &separator, &quit])?;

    let mut tray = TrayIconBuilder::with_id("main")
        .tooltip(&title)
        .menu(&menu)
        .show_menu_on_left_click(false)
        .on_menu_event(|app, event| match event.id.as_ref() {
            "open" => show_main_window(app),
            "quit" => app.exit(0),
            _ => {}
        })
        .on_tray_icon_event(|tray, event| {
            if let TrayIconEvent::Click {
                button: MouseButton::Left,
                button_state: MouseButtonState::Up,
                ..
            } = event
            {
                show_main_window(tray.app_handle());
            }
        });
    if let Some(icon) = app.default_window_icon() {
        tray = tray.icon(icon.clone());
    }
    tray.build(app)?;
    Ok(())
}

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_notification::init())
        .plugin(tauri_plugin_dialog::init())
        .invoke_handler(tauri::generate_handler![launch_args, show_window])
        .setup(|app| {
            let handle = app.handle();

            if let Some(window) = app.get_webview_window(MAIN_WINDOW) {
                let _ = window.set_title(&handle.package_info().name);
                if let Err(err) = position_bottom_right(&window) {
                    eprintln!("Failed to position window: {err}");
                }
            }

            #[cfg(windows)]
            register_notification_app_id(handle);

            setup_tray(handle)?;
            Ok(())
        })
        .on_window_event(|window, event| {
            // Closing the window only hides it; the app keeps running in the system tray.
            if let WindowEvent::CloseRequested { api, .. } = event {
                api.prevent_close();
                let _ = window.hide();
            }
        })
        .run(tauri::generate_context!())
        .expect("error while running Xvirus UI");
}

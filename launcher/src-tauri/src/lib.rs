#[allow(dead_code)]
mod credential;
mod install;
mod paths;
mod state;

use serde::Serialize;
use state::LauncherConfig;
use std::path::PathBuf;
use std::process::Command;

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LauncherState {
    profile_id: String,
    daggerfall_path: Option<String>,
    client_ready: bool,
}

#[tauri::command]
fn load_state() -> Result<LauncherState, String> {
    let mut config = LauncherConfig::load();
    let profile_id = config.ensure_profile_id()?;

    Ok(LauncherState {
        profile_id,
        daggerfall_path: config.daggerfall_path.map(|p| p.to_string_lossy().into_owned()),
        client_ready: install::resolve_client_path().is_ok(),
    })
}

#[tauri::command]
fn set_daggerfall_path(path: String) -> Result<(), String> {
    let selected = PathBuf::from(path);
    let resolved = paths::resolve_daggerfall_root(&selected).ok_or(
        "That folder does not look like a Daggerfall install. Select either your Daggerfall folder \
         or the arena2 folder inside it.",
    )?;

    let mut config = LauncherConfig::load();
    config.ensure_profile_id()?;
    config.daggerfall_path = Some(resolved.clone());
    config.save()?;

    if let Ok(client_path) = install::resolve_client_path() {
        paths::write_client_settings(&client_path, &resolved)?;
    }

    Ok(())
}

#[tauri::command]
fn launch_client() -> Result<(), String> {
    let mut config = LauncherConfig::load();
    let profile_id = config.ensure_profile_id()?;

    let daggerfall_path = config
        .daggerfall_path
        .as_ref()
        .ok_or("Set your Daggerfall folder first.")?;
    if !paths::is_daggerfall_folder(daggerfall_path) {
        return Err("The saved Daggerfall folder is no longer valid.".into());
    }

    let client_path = install::resolve_client_path()?;

    paths::write_client_settings(&client_path, daggerfall_path)?;
    let session_path = paths::write_session_file(&profile_id, "")?;

    let working_directory = client_path
        .parent()
        .ok_or("Could not determine the client directory.")?;

    Command::new(&client_path)
        .current_dir(working_directory)
        .arg("-client")
        .arg("-dfmp-session")
        .arg(&session_path)
        .spawn()
        .map_err(|e| format!("Could not start the DFMP client: {e}"))?;

    Ok(())
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .invoke_handler(tauri::generate_handler![
            load_state,
            set_daggerfall_path,
            launch_client
        ])
        .run(tauri::generate_context!())
        .expect("error while running DFMP launcher");
}

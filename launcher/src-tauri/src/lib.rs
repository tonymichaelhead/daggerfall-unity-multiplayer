mod credential;
mod install;
mod paths;
mod state;

use serde::Serialize;
use state::LauncherConfig;
use std::path::PathBuf;
use std::process::Command;

const KEYRING_SERVICE: &str = "dev.dfmp.launcher";

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LauncherState {
    account_id: String,
    daggerfall_path: Option<String>,
    client_ready: bool,
    has_stored_password: bool,
}

fn keyring_entry(account_id: &str) -> Result<keyring::Entry, String> {
    keyring::Entry::new(KEYRING_SERVICE, account_id).map_err(|e| format!("Credential store unavailable: {e}"))
}

#[tauri::command]
fn load_state() -> Result<LauncherState, String> {
    let config = LauncherConfig::load();
    let has_stored_password = if config.account_id.is_empty() {
        false
    } else {
        keyring_entry(&config.account_id)
            .and_then(|entry| entry.get_password().map_err(|e| e.to_string()))
            .is_ok()
    };

    Ok(LauncherState {
        account_id: config.account_id,
        daggerfall_path: config.daggerfall_path.map(|p| p.to_string_lossy().into_owned()),
        client_ready: install::resolve_client_path().is_ok(),
        has_stored_password,
    })
}

#[tauri::command]
fn sign_in(account_id: String, password: String, remember: bool) -> Result<(), String> {
    let account_id = credential::normalize_account_id(&account_id)?;
    credential::check_password(&password)?;

    // Only the derived value is ever stored, so the password itself never rests on disk.
    let derived = credential::derive_client_credential(&account_id, &password);

    if remember {
        keyring_entry(&account_id)?
            .set_password(&derived)
            .map_err(|e| format!("Could not save your credentials: {e}"))?;
    }

    let mut config = LauncherConfig::load();
    config.account_id = account_id;
    config.save()
}

#[tauri::command]
fn sign_out() -> Result<(), String> {
    let mut config = LauncherConfig::load();
    if !config.account_id.is_empty() {
        if let Ok(entry) = keyring_entry(&config.account_id) {
            let _ = entry.delete_credential();
        }
    }

    config.account_id = String::new();
    config.save()
}

#[tauri::command]
fn set_daggerfall_path(path: String) -> Result<(), String> {
    let selected = PathBuf::from(path);
    let resolved = paths::resolve_daggerfall_root(&selected).ok_or(
        "That folder does not look like a Daggerfall install. Select either your Daggerfall folder \
         or the arena2 folder inside it.",
    )?;

    let mut config = LauncherConfig::load();
    config.daggerfall_path = Some(resolved.clone());
    config.save()?;

    if let Ok(client_path) = install::resolve_client_path() {
        paths::write_client_settings(&client_path, &resolved)?;
    }

    Ok(())
}

#[tauri::command]
fn launch_client() -> Result<(), String> {
    let config = LauncherConfig::load();

    if config.account_id.is_empty() {
        return Err("Sign in first.".into());
    }

    let daggerfall_path = config
        .daggerfall_path
        .as_ref()
        .ok_or("Set your Daggerfall folder first.")?;
    if !paths::is_daggerfall_folder(daggerfall_path) {
        return Err("The saved Daggerfall folder is no longer valid.".into());
    }

    let client_path = install::resolve_client_path()?;

    let derived = keyring_entry(&config.account_id)?
        .get_password()
        .map_err(|_| "Stored credentials are missing. Sign in again.".to_string())?;

    paths::write_client_settings(&client_path, daggerfall_path)?;
    let session_path = paths::write_session_file(&config.account_id, &derived)?;

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
            sign_in,
            sign_out,
            set_daggerfall_path,
            launch_client
        ])
        .run(tauri::generate_context!())
        .expect("error while running DFMP launcher");
}

use serde::{Deserialize, Serialize};
use std::fs;
use std::path::PathBuf;

#[derive(Default, Serialize, Deserialize)]
#[serde(default)]
pub struct LauncherConfig {
    pub account_id: String,
    pub daggerfall_path: Option<PathBuf>,
    pub client_path: Option<PathBuf>,
}

impl LauncherConfig {
    pub fn path() -> PathBuf {
        let mut path = dirs::config_dir().unwrap_or_else(|| PathBuf::from("."));
        path.push("dfmp-launcher");
        path.push("launcher.json");
        path
    }

    pub fn load() -> Self {
        let path = Self::path();
        let Ok(text) = fs::read_to_string(path) else {
            return Self::default();
        };

        serde_json::from_str(&text).unwrap_or_default()
    }

    pub fn save(&self) -> Result<(), String> {
        let path = Self::path();
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent).map_err(|e| format!("Could not create the settings folder: {e}"))?;
        }

        let text = serde_json::to_string_pretty(self).map_err(|e| e.to_string())?;
        fs::write(&path, text).map_err(|e| format!("Could not save settings: {e}"))
    }
}

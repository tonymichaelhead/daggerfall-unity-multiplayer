use serde::{Deserialize, Serialize};
use std::path::PathBuf;
use std::time::{SystemTime, UNIX_EPOCH};

#[derive(Default, Serialize, Deserialize)]
#[serde(default)]
pub struct LauncherConfig {
    pub profile_id: String,
    pub daggerfall_path: Option<PathBuf>,
    /// Reserved for remembered per-server logins once username/password returns.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub server_logins: Vec<serde_json::Value>,
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
        let Ok(text) = fs_read(&path) else {
            return Self::default();
        };

        serde_json::from_str(&text).unwrap_or_default()
    }

    pub fn save(&self) -> Result<(), String> {
        let path = Self::path();
        if let Some(parent) = path.parent() {
            std::fs::create_dir_all(parent).map_err(|e| format!("Could not create the settings folder: {e}"))?;
        }

        let text = serde_json::to_string_pretty(self).map_err(|e| e.to_string())?;
        std::fs::write(&path, text).map_err(|e| format!("Could not save settings: {e}"))
    }

    pub fn ensure_profile_id(&mut self) -> Result<String, String> {
        if self.profile_id.is_empty() {
            self.profile_id = new_profile_id();
            self.save()?;
        }

        Ok(self.profile_id.clone())
    }
}

fn fs_read(path: &PathBuf) -> Result<String, std::io::Error> {
    std::fs::read_to_string(path)
}

fn new_profile_id() -> String {
    let nanos = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|duration| duration.as_nanos())
        .unwrap_or(0);
    format!("profile-{:x}{:x}", nanos, std::process::id())
}

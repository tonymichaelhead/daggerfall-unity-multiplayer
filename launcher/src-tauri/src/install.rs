use serde::Deserialize;
use std::env;
use std::fs;
use std::path::{Path, PathBuf};

const INSTALL_CONFIG_NAME: &str = "dfmp-launcher.json";
const CLIENT_ENV_VAR: &str = "DFMP_CLIENT_PATH";

#[derive(Default, Deserialize)]
#[serde(default, rename_all = "camelCase")]
struct InstallConfig {
    client_path: Option<PathBuf>,
}

/// Resolves the DFMP client executable for the bundled install.
///
/// Order: `DFMP_CLIENT_PATH` env var, `clientPath` in exe-adjacent
/// `dfmp-launcher.json`, default `client/` subfolder beside the launcher, then
/// (debug builds only) the Unity `Build/` output used during development.
pub fn resolve_client_path() -> Result<PathBuf, String> {
    let exe_dir = launcher_directory()?;
    resolve_client_path_from(&exe_dir, env::var_os(CLIENT_ENV_VAR).map(PathBuf::from))
}

fn resolve_client_path_from(exe_dir: &Path, env_override: Option<PathBuf>) -> Result<PathBuf, String> {
    if let Some(path) = env_override {
        return require_client_file(&resolve_against_exe_dir(exe_dir, path), "DFMP_CLIENT_PATH");
    }

    if let Some(path) = load_install_config(exe_dir)?.client_path {
        return require_client_file(
            &resolve_against_exe_dir(exe_dir, path),
            "dfmp-launcher.json clientPath",
        );
    }

    for name in default_client_names() {
        let candidate = exe_dir.join("client").join(name);
        if candidate.is_file() {
            return Ok(candidate);
        }
    }

    #[cfg(debug_assertions)]
    {
        for name in default_client_names() {
            let candidate = exe_dir
                .join("..")
                .join("..")
                .join("..")
                .join("..")
                .join("Build")
                .join(name);
            if let Ok(canonical) = candidate.canonicalize() {
                if canonical.is_file() {
                    return Ok(canonical);
                }
            }
            if candidate.is_file() {
                return Ok(candidate);
            }
        }
    }

    Err(
        "DFMP client files were not found next to the launcher. Reinstall DFMP, keeping the \
         launcher and its `client` folder together."
            .into(),
    )
}

fn launcher_directory() -> Result<PathBuf, String> {
    let exe = env::current_exe().map_err(|e| format!("Could not locate the launcher: {e}"))?;
    exe.parent()
        .map(Path::to_path_buf)
        .ok_or_else(|| "Could not determine the launcher directory.".into())
}

fn load_install_config(exe_dir: &Path) -> Result<InstallConfig, String> {
    let path = exe_dir.join(INSTALL_CONFIG_NAME);
    if !path.exists() {
        return Ok(InstallConfig::default());
    }

    let text = fs::read_to_string(&path)
        .map_err(|e| format!("Could not read {INSTALL_CONFIG_NAME}: {e}"))?;
    serde_json::from_str(&text)
        .map_err(|e| format!("{INSTALL_CONFIG_NAME} is invalid: {e}"))
}

fn resolve_against_exe_dir(exe_dir: &Path, path: PathBuf) -> PathBuf {
    if path.is_absolute() {
        path
    } else {
        exe_dir.join(path)
    }
}

fn require_client_file(path: &Path, source: &str) -> Result<PathBuf, String> {
    if path.is_file() {
        Ok(path.to_path_buf())
    } else {
        Err(format!(
            "The DFMP client at {} (from {source}) is missing.",
            path.display()
        ))
    }
}

fn default_client_names() -> &'static [&'static str] {
    #[cfg(target_os = "windows")]
    {
        &["Daggerfall Unity.exe"]
    }
    #[cfg(target_os = "linux")]
    {
        &["Daggerfall Unity.x86_64", "Daggerfall Unity"]
    }
    #[cfg(not(any(target_os = "windows", target_os = "linux")))]
    {
        &["Daggerfall Unity"]
    }
}

#[cfg(test)]
mod tests {
    use super::{resolve_client_path_from, InstallConfig, INSTALL_CONFIG_NAME};
    use std::fs;
    use std::path::{Path, PathBuf};

    fn temp_dir(name: &str) -> PathBuf {
        let path = std::env::temp_dir().join(format!("dfmp-install-{name}-{}", std::process::id()));
        let _ = fs::remove_dir_all(&path);
        fs::create_dir_all(&path).unwrap();
        path
    }

    fn write_client(directory: &Path, name: &str) -> PathBuf {
        fs::create_dir_all(directory).unwrap();
        let path = directory.join(name);
        fs::write(&path, b"stub").unwrap();
        path
    }

    #[test]
    fn env_override_wins() {
        let root = temp_dir("env");
        let client = write_client(&root.join("elsewhere"), "custom-client.exe");

        let resolved = resolve_client_path_from(&root, Some(client.clone())).unwrap();
        assert_eq!(resolved, client);
        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn absolute_config_override() {
        let root = temp_dir("absolute-config");
        let client = write_client(&root.join("elsewhere"), "custom-client.exe");
        let config = serde_json::json!({ "clientPath": client });
        fs::write(root.join(INSTALL_CONFIG_NAME), config.to_string()).unwrap();

        let resolved = resolve_client_path_from(&root, None).unwrap();
        assert_eq!(resolved, client);
        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn relative_config_override_resolves_against_exe_dir() {
        let root = temp_dir("relative-config");
        let client = write_client(&root.join("custom"), "Daggerfall Unity.exe");
        let config = serde_json::json!({ "clientPath": "custom/Daggerfall Unity.exe" });
        fs::write(root.join(INSTALL_CONFIG_NAME), config.to_string()).unwrap();

        let resolved = resolve_client_path_from(&root, None).unwrap();
        assert_eq!(resolved, client);
        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn default_client_subfolder() {
        let root = temp_dir("default");
        let name = super::default_client_names()[0];
        let client = write_client(&root.join("client"), name);

        let resolved = resolve_client_path_from(&root, None).unwrap();
        assert_eq!(resolved, client);
        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn missing_client_returns_readable_error() {
        let root = temp_dir("missing");

        let error = resolve_client_path_from(&root, None).unwrap_err();
        assert!(error.contains("client"));
        assert!(error.contains("launcher"));
        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn malformed_config_is_a_hard_error() {
        let root = temp_dir("malformed");
        fs::write(root.join(INSTALL_CONFIG_NAME), "{ not json").unwrap();

        let error = resolve_client_path_from(&root, None).unwrap_err();
        assert!(error.contains(INSTALL_CONFIG_NAME));
        assert!(error.contains("invalid"));
        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn missing_override_target_reports_source() {
        let root = temp_dir("missing-override");
        let missing = root.join("nope.exe");
        let config = serde_json::json!({ "clientPath": "nope.exe" });
        fs::write(root.join(INSTALL_CONFIG_NAME), config.to_string()).unwrap();

        let error = resolve_client_path_from(&root, None).unwrap_err();
        assert!(error.contains("dfmp-launcher.json clientPath"));
        assert!(error.contains(missing.to_string_lossy().as_ref()) || error.contains("nope.exe"));
        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn install_config_deserializes_camel_case() {
        let config: InstallConfig =
            serde_json::from_str(r#"{ "clientPath": "client/Daggerfall Unity.exe" }"#).unwrap();
        assert_eq!(
            config.client_path.as_deref(),
            Some(Path::new("client/Daggerfall Unity.exe"))
        );
    }
}

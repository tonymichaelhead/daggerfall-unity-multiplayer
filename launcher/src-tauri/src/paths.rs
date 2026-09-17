use crate::state::LauncherConfig;
use std::fs;
use std::path::{Path, PathBuf};

/// A valid Daggerfall install has an arena2 folder containing the core BSA archives.
pub fn is_daggerfall_folder(path: &Path) -> bool {
    let Some(arena2) = find_arena2(path) else {
        return false;
    };

    ["ARCH3D.BSA", "BLOCKS.BSA", "MAPS.BSA"]
        .iter()
        .all(|name| contains_file_ignoring_case(&arena2, name))
}

fn find_arena2(path: &Path) -> Option<PathBuf> {
    let entries = fs::read_dir(path).ok()?;
    for entry in entries.flatten() {
        if entry.file_name().to_string_lossy().eq_ignore_ascii_case("arena2") && entry.path().is_dir() {
            return Some(entry.path());
        }
    }
    None
}

fn contains_file_ignoring_case(directory: &Path, file_name: &str) -> bool {
    let Ok(entries) = fs::read_dir(directory) else {
        return false;
    };

    entries
        .flatten()
        .any(|entry| entry.file_name().to_string_lossy().eq_ignore_ascii_case(file_name))
}

/// The DFMP client is always run as a portable install so it never reads or writes the settings,
/// saves, or keybinds of the player's existing Daggerfall Unity install.
fn portable_data_directory(client_path: &Path) -> Result<PathBuf, String> {
    let client_directory = client_path
        .parent()
        .ok_or("Could not determine the client directory.")?;

    let marker = client_directory.join("Portable.txt");
    if !marker.exists() {
        fs::write(&marker, "DFMP client runs as a portable install.\n")
            .map_err(|e| format!("Could not mark the client as a portable install: {e}"))?;
    }

    let data_directory = client_directory.join("PortableAppdata");
    fs::create_dir_all(&data_directory)
        .map_err(|e| format!("Could not create the client data folder: {e}"))?;

    Ok(data_directory)
}

pub fn write_client_settings(config: &LauncherConfig) -> Result<(), String> {
    let (Some(client_path), Some(daggerfall_path)) = (&config.client_path, &config.daggerfall_path) else {
        return Ok(());
    };

    let settings_path = portable_data_directory(client_path)?.join("settings.ini");
    let existing = fs::read_to_string(&settings_path).unwrap_or_default();
    let updated = set_ini_value(
        &existing,
        "Daggerfall",
        "MyDaggerfallPath",
        &daggerfall_path.to_string_lossy(),
    );

    fs::write(&settings_path, updated).map_err(|e| format!("Could not write client settings: {e}"))
}

/// Minimal INI edit that preserves every line it does not own. The client owns this file, so the
/// launcher must not rewrite or reorder anything else in it.
fn set_ini_value(source: &str, section: &str, key: &str, value: &str) -> String {
    let mut lines: Vec<String> = source.lines().map(|line| line.to_string()).collect();
    let section_header = format!("[{section}]");

    let section_start = lines
        .iter()
        .position(|line| line.trim().eq_ignore_ascii_case(&section_header));

    let Some(section_start) = section_start else {
        if !lines.is_empty() && !lines.last().map(|l| l.trim().is_empty()).unwrap_or(true) {
            lines.push(String::new());
        }
        lines.push(section_header);
        lines.push(format!("{key}={value}"));
        lines.push(String::new());
        return lines.join("\n");
    };

    let section_end = lines
        .iter()
        .enumerate()
        .skip(section_start + 1)
        .find(|(_, line)| line.trim().starts_with('['))
        .map(|(index, _)| index)
        .unwrap_or(lines.len());

    for index in section_start + 1..section_end {
        let trimmed = lines[index].trim_start();
        if trimmed.starts_with(';') || trimmed.starts_with('#') {
            continue;
        }

        if let Some((existing_key, _)) = trimmed.split_once('=') {
            if existing_key.trim().eq_ignore_ascii_case(key) {
                lines[index] = format!("{key}={value}");
                return lines.join("\n");
            }
        }
    }

    lines.insert(section_end, format!("{key}={value}"));
    lines.join("\n")
}

/// Writes the one-shot session handoff. The client deletes it on read. Credentials go through a
/// file rather than the command line because any local process can read another process's argv.
pub fn write_session_file(account_id: &str, credential: &str) -> Result<PathBuf, String> {
    let mut path = std::env::temp_dir();
    path.push(format!("dfmp-session-{}.json", std::process::id()));

    let payload = serde_json::json!({ "accountId": account_id, "credential": credential });
    let text = serde_json::to_string(&payload).map_err(|e| e.to_string())?;

    write_private_file(&path, &text)?;
    Ok(path)
}

#[cfg(unix)]
fn write_private_file(path: &Path, contents: &str) -> Result<(), String> {
    use std::io::Write;
    use std::os::unix::fs::OpenOptionsExt;

    let mut file = fs::OpenOptions::new()
        .write(true)
        .create(true)
        .truncate(true)
        .mode(0o600)
        .open(path)
        .map_err(|e| format!("Could not create the session file: {e}"))?;

    file.write_all(contents.as_bytes())
        .map_err(|e| format!("Could not write the session file: {e}"))
}

#[cfg(not(unix))]
fn write_private_file(path: &Path, contents: &str) -> Result<(), String> {
    // On Windows the per-user temp directory is already ACL-restricted to the current user.
    fs::write(path, contents).map_err(|e| format!("Could not write the session file: {e}"))
}

#[cfg(test)]
mod tests {
    use super::set_ini_value;

    #[test]
    fn replaces_an_existing_key_in_place() {
        let source = "[Daggerfall]\nMyDaggerfallPath=/old\nOther=1\n[Video]\nFullscreen=1";
        let updated = set_ini_value(source, "Daggerfall", "MyDaggerfallPath", "/new");

        assert!(updated.contains("MyDaggerfallPath=/new"));
        assert!(!updated.contains("/old"));
        assert!(updated.contains("Other=1"));
        assert!(updated.contains("Fullscreen=1"));
    }

    #[test]
    fn appends_the_key_to_an_existing_section() {
        let source = "[Daggerfall]\nOther=1\n[Video]\nFullscreen=1";
        let updated = set_ini_value(source, "Daggerfall", "MyDaggerfallPath", "/new");

        let daggerfall_block = updated.split("[Video]").next().unwrap();
        assert!(daggerfall_block.contains("MyDaggerfallPath=/new"));
    }

    #[test]
    fn creates_the_section_when_absent() {
        let updated = set_ini_value("[Video]\nFullscreen=1", "Daggerfall", "MyDaggerfallPath", "/new");

        assert!(updated.contains("[Daggerfall]"));
        assert!(updated.contains("MyDaggerfallPath=/new"));
        assert!(updated.contains("Fullscreen=1"));
    }

    #[test]
    fn ignores_commented_keys() {
        let source = "[Daggerfall]\n;MyDaggerfallPath=/commented\n";
        let updated = set_ini_value(source, "Daggerfall", "MyDaggerfallPath", "/new");

        assert!(updated.contains(";MyDaggerfallPath=/commented"));
        assert!(updated.contains("MyDaggerfallPath=/new"));
    }
}

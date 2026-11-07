use crate::models::{DownloadedItem, SoundboardConfig};
use std::fs;
use std::path::PathBuf;

pub fn get_app_data_dir() -> PathBuf {
    dirs::data_dir()
        .unwrap_or_else(|| PathBuf::from("."))
        .join("TikTokSoundboard")
}

pub fn get_config_path() -> PathBuf {
    get_app_data_dir().join("soundboard_config.json")
}

pub fn get_default_download_dir() -> PathBuf {
    dirs::document_dir()
        .unwrap_or_else(|| PathBuf::from("."))
        .join("TikTokSoundboardDownloads")
}

pub fn load_config() -> SoundboardConfig {
    let config_path = get_config_path();
    let legacy_exe_path = std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(|d| d.join("soundboard_config.json")));
    let legacy_cwd_path = std::env::current_dir()
        .ok()
        .map(|d| d.join("soundboard_config.json"));

    let raw_json = if config_path.exists() {
        fs::read_to_string(&config_path).ok()
    } else if let Some(ref p) = legacy_exe_path {
        if p.exists() {
            fs::read_to_string(p).ok()
        } else if let Some(ref cwd) = legacy_cwd_path {
            fs::read_to_string(cwd).ok()
        } else {
            None
        }
    } else if let Some(ref cwd) = legacy_cwd_path {
        fs::read_to_string(cwd).ok()
    } else {
        None
    };

    let mut cfg: SoundboardConfig = raw_json
        .and_then(|s| serde_json::from_str(&s).ok())
        .unwrap_or_default();

    if cfg.download_directory.trim().is_empty() {
        cfg.download_directory = get_default_download_dir().to_string_lossy().to_string();
    }

    cfg
}

pub fn save_config(config: &SoundboardConfig) -> Result<(), String> {
    let dir = get_app_data_dir();
    fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    let json = serde_json::to_string_pretty(config).map_err(|e| e.to_string())?;
    fs::write(get_config_path(), json).map_err(|e| e.to_string())?;
    Ok(())
}

pub fn list_downloaded_files(directory: &str) -> Vec<DownloadedItem> {
    let dir_path = if directory.trim().is_empty() {
        get_default_download_dir()
    } else {
        PathBuf::from(directory.trim())
    };

    if !dir_path.exists() {
        return Vec::new();
    }

    let valid_exts = [
        "mp3", "wav", "ogg", "flac", "aac", "wma", "m4a", "opus",
    ];

    let mut entries: Vec<(std::time::SystemTime, DownloadedItem)> = Vec::new();
    if let Ok(read_dir) = fs::read_dir(&dir_path) {
        for entry in read_dir.flatten() {
            let path = entry.path();
            if !path.is_file() {
                continue;
            }
            let ext_ok = path
                .extension()
                .and_then(|e| e.to_str())
                .map(|e| valid_exts.contains(&e.to_ascii_lowercase().as_str()))
                .unwrap_or(false);
            if !ext_ok {
                continue;
            }

            let modified = entry
                .metadata()
                .and_then(|m| m.modified())
                .unwrap_or(std::time::SystemTime::UNIX_EPOCH);
            let stem = path
                .file_stem()
                .and_then(|s| s.to_str())
                .unwrap_or("audio")
                .to_string();

            entries.push((
                modified,
                DownloadedItem {
                    name: stem,
                    path: path.to_string_lossy().to_string(),
                },
            ));
        }
    }

    entries.sort_by(|a, b| b.0.cmp(&a.0));
    entries.into_iter().map(|(_, item)| item).collect()
}


#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod audio;
mod config;
mod hotkeys;
mod models;
mod ytdlp;

use audio::AudioService;
use hotkeys::GlobalHotkeyService;
use models::{DownloadedItem, SoundboardConfig, WaveformData};
use tauri::{AppHandle, Manager, State};
use ytdlp::YtDlpService;

struct AppState {
    audio: AudioService,
    hotkeys: GlobalHotkeyService,
    ytdlp: YtDlpService,
}

#[tauri::command]
fn load_config(state: State<'_, AppState>) -> SoundboardConfig {
    let cfg = config::load_config();
    state.audio.set_master_volume(cfg.master_volume);
    cfg
}

#[tauri::command]
fn save_config(config: SoundboardConfig) -> Result<(), String> {
    config::save_config(&config)
}

#[tauri::command]
fn list_downloaded_files(directory: String) -> Vec<DownloadedItem> {
    config::list_downloaded_files(&directory)
}

#[tauri::command]
fn play_sound(
    state: State<'_, AppState>,
    key: String,
    file_path: String,
    volume: i32,
    start_time: f64,
    end_time: f64,
    speed: i32,
) -> Result<bool, String> {
    state
        .audio
        .play_sound(key, &file_path, volume, start_time, end_time, speed)
}

#[tauri::command]
fn stop_sound(state: State<'_, AppState>, key: String) {
    state.audio.stop_sound(key);
}

#[tauri::command]
fn stop_all_sounds(state: State<'_, AppState>) {
    state.audio.stop_all();
}

#[tauri::command]
fn set_master_volume(state: State<'_, AppState>, volume: i32) {
    state.audio.set_master_volume(volume);
}

#[tauri::command]
async fn get_waveform_peaks(
    state: State<'_, AppState>,
    file_path: String,
) -> Result<WaveformData, String> {
    let audio = state.audio.clone();
    tokio::task::spawn_blocking(move || audio.get_waveform(&file_path))
        .await
        .map_err(|e| e.to_string())?
}

#[tauri::command]
async fn play_preview(
    state: State<'_, AppState>,
    file_path: String,
    volume: i32,
    start_time: f64,
    end_time: f64,
    speed: i32,
) -> Result<(), String> {
    let audio = state.audio.clone();
    tokio::task::spawn_blocking(move || {
        audio.play_preview(&file_path, volume, start_time, end_time, speed)
    })
    .await
    .map_err(|e| e.to_string())?
}

#[tauri::command]
fn stop_preview(state: State<'_, AppState>) {
    state.audio.stop_preview();
}

#[tauri::command]
fn sync_hotkeys(state: State<'_, AppState>, registered_keys: Vec<String>, active: bool) {
    state.hotkeys.sync(registered_keys, active);
}

#[tauri::command]
async fn download_audio(
    app: AppHandle,
    state: State<'_, AppState>,
    url: String,
    download_dir: String,
) -> Result<DownloadedItem, String> {
    let ytdlp = state.ytdlp.clone();
    ytdlp.download_audio(app, url, download_dir).await
}

#[tauri::command]
fn cancel_download(state: State<'_, AppState>) {
    state.ytdlp.cancel();
}

#[tauri::command]
async fn pick_audio_file() -> Option<String> {
    tokio::task::spawn_blocking(|| {
        rfd::FileDialog::new()
            .set_title("เลือกไฟล์เสียง")
            .add_filter(
                "Audio Files",
                &["mp3", "wav", "ogg", "flac", "aac", "wma", "m4a", "opus"],
            )
            .add_filter("All Files", &["*"])
            .pick_file()
            .map(|p| p.to_string_lossy().to_string())
    })
    .await
    .ok()
    .flatten()
}

#[tauri::command]
async fn pick_download_folder(initial_dir: String) -> Option<String> {
    tokio::task::spawn_blocking(move || {
        let mut dlg =
            rfd::FileDialog::new().set_title("เลือกโฟลเดอร์สำหรับเก็บไฟล์เสียงที่ดาวน์โหลด");
        if !initial_dir.trim().is_empty() {
            let p = std::path::Path::new(initial_dir.trim());
            if p.exists() {
                dlg = dlg.set_directory(p);
            }
        }
        dlg.pick_folder().map(|p| p.to_string_lossy().to_string())
    })
    .await
    .ok()
    .flatten()
}

fn main() {
    tauri::Builder::default()
        .setup(|app| {
            let initial_cfg = config::load_config();
            let handle = app.handle().clone();
            let audio = AudioService::new(handle.clone(), initial_cfg.master_volume);
            let hotkeys = GlobalHotkeyService::new(handle);
            let ytdlp = YtDlpService::new();

            app.manage(AppState {
                audio,
                hotkeys,
                ytdlp,
            });
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            load_config,
            save_config,
            list_downloaded_files,
            play_sound,
            stop_sound,
            stop_all_sounds,
            set_master_volume,
            get_waveform_peaks,
            play_preview,
            stop_preview,
            sync_hotkeys,
            download_audio,
            cancel_download,
            pick_audio_file,
            pick_download_folder,
        ])
        .run(tauri::generate_context!())
        .expect("error while running TikTok Soundboard");
}


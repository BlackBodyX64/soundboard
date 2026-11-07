use serde::{Deserialize, Serialize};

fn default_volume() -> i32 {
    100
}

fn default_neg_one() -> f64 {
    -1.0
}

fn default_speed() -> i32 {
    100
}

fn default_master_volume() -> i32 {
    80
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SoundPad {
    #[serde(default)]
    pub key: String,
    #[serde(default)]
    pub sound_path: String,
    #[serde(default)]
    pub sound_name: String,
    #[serde(default = "default_volume")]
    pub volume: i32,
    #[serde(default = "default_neg_one")]
    pub start_time: f64,
    #[serde(default = "default_neg_one")]
    pub end_time: f64,
    #[serde(default = "default_speed")]
    pub speed: i32,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SoundboardConfig {
    #[serde(default = "default_master_volume")]
    pub master_volume: i32,
    #[serde(default)]
    pub download_directory: String,
    #[serde(default)]
    pub pads: Vec<SoundPad>,
}

impl Default for SoundboardConfig {
    fn default() -> Self {
        Self {
            master_volume: 80,
            download_directory: String::new(),
            pads: Vec::new(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DownloadedItem {
    pub name: String,
    pub path: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct WaveformData {
    pub peaks: Vec<f32>,
    pub duration: f64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DownloadProgressEvent {
    pub percentage: f64,
    pub message: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SoundStateEvent {
    pub key: String,
    pub playing: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PreviewProgressEvent {
    pub ratio: f64,
    pub finished: bool,
}


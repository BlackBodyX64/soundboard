use crate::config::{get_app_data_dir, get_default_download_dir};
use crate::models::{DownloadProgressEvent, DownloadedItem};
use futures_util::StreamExt;
use regex::Regex;
use std::fs;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::process::Stdio;
use std::sync::{Arc, Mutex};
use tauri::{AppHandle, Emitter};
use tokio::io::{AsyncBufReadExt, BufReader};
use tokio::process::Command;
use tokio::sync::Notify;

#[cfg(windows)]
const CREATE_NO_WINDOW: u32 = 0x08000000;

const YTDLP_DOWNLOAD_URL: &str =
    "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
const FFMPEG_DOWNLOAD_URL: &str =
    "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

#[derive(Clone)]
pub struct YtDlpService {
    cancel_notify: Arc<Notify>,
    is_cancelled: Arc<Mutex<bool>>,
}

impl YtDlpService {
    pub fn new() -> Self {
        Self {
            cancel_notify: Arc::new(Notify::new()),
            is_cancelled: Arc::new(Mutex::new(false)),
        }
    }

    pub fn cancel(&self) {
        if let Ok(mut guard) = self.is_cancelled.lock() {
            *guard = true;
        }
        self.cancel_notify.notify_waiters();
    }

    fn reset_cancel(&self) {
        if let Ok(mut guard) = self.is_cancelled.lock() {
            *guard = false;
        }
    }

    fn check_cancelled(&self) -> Result<(), String> {
        if let Ok(guard) = self.is_cancelled.lock() {
            if *guard {
                return Err("ยกเลิกการดาวน์โหลดแล้ว".to_string());
            }
        }
        Ok(())
    }

    fn tools_dir() -> PathBuf {
        get_app_data_dir().join("tools")
    }

    fn ytdlp_path() -> PathBuf {
        Self::tools_dir().join("yt-dlp.exe")
    }

    fn ffmpeg_path() -> PathBuf {
        Self::tools_dir().join("ffmpeg.exe")
    }

    fn emit_progress(app: &AppHandle, message: impl Into<String>, percentage: f64) {
        let _ = app.emit(
            "download-progress",
            DownloadProgressEvent {
                message: message.into(),
                percentage,
            },
        );
    }

    async fn ensure_ytdlp(&self, app: &AppHandle) -> Result<PathBuf, String> {
        let tools = Self::tools_dir();
        fs::create_dir_all(&tools).map_err(|e| e.to_string())?;
        let ytdlp = Self::ytdlp_path();

        if !ytdlp.exists() {
            Self::emit_progress(app, "กำลังดาวน์โหลด yt-dlp...", 0.0);
            let client = reqwest::Client::builder()
                .user_agent("TikTokSoundboard/2.3")
                .build()
                .map_err(|e| e.to_string())?;

            let resp = client
                .get(YTDLP_DOWNLOAD_URL)
                .send()
                .await
                .map_err(|e| e.to_string())?;
            let bytes = resp.bytes().await.map_err(|e| e.to_string())?;
            self.check_cancelled()?;

            fs::write(&ytdlp, &bytes).map_err(|e| e.to_string())?;
            Self::emit_progress(app, "ดาวน์โหลด yt-dlp สำเร็จ", 100.0);
        }

        Ok(ytdlp)
    }

    async fn ensure_ffmpeg(&self, app: &AppHandle) -> Result<PathBuf, String> {
        let tools = Self::tools_dir();
        fs::create_dir_all(&tools).map_err(|e| e.to_string())?;
        let ffmpeg = Self::ffmpeg_path();

        if !ffmpeg.exists() {
            Self::emit_progress(app, "กำลังดาวน์โหลด ffmpeg (อาจใช้เวลาสักครู่)...", 0.0);
            let client = reqwest::Client::builder()
                .user_agent("TikTokSoundboard/2.3")
                .build()
                .map_err(|e| e.to_string())?;

            let zip_path = tools.join("ffmpeg.zip");
            let resp = client
                .get(FFMPEG_DOWNLOAD_URL)
                .send()
                .await
                .map_err(|e| e.to_string())?;

            let mut file = fs::File::create(&zip_path).map_err(|e| e.to_string())?;
            let mut stream = resp.bytes_stream();

            while let Some(chunk_res) = stream.next().await {
                self.check_cancelled()?;
                let chunk = chunk_res.map_err(|e| e.to_string())?;
                file.write_all(&chunk).map_err(|e| e.to_string())?;
            }
            drop(file);

            Self::emit_progress(app, "กำลังแตกไฟล์ ffmpeg...", 50.0);

            let tools_clone = tools.clone();
            let zip_clone = zip_path.clone();
            tokio::task::spawn_blocking(move || -> Result<(), String> {
                let zf = fs::File::open(&zip_clone).map_err(|e| e.to_string())?;
                let mut archive = zip::ZipArchive::new(zf).map_err(|e| e.to_string())?;

                for i in 0..archive.len() {
                    let mut entry = archive.by_index(i).map_err(|e| e.to_string())?;
                    let name = entry.name().to_string();
                    if name.ends_with("/ffmpeg.exe") || name.ends_with("\\ffmpeg.exe") {
                        let mut out =
                            fs::File::create(tools_clone.join("ffmpeg.exe")).map_err(|e| e.to_string())?;
                        std::io::copy(&mut entry, &mut out).map_err(|e| e.to_string())?;
                    } else if name.ends_with("/ffprobe.exe") || name.ends_with("\\ffprobe.exe") {
                        let mut out =
                            fs::File::create(tools_clone.join("ffprobe.exe")).map_err(|e| e.to_string())?;
                        std::io::copy(&mut entry, &mut out).map_err(|e| e.to_string())?;
                    }
                }
                let _ = fs::remove_file(&zip_clone);
                Ok(())
            })
            .await
            .map_err(|e| e.to_string())??;

            Self::emit_progress(app, "ดาวน์โหลด ffmpeg สำเร็จ", 100.0);
        }

        Ok(ffmpeg)
    }

    async fn get_title(&self, ytdlp: &Path, url: &str) -> Option<String> {
        let mut cmd = Command::new(ytdlp);
        cmd.arg("--get-title")
            .arg(url)
            .stdout(Stdio::piped())
            .stderr(Stdio::null());

        #[cfg(windows)]
        cmd.creation_flags(CREATE_NO_WINDOW);

        let output = cmd.output().await.ok()?;
        let s = String::from_utf8_lossy(&output.stdout).trim().to_string();
        if s.is_empty() {
            None
        } else {
            Some(s.lines().next().unwrap_or(&s).trim().to_string())
        }
    }

    pub async fn download_audio(
        &self,
        app: AppHandle,
        url: String,
        download_dir: String,
    ) -> Result<DownloadedItem, String> {
        self.reset_cancel();

        let ytdlp = self.ensure_ytdlp(&app).await?;
        let _ffmpeg = self.ensure_ffmpeg(&app).await?;
        self.check_cancelled()?;

        let target_dir = if download_dir.trim().is_empty() {
            get_default_download_dir()
        } else {
            PathBuf::from(download_dir.trim())
        };
        fs::create_dir_all(&target_dir).map_err(|e| e.to_string())?;

        Self::emit_progress(&app, "กำลังดึงข้อมูล...", 0.0);
        let title = self.get_title(&ytdlp, &url).await;
        self.check_cancelled()?;

        if let Some(ref t) = title {
            Self::emit_progress(&app, format!("กำลังดาวน์โหลด: {}", t), 0.0);
        } else {
            Self::emit_progress(&app, "กำลังเริ่มดาวน์โหลดเสียง...", 0.0);
        }

        let output_template = target_dir.join("%(title)s.%(ext)s");
        let tools_dir = Self::tools_dir();

        let mut cmd = Command::new(&ytdlp);
        cmd.arg("--no-playlist")
            .arg("--extract-audio")
            .arg("--audio-format")
            .arg("mp3")
            .arg("--audio-quality")
            .arg("0")
            .arg("--ffmpeg-location")
            .arg(&tools_dir)
            .arg("-o")
            .arg(&output_template)
            .arg("--no-overwrites")
            .arg("--newline")
            .arg(&url)
            .current_dir(&target_dir)
            .stdout(Stdio::piped())
            .stderr(Stdio::piped());

        #[cfg(windows)]
        cmd.creation_flags(CREATE_NO_WINDOW);

        let mut child = cmd
            .spawn()
            .map_err(|e| format!("ไม่สามารถเริ่มกระบวนการดาวน์โหลดได้: {}", e))?;

        let stdout = child.stdout.take().unwrap();
        let stderr = child.stderr.take().unwrap();

        let mut stdout_reader = BufReader::new(stdout).lines();
        let mut stderr_reader = BufReader::new(stderr).lines();

        let re_dest = Regex::new(r"\[ExtractAudio\] Destination:\s*(.+)$").unwrap();
        let re_already =
            Regex::new(r"\[download\]\s+(.+\.mp3)\s+has already been downloaded").unwrap();
        let re_pct_size = Regex::new(r"(\d+\.?\d*)%\s+of\s+~?\s*(\d+\.?\d*[a-zA-Z]+)").unwrap();
        let re_pct = Regex::new(r"(\d+\.?\d*)%").unwrap();

        let mut detected_path: Option<String> = None;
        let mut stderr_log = String::new();
        let cancel_notify = Arc::clone(&self.cancel_notify);

        loop {
            tokio::select! {
                _ = cancel_notify.notified() => {
                    let _ = child.kill().await;
                    return Err("ยกเลิกการดาวน์โหลดแล้ว".to_string());
                }
                line_res = stdout_reader.next_line() => {
                    match line_res {
                        Ok(Some(line)) => {
                            if let Some(caps) = re_dest.captures(&line) {
                                detected_path = Some(caps[1].trim().trim_matches('"').to_string());
                            } else if let Some(caps) = re_already.captures(&line) {
                                detected_path = Some(caps[1].trim().trim_matches('"').to_string());
                            } else if line.contains("[download]") && line.contains('%') {
                                if let Some(caps) = re_pct_size.captures(&line) {
                                    if let Ok(pct) = caps[1].parse::<f64>() {
                                        Self::emit_progress(
                                            &app,
                                            format!("กำลังดาวน์โหลด... {}% (ขนาด {})", pct, &caps[2]),
                                            pct,
                                        );
                                    }
                                } else if let Some(caps) = re_pct.captures(&line) {
                                    if let Ok(pct) = caps[1].parse::<f64>() {
                                        Self::emit_progress(
                                            &app,
                                            format!("กำลังดาวน์โหลด... {}%", pct),
                                            pct,
                                        );
                                    }
                                }
                            }
                        }
                        Ok(None) => break,
                        Err(_) => break,
                    }
                }
                err_res = stderr_reader.next_line() => {
                    if let Ok(Some(line)) = err_res {
                        stderr_log.push_str(&line);
                        stderr_log.push('\n');
                    }
                }
            }
        }

        let status = child.wait().await.map_err(|e| e.to_string())?;
        self.check_cancelled()?;

        let final_path = detected_path
            .map(PathBuf::from)
            .filter(|p| p.exists())
            .or_else(|| {
                // Fallback: newest .mp3 file in target_dir
                fs::read_dir(&target_dir)
                    .ok()?
                    .flatten()
                    .map(|e| e.path())
                    .filter(|p| {
                        p.extension()
                            .and_then(|x| x.to_str())
                            .map(|x| x.eq_ignore_ascii_case("mp3"))
                            .unwrap_or(false)
                    })
                    .max_by_key(|p| {
                        p.metadata()
                            .and_then(|m| m.modified())
                            .unwrap_or(std::time::SystemTime::UNIX_EPOCH)
                    })
            });

        match final_path {
            Some(path) if path.exists() => {
                let item_name = title.unwrap_or_else(|| {
                    path.file_stem()
                        .and_then(|s| s.to_str())
                        .unwrap_or("audio")
                        .to_string()
                });
                Ok(DownloadedItem {
                    name: item_name,
                    path: path.to_string_lossy().to_string(),
                })
            }
            _ => {
                if !status.success() && !stderr_log.trim().is_empty() {
                    Err(format!("ดาวน์โหลดไม่สำเร็จ: {}", stderr_log.trim()))
                } else {
                    Err("ไม่พบไฟล์ที่ดาวน์โหลด".to_string())
                }
            }
        }
    }
}


use crate::models::{PreviewProgressEvent, SoundStateEvent, WaveformData};
use rodio::{buffer::SamplesBuffer, OutputStream, Sink};
use std::collections::HashMap;
use std::fs::File;
use std::path::Path;
use std::sync::mpsc::{self, Sender};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant, SystemTime};
use symphonia::core::audio::SampleBuffer;
use symphonia::core::codecs::DecoderOptions;
use symphonia::core::formats::FormatOptions;
use symphonia::core::io::MediaSourceStream;
use symphonia::core::meta::MetadataOptions;
use symphonia::core::probe::Hint;
use tauri::{AppHandle, Emitter};

pub struct DecodedAudio {
    pub samples: Vec<f32>,
    pub channels: u16,
    pub sample_rate: u32,
    pub duration: f64,
}

type AudioCache = Arc<Mutex<HashMap<String, (SystemTime, Arc<DecodedAudio>)>>>;

pub fn decode_audio_cached(cache: &AudioCache, file_path: &str) -> Result<Arc<DecodedAudio>, String> {
    let path = Path::new(file_path);
    if !path.exists() {
        return Err("ไม่พบไฟล์เสียง".to_string());
    }

    let modified = path
        .metadata()
        .and_then(|m| m.modified())
        .unwrap_or(SystemTime::UNIX_EPOCH);

    if let Ok(guard) = cache.lock() {
        if let Some((cached_time, decoded)) = guard.get(file_path) {
            if *cached_time == modified {
                return Ok(Arc::clone(decoded));
            }
        }
    }

    let decoded = Arc::new(decode_audio_file(path)?);

    if let Ok(mut guard) = cache.lock() {
        if guard.len() >= 24 {
            guard.clear();
        }
        guard.insert(file_path.to_string(), (modified, Arc::clone(&decoded)));
    }

    Ok(decoded)
}

fn decode_audio_file(path: &Path) -> Result<DecodedAudio, String> {
    let file = File::open(path).map_err(|e| format!("ไม่สามารถเปิดไฟล์ได้: {}", e))?;
    let mss = MediaSourceStream::new(Box::new(file), Default::default());

    let mut hint = Hint::new();
    if let Some(ext) = path.extension().and_then(|e| e.to_str()) {
        hint.with_extension(ext);
    }

    let probed = symphonia::default::get_probe()
        .format(
            &hint,
            mss,
            &FormatOptions::default(),
            &MetadataOptions::default(),
        )
        .map_err(|e| format!("รูปแบบไฟล์ไม่รองรับ: {}", e))?;

    let mut format = probed.format;
    let track = format
        .default_track()
        .ok_or_else(|| "ไม่พบแทร็กเสียงในไฟล์".to_string())?;

    let track_id = track.id;
    let codec_params = track.codec_params.clone();

    let mut decoder = symphonia::default::get_codecs()
        .make(&codec_params, &DecoderOptions::default())
        .map_err(|e| format!("ไม่สามารถถอดรหัสเสียงได้: {}", e))?;

    let mut sample_rate = codec_params.sample_rate.unwrap_or(44100);
    let mut channels = codec_params
        .channels
        .map(|c| c.count() as u16)
        .unwrap_or(2)
        .max(1);

    let mut all_samples: Vec<f32> = Vec::new();

    loop {
        let packet = match format.next_packet() {
            Ok(p) => p,
            Err(_) => break,
        };

        if packet.track_id() != track_id {
            continue;
        }

        match decoder.decode(&packet) {
            Ok(audio_buf) => {
                let spec = *audio_buf.spec();
                sample_rate = spec.rate;
                channels = (spec.channels.count() as u16).max(1);

                let mut sample_buf =
                    SampleBuffer::<f32>::new(audio_buf.capacity() as u64, spec);
                sample_buf.copy_interleaved_ref(audio_buf);
                all_samples.extend_from_slice(sample_buf.samples());
            }
            Err(symphonia::core::errors::Error::DecodeError(_)) => continue,
            Err(_) => break,
        }
    }

    if all_samples.is_empty() {
        return Err("ไม่มีข้อมูลเสียงในไฟล์".to_string());
    }

    let total_frames = all_samples.len() / (channels as usize);
    let duration = (total_frames as f64) / (sample_rate as f64);

    Ok(DecodedAudio {
        samples: all_samples,
        channels,
        sample_rate,
        duration,
    })
}

pub fn build_waveform_peaks(decoded: &DecodedAudio) -> WaveformData {
    const TARGET_BINS: usize = 800;
    let ch = decoded.channels as usize;
    let total_frames = decoded.samples.len() / ch;
    if total_frames == 0 {
        return WaveformData {
            peaks: Vec::new(),
            duration: decoded.duration,
        };
    }

    let bins = TARGET_BINS.min(total_frames);
    let frames_per_bin = (total_frames / bins).max(1);
    let mut peaks = Vec::with_capacity(bins);

    for b in 0..bins {
        let start_frame = b * frames_per_bin;
        let end_frame = ((b + 1) * frames_per_bin).min(total_frames);
        let mut max_val: f32 = 0.0;
        for f in start_frame..end_frame {
            let base = f * ch;
            let mut sum = 0.0f32;
            for c in 0..ch {
                sum += decoded.samples[base + c].abs();
            }
            let mono_abs = sum / (ch as f32);
            if mono_abs > max_val {
                max_val = mono_abs;
            }
        }
        peaks.push(max_val.min(1.0));
    }

    WaveformData {
        peaks,
        duration: decoded.duration,
    }
}

/// Applies start/end trim and linear-interpolation speed scaling (matching SpeedSampleProvider.cs).
fn prepare_playback_samples(
    decoded: &DecodedAudio,
    start_time: f64,
    end_time: f64,
    speed_pct: i32,
) -> (Vec<f32>, f64, f64, Duration) {
    let ch = decoded.channels as usize;
    let total_frames = decoded.samples.len() / ch;
    if total_frames == 0 {
        return (Vec::new(), 0.0, 1.0, Duration::ZERO);
    }

    let start_frame = if start_time > 0.0 && start_time < decoded.duration {
        ((start_time * decoded.sample_rate as f64) as usize).min(total_frames.saturating_sub(1))
    } else {
        0
    };

    let end_frame = if end_time > 0.0 && end_time <= decoded.duration {
        ((end_time * decoded.sample_rate as f64) as usize).clamp(start_frame + 1, total_frames)
    } else {
        total_frames
    };

    let start_ratio = (start_frame as f64) / (total_frames as f64);
    let end_ratio = (end_frame as f64) / (total_frames as f64);

    let slice_frames = end_frame.saturating_sub(start_frame);
    if slice_frames == 0 {
        return (Vec::new(), start_ratio, end_ratio, Duration::ZERO);
    }

    let speed = ((speed_pct as f32) / 100.0).clamp(0.25, 4.0);
    let slice = &decoded.samples[start_frame * ch..end_frame * ch];

    if (speed - 1.0).abs() < 0.001 || slice_frames < 2 {
        let secs = (slice_frames as f64) / (decoded.sample_rate as f64);
        return (
            slice.to_vec(),
            start_ratio,
            end_ratio,
            Duration::from_secs_f64(secs),
        );
    }

    let out_frames = (((slice_frames - 1) as f32) / speed).ceil() as usize;
    let mut out = vec![0.0f32; out_frames * ch];

    for i in 0..out_frames {
        let src_idx = (i as f32) * speed;
        let idx0 = (src_idx as usize).min(slice_frames - 1);
        let idx1 = (idx0 + 1).min(slice_frames - 1);
        let frac = src_idx - (idx0 as f32);

        for c in 0..ch {
            let s0 = slice[idx0 * ch + c];
            let s1 = slice[idx1 * ch + c];
            out[i * ch + c] = s0 + (s1 - s0) * frac;
        }
    }

    let secs = (out_frames as f64) / (decoded.sample_rate as f64);
    (
        out,
        start_ratio,
        end_ratio,
        Duration::from_secs_f64(secs),
    )
}

enum AudioCommand {
    PlayPad {
        key: String,
        decoded: Arc<DecodedAudio>,
        volume: i32,
        start_time: f64,
        end_time: f64,
        speed: i32,
    },
    StopPad {
        key: String,
    },
    StopAll,
    SetMasterVolume {
        volume: f32,
    },
    PlayPreview {
        decoded: Arc<DecodedAudio>,
        volume: i32,
        start_time: f64,
        end_time: f64,
        speed: i32,
    },
    StopPreview,
}

struct ActivePadPlayback {
    sink: Sink,
    pad_volume: f32,
}

struct ActivePreviewPlayback {
    sink: Sink,
    started_at: Instant,
    expected_duration: Duration,
    start_ratio: f64,
    end_ratio: f64,
}

#[derive(Clone)]
pub struct AudioService {
    tx: Sender<AudioCommand>,
    cache: AudioCache,
}

impl AudioService {
    pub fn new(app_handle: AppHandle, initial_master_volume: i32) -> Self {
        let (tx, rx) = mpsc::channel::<AudioCommand>();
        let cache: AudioCache = Arc::new(Mutex::new(HashMap::new()));

        std::thread::spawn(move || {
            let stream_res = OutputStream::try_default();
            let (_stream, stream_handle) = match stream_res {
                Ok(pair) => (Some(pair.0), Some(pair.1)),
                Err(e) => {
                    eprintln!("Audio output device init failed: {}", e);
                    (None, None)
                }
            };

            let mut master_vol = (initial_master_volume as f32 / 100.0).clamp(0.0, 1.0);
            let mut active_pads: HashMap<String, ActivePadPlayback> = HashMap::new();
            let mut active_preview: Option<ActivePreviewPlayback> = None;

            loop {
                while let Ok(cmd) = rx.try_recv() {
                    match cmd {
                        AudioCommand::PlayPad {
                            key,
                            decoded,
                            volume,
                            start_time,
                            end_time,
                            speed,
                        } => {
                            if let Some(prev) = active_pads.remove(&key) {
                                prev.sink.stop();
                            }

                            if let Some(ref handle) = stream_handle {
                                let (samples, _, _, _) = prepare_playback_samples(
                                    &decoded, start_time, end_time, speed,
                                );
                                if !samples.is_empty() {
                                    if let Ok(sink) = Sink::try_new(handle) {
                                        let pad_vol = (volume as f32 / 100.0).clamp(0.0, 1.0);
                                        sink.set_volume(pad_vol * master_vol);
                                        let buf = SamplesBuffer::new(
                                            decoded.channels,
                                            decoded.sample_rate,
                                            samples,
                                        );
                                        sink.append(buf);
                                        active_pads.insert(
                                            key.clone(),
                                            ActivePadPlayback {
                                                sink,
                                                pad_volume: pad_vol,
                                            },
                                        );
                                        let _ = app_handle.emit(
                                            "sound-state",
                                            SoundStateEvent { key, playing: true },
                                        );
                                    }
                                }
                            }
                        }
                        AudioCommand::StopPad { key } => {
                            if let Some(active) = active_pads.remove(&key) {
                                active.sink.stop();
                                let _ = app_handle.emit(
                                    "sound-state",
                                    SoundStateEvent {
                                        key,
                                        playing: false,
                                    },
                                );
                            }
                        }
                        AudioCommand::StopAll => {
                            for (k, active) in active_pads.drain() {
                                active.sink.stop();
                                let _ = app_handle.emit(
                                    "sound-state",
                                    SoundStateEvent {
                                        key: k,
                                        playing: false,
                                    },
                                );
                            }
                        }
                        AudioCommand::SetMasterVolume { volume } => {
                            master_vol = volume.clamp(0.0, 1.0);
                            for active in active_pads.values() {
                                active.sink.set_volume(active.pad_volume * master_vol);
                            }
                        }
                        AudioCommand::PlayPreview {
                            decoded,
                            volume,
                            start_time,
                            end_time,
                            speed,
                        } => {
                            if let Some(prev) = active_preview.take() {
                                prev.sink.stop();
                            }
                            if let Some(ref handle) = stream_handle {
                                let (samples, start_ratio, end_ratio, expected_duration) =
                                    prepare_playback_samples(&decoded, start_time, end_time, speed);
                                if !samples.is_empty() {
                                    if let Ok(sink) = Sink::try_new(handle) {
                                        let vol = (volume as f32 / 100.0).clamp(0.0, 1.0);
                                        sink.set_volume(vol);
                                        let buf = SamplesBuffer::new(
                                            decoded.channels,
                                            decoded.sample_rate,
                                            samples,
                                        );
                                        sink.append(buf);
                                        active_preview = Some(ActivePreviewPlayback {
                                            sink,
                                            started_at: Instant::now(),
                                            expected_duration,
                                            start_ratio,
                                            end_ratio,
                                        });
                                    }
                                }
                            }
                        }
                        AudioCommand::StopPreview => {
                            if let Some(prev) = active_preview.take() {
                                prev.sink.stop();
                                let _ = app_handle.emit(
                                    "preview-progress",
                                    PreviewProgressEvent {
                                        ratio: -1.0,
                                        finished: true,
                                    },
                                );
                            }
                        }
                    }
                }

                // Poll active pad completions
                let finished_keys: Vec<String> = active_pads
                    .iter()
                    .filter(|(_, v)| v.sink.empty())
                    .map(|(k, _)| k.clone())
                    .collect();

                for k in finished_keys {
                    active_pads.remove(&k);
                    let _ = app_handle.emit(
                        "sound-state",
                        SoundStateEvent {
                            key: k,
                            playing: false,
                        },
                    );
                }

                // Poll preview progress
                if let Some(ref preview) = active_preview {
                    if preview.sink.empty() {
                        active_preview = None;
                        let _ = app_handle.emit(
                            "preview-progress",
                            PreviewProgressEvent {
                                ratio: -1.0,
                                finished: true,
                            },
                        );
                    } else {
                        let elapsed = preview.started_at.elapsed().as_secs_f64();
                        let total = preview.expected_duration.as_secs_f64().max(0.001);
                        let frac = (elapsed / total).clamp(0.0, 1.0);
                        let current_ratio = preview.start_ratio
                            + frac * (preview.end_ratio - preview.start_ratio);
                        let _ = app_handle.emit(
                            "preview-progress",
                            PreviewProgressEvent {
                                ratio: current_ratio,
                                finished: false,
                            },
                        );
                    }
                }

                std::thread::sleep(Duration::from_millis(40));
            }
        });

        Self { tx, cache }
    }

    pub fn play_sound(
        &self,
        key: String,
        file_path: &str,
        volume: i32,
        start_time: f64,
        end_time: f64,
        speed: i32,
    ) -> Result<bool, String> {
        let decoded = decode_audio_cached(&self.cache, file_path)?;
        self.tx
            .send(AudioCommand::PlayPad {
                key,
                decoded,
                volume,
                start_time,
                end_time,
                speed,
            })
            .map_err(|e| e.to_string())?;
        Ok(true)
    }

    pub fn stop_sound(&self, key: String) {
        let _ = self.tx.send(AudioCommand::StopPad { key });
    }

    pub fn stop_all(&self) {
        let _ = self.tx.send(AudioCommand::StopAll);
    }

    pub fn set_master_volume(&self, volume_pct: i32) {
        let _ = self.tx.send(AudioCommand::SetMasterVolume {
            volume: (volume_pct as f32) / 100.0,
        });
    }

    pub fn get_waveform(&self, file_path: &str) -> Result<WaveformData, String> {
        let decoded = decode_audio_cached(&self.cache, file_path)?;
        Ok(build_waveform_peaks(&decoded))
    }

    pub fn play_preview(
        &self,
        file_path: &str,
        volume: i32,
        start_time: f64,
        end_time: f64,
        speed: i32,
    ) -> Result<(), String> {
        let decoded = decode_audio_cached(&self.cache, file_path)?;
        self.tx
            .send(AudioCommand::PlayPreview {
                decoded,
                volume,
                start_time,
                end_time,
                speed,
            })
            .map_err(|e| e.to_string())
    }

    pub fn stop_preview(&self) {
        let _ = self.tx.send(AudioCommand::StopPreview);
    }
}


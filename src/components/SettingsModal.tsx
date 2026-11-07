import React, { useEffect, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { PreviewProgressEvent, SoundPad, WaveformData } from "../types";
import { WaveformViewer } from "./WaveformViewer";
import { KeyPickerModal } from "./KeyPickerModal";

interface SettingsModalProps {
  pad: SoundPad;
  onSave: (updatedPad: SoundPad, moveToKey?: string) => void;
  onClear: (key: string) => void;
  onClose: () => void;
}

export const SettingsModal: React.FC<SettingsModalProps> = ({
  pad,
  onSave,
  onClear,
  onClose,
}) => {
  const [filePath, setFilePath] = useState(pad.sound_path);
  const [displayName, setDisplayName] = useState(pad.sound_name);
  const [volume, setVolume] = useState(pad.volume);
  const [speed, setSpeed] = useState(pad.speed || 100);

  const [peaks, setPeaks] = useState<number[]>([]);
  const [fileDuration, setFileDuration] = useState(0);
  const [isLoadingWaveform, setIsLoadingWaveform] = useState(false);

  const [startRatio, setStartRatio] = useState(0);
  const [endRatio, setEndRatio] = useState(1);
  const [playbackRatio, setPlaybackRatio] = useState(-1);
  const [isPlayingPreview, setIsPlayingPreview] = useState(false);
  const [playStatus, setPlayStatus] = useState("");
  const [showKeyPicker, setShowKeyPicker] = useState(false);

  // Load waveform whenever filePath changes
  useEffect(() => {
    let cancelled = false;

    const loadWaveform = async (
      path: string,
      savedStart: number,
      savedEnd: number
    ) => {
      const trimmed = path.trim();
      if (!trimmed) {
        setPeaks([]);
        setFileDuration(0);
        setStartRatio(0);
        setEndRatio(1);
        setPlayStatus("");
        return;
      }

      setIsLoadingWaveform(true);
      setPlayStatus("กำลังโหลดไฟล์...");
      try {
        const data = await invoke<WaveformData>("get_waveform_peaks", {
          filePath: trimmed,
        });
        if (cancelled) return;
        setPeaks(data.peaks);
        setFileDuration(data.duration);

        if (data.duration > 0) {
          const sr =
            savedStart >= 0 && savedStart < data.duration
              ? savedStart / data.duration
              : 0.0;
          const er =
            savedEnd > 0 && savedEnd <= data.duration
              ? savedEnd / data.duration
              : 1.0;
          setStartRatio(sr);
          setEndRatio(er);
          setPlayStatus("ลากจุด ◀ ▶ เพื่อกำหนดช่วงเสียง");
        } else {
          setStartRatio(0);
          setEndRatio(1);
          setPlayStatus("");
        }
      } catch {
        if (!cancelled) {
          setPeaks([]);
          setFileDuration(0);
          setStartRatio(0);
          setEndRatio(1);
          setPlayStatus("⚠️ ไม่สามารถอ่านไฟล์เสียงนี้ได้");
        }
      } finally {
        if (!cancelled) setIsLoadingWaveform(false);
      }
    };

    const isInitialFile = filePath.trim() === pad.sound_path.trim();
    loadWaveform(
      filePath,
      isInitialFile ? pad.start_time : -1,
      isInitialFile ? pad.end_time : -1
    );

    return () => {
      cancelled = true;
    };
  }, [filePath, pad.sound_path, pad.start_time, pad.end_time]);

  // Listen for preview progress from Rust audio engine
  useEffect(() => {
    const unlistenPromise = listen<PreviewProgressEvent>(
      "preview-progress",
      (event) => {
        if (event.payload.finished) {
          setPlaybackRatio(-1);
          setIsPlayingPreview(false);
          setPlayStatus("");
        } else {
          setPlaybackRatio(event.payload.ratio);
        }
      }
    );

    return () => {
      unlistenPromise.then((unlisten) => unlisten());
      invoke("stop_preview").catch(() => {});
    };
  }, []);

  const handleBrowse = async () => {
    try {
      const selected = await invoke<string | null>("pick_audio_file");
      if (selected) {
        setFilePath(selected);
        if (!displayName.trim()) {
          const fileName = selected.split(/[\\/]/).pop() || "";
          const dotIdx = fileName.lastIndexOf(".");
          setDisplayName(dotIdx > 0 ? fileName.slice(0, dotIdx) : fileName);
        }
      }
    } catch (err) {
      setPlayStatus(`❌ ${String(err)}`);
    }
  };

  const handlePlayPreview = async () => {
    const trimmed = filePath.trim();
    if (!trimmed) {
      setPlayStatus("⚠️ ไม่พบไฟล์");
      return;
    }

    const startSec = startRatio * fileDuration;
    const endSec = endRatio * fileDuration;

    try {
      setIsPlayingPreview(true);
      setPlayStatus(
        `▶ กำลังเล่น  ${startSec.toFixed(2)}s → ${endSec.toFixed(2)}s`
      );
      await invoke("play_preview", {
        filePath: trimmed,
        volume,
        startTime: startRatio > 0.001 ? startSec : -1,
        endTime: endRatio < 0.999 ? endSec : -1,
        speed,
      });
    } catch (err) {
      setIsPlayingPreview(false);
      setPlaybackRatio(-1);
      setPlayStatus(`❌ ${String(err)}`);
    }
  };

  const handleStopPreview = async () => {
    try {
      await invoke("stop_preview");
    } catch {}
    setIsPlayingPreview(false);
    setPlaybackRatio(-1);
    setPlayStatus("");
  };

  const handleResetTrim = () => {
    setStartRatio(0.0);
    setEndRatio(1.0);
    setPlayStatus("");
  };

  const buildUpdatedPad = (): SoundPad => {
    const trimmedPath = filePath.trim();
    let finalName = displayName.trim();
    if (!finalName && trimmedPath) {
      const fileName = trimmedPath.split(/[\\/]/).pop() || "";
      const dotIdx = fileName.lastIndexOf(".");
      finalName = dotIdx > 0 ? fileName.slice(0, dotIdx) : fileName;
    }

    return {
      key: pad.key,
      sound_path: trimmedPath,
      sound_name: finalName,
      volume,
      speed,
      start_time:
        fileDuration > 0 && startRatio > 0.001
          ? startRatio * fileDuration
          : -1,
      end_time:
        fileDuration > 0 && endRatio < 0.999 ? endRatio * fileDuration : -1,
    };
  };

  const handleSave = async () => {
    await handleStopPreview();
    onSave(buildUpdatedPad());
  };

  const handleClear = async () => {
    await handleStopPreview();
    onClear(pad.key);
  };

  const handleMoveSelect = async (targetKey: string) => {
    setShowKeyPicker(false);
    await handleStopPreview();
    onSave(buildUpdatedPad(), targetKey);
  };

  const isTrimModified = startRatio > 0.001 || endRatio < 0.999;

  return (
    <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/65 backdrop-blur-[2px]">
      <div className="w-[540px] rounded-[10px] bg-[#0d1117] border border-[#30363d] shadow-2xl flex flex-col overflow-hidden">
        {/* Title Bar */}
        <div className="bg-[#161b22] px-4 py-2.5 flex items-center justify-between border-b border-[#30363d]">
          <span className="text-[#e94560] text-[16px] font-bold">
            ⚙ ตั้งค่าปุ่ม [ {pad.key} ]
          </span>
          <button
            onClick={async () => {
              await handleStopPreview();
              onClose();
            }}
            className="w-7 h-7 rounded text-[#8b949e] hover:text-[#f85149] hover:bg-white/10 flex items-center justify-center"
          >
            ✕
          </button>
        </div>

        {/* Body */}
        <div className="px-5 pt-3.5 pb-2 space-y-3 overflow-y-auto max-h-[440px]">
          {/* File picker row */}
          <div>
            <label className="block text-[#c9d1d9] text-[13px] font-semibold mb-1">
              ไฟล์เสียง:
            </label>
            <div className="flex items-center space-x-1">
              <input
                type="text"
                value={filePath}
                onChange={(e) => setFilePath(e.target.value)}
                placeholder="เลือกไฟล์เสียง..."
                className="flex-1 bg-[#161b22] text-[#c9d1d9] border border-[#30363d] rounded px-2 py-1 text-[13px] focus:outline-none focus:border-[#58a6ff]"
              />
              <button
                onClick={handleBrowse}
                title="เลือกไฟล์เสียง"
                className="w-[34px] h-[28px] bg-[#30363d] hover:bg-[#3d444d] text-[#c9d1d9] border border-[#484f58] rounded flex items-center justify-center text-sm"
              >
                📂
              </button>
            </div>
          </div>

          {/* Waveform + Trim labels */}
          <div>
            <div className="rounded-[8px] overflow-hidden border border-[#30363d]">
              <WaveformViewer
                peaks={peaks}
                duration={fileDuration}
                isLoading={isLoadingWaveform}
                startRatio={startRatio}
                endRatio={endRatio}
                playbackRatio={playbackRatio}
                onTrimChange={(sr, er) => {
                  setStartRatio(sr);
                  setEndRatio(er);
                }}
              />
            </div>

            <div className="grid grid-cols-3 items-center mt-1.5 text-[12px]">
              <div className="text-[#ffd700] font-mono">
                ◀ {(startRatio * fileDuration).toFixed(2)} s
              </div>
              <div className="text-center text-[#484f58] text-[13px]">
                {fileDuration > 0
                  ? `⏱ ${fileDuration.toFixed(2)} s`
                  : "— ไม่มีไฟล์ —"}
              </div>
              <div className="text-right text-[#ffd700] font-mono">
                {(endRatio * fileDuration).toFixed(2)} s ▶
              </div>
            </div>

            {/* Playback controls + Reset Trim */}
            <div className="flex items-center mt-1.5">
              <button
                onClick={handlePlayPreview}
                disabled={isPlayingPreview}
                title="เล่นเสียงตามจุด Trim ที่กำหนด"
                className="w-[36px] h-[36px] rounded bg-[#238636] hover:bg-[#2ea043] disabled:opacity-50 text-white flex items-center justify-center text-[15px]"
              >
                ▶
              </button>
              <button
                onClick={handleStopPreview}
                title="หยุดเสียง"
                className="w-[36px] h-[36px] ml-1.5 rounded bg-[#30363d] hover:bg-[#3d444d] text-[#c9d1d9] flex items-center justify-center text-[14px]"
              >
                ⏹
              </button>

              <span className="flex-1 ml-2.5 text-[#8b949e] text-[13px] truncate">
                {playStatus}
              </span>

              {isTrimModified && fileDuration > 0 && (
                <button
                  onClick={handleResetTrim}
                  className="h-[28px] px-2 text-[11px] font-semibold text-[#8b949e] hover:text-[#c9d1d9] border border-[#30363d] hover:border-[#484f58] rounded"
                >
                  ↺ รีเซ็ต Trim
                </button>
              )}
            </div>
          </div>

          {/* Display Name */}
          <div>
            <label className="block text-[#c9d1d9] text-[13px] font-semibold mb-1">
              ชื่อแสดง:
            </label>
            <input
              type="text"
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              placeholder="ชื่อปุ่มเสียง..."
              className="w-full bg-[#161b22] text-[#c9d1d9] border border-[#30363d] rounded px-2 py-1 text-[13px] focus:outline-none focus:border-[#58a6ff]"
            />
          </div>

          {/* Volume */}
          <div>
            <label className="block text-[#c9d1d9] text-[13px] font-semibold mb-1">
              Volume:
            </label>
            <div className="flex items-center space-x-3">
              <input
                type="range"
                min={0}
                max={100}
                step={1}
                value={volume}
                onChange={(e) => setVolume(Number(e.target.value))}
                className="flex-1"
              />
              <span className="w-[44px] text-right font-mono text-[13px] font-bold text-[#c9d1d9]">
                {volume}%
              </span>
            </div>
          </div>

          {/* Speed */}
          <div>
            <label className="block text-[#c9d1d9] text-[13px] font-semibold mb-1">
              ความเร็ว:
            </label>
            <div className="flex items-center space-x-3">
              <input
                type="range"
                min={25}
                max={400}
                step={5}
                value={speed}
                onChange={(e) => setSpeed(Number(e.target.value))}
                className="flex-1"
              />
              <span className="w-[52px] text-right font-mono text-[13px] font-bold text-[#c9d1d9]">
                {speed}%
              </span>
            </div>
          </div>
        </div>

        {/* Action Buttons */}
        <div className="px-5 py-3.5 grid grid-cols-3 gap-2">
          <button
            onClick={handleSave}
            className="h-[42px] rounded font-bold text-[14px] text-white bg-gradient-to-br from-[#238636] to-[#1a7f37] hover:opacity-95"
          >
            💾 บันทึก
          </button>
          <button
            onClick={() => setShowKeyPicker(true)}
            className="h-[42px] rounded font-bold text-[14px] text-white bg-gradient-to-br from-[#58a6ff] to-[#1a7fbf] hover:opacity-95"
          >
            📦 ย้าย
          </button>
          <button
            onClick={handleClear}
            className="h-[42px] rounded font-bold text-[14px] text-white bg-gradient-to-br from-[#da3633] to-[#b92d2d] hover:opacity-95"
          >
            🗑 ล้าง
          </button>
        </div>
      </div>

      {showKeyPicker && (
        <KeyPickerModal
          currentKey={pad.key}
          onSelectKey={handleMoveSelect}
          onClose={() => setShowKeyPicker(false)}
        />
      )}
    </div>
  );
};


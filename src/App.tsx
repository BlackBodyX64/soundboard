import React, { useCallback, useEffect, useRef, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { getCurrentWebview } from "@tauri-apps/api/webview";
import {
  createEmptyPad,
  DownloadedItem,
  DownloadProgressEvent,
  FKEY_ROW,
  getAllKeys,
  getColorsForKey,
  isAudioFile,
  KB_OFFSETS,
  KB_ROWS,
  NUMPAD_ROWS,
  PreviewProgressEvent,
  SECTION_COLORS,
  SoundboardConfig,
  SoundPad,
  SoundStateEvent,
} from "./types";
import { KeyButton } from "./components/KeyButton";
import { SettingsModal } from "./components/SettingsModal";

export const App: React.FC = () => {
  const [pads, setPads] = useState<Record<string, SoundPad>>(() => {
    const initial: Record<string, SoundPad> = {};
    for (const k of getAllKeys()) {
      initial[k] = createEmptyPad(k);
    }
    return initial;
  });

  const [masterVolume, setMasterVolume] = useState<number>(80);
  const [downloadDirectory, setDownloadDirectory] = useState<string>("");
  const [hotkeysEnabled, setHotkeysEnabled] = useState<boolean>(true);
  const [isInputFocused, setIsInputFocused] = useState<boolean>(false);

  const [playingKeys, setPlayingKeys] = useState<Record<string, boolean>>({});
  const [editingKey, setEditingKey] = useState<string | null>(null);
  const [osHoveredKey, setOsHoveredKey] = useState<string | null>(null);

  // Downloader state
  const [urlInput, setUrlInput] = useState<string>("");
  const [isDownloading, setIsDownloading] = useState<boolean>(false);
  const [downloadStatus, setDownloadStatus] = useState<{
    message: string;
    color: string;
    percentage: number;
    visible: boolean;
  }>({
    message: "",
    color: "#8b949e",
    percentage: -1,
    visible: false,
  });
  const [downloadedItems, setDownloadedItems] = useState<DownloadedItem[]>([]);
  const [sidebarPreviewPath, setSidebarPreviewPath] = useState<string | null>(
    null
  );

  const padsRef = useRef(pads);
  padsRef.current = pads;
  const masterVolRef = useRef(masterVolume);
  masterVolRef.current = masterVolume;
  const downloadDirRef = useRef(downloadDirectory);
  downloadDirRef.current = downloadDirectory;

  // Save config helper
  const persistConfig = useCallback(
    async (
      nextPads: Record<string, SoundPad>,
      nextMasterVol: number,
      nextDownloadDir: string
    ) => {
      const cfg: SoundboardConfig = {
        master_volume: nextMasterVol,
        download_directory: nextDownloadDir,
        pads: Object.values(nextPads),
      };
      try {
        await invoke("save_config", { config: cfg });
      } catch (err) {
        console.error("save_config failed:", err);
      }
    },
    []
  );

  // Sync hotkeys with backend whenever pads, toggle, or modal/input focus changes
  useEffect(() => {
    const activeKeys = Object.values(pads)
      .filter((p) => Boolean(p.sound_path && p.sound_path.trim().length > 0))
      .map((p) => p.key);

    const shouldBeActive =
      hotkeysEnabled && editingKey === null && !isInputFocused;

    invoke("sync_hotkeys", {
      registeredKeys: activeKeys,
      active: shouldBeActive,
    }).catch((err) => console.error("sync_hotkeys error:", err));
  }, [pads, hotkeysEnabled, editingKey, isInputFocused]);

  // Initial load of config & existing downloaded files
  useEffect(() => {
    const init = async () => {
      try {
        const cfg = await invoke<SoundboardConfig>("load_config");
        setMasterVolume(cfg.master_volume);
        setDownloadDirectory(cfg.download_directory);

        const merged: Record<string, SoundPad> = {};
        for (const k of getAllKeys()) {
          merged[k] = createEmptyPad(k);
        }
        for (const p of cfg.pads) {
          if (p.key && merged[p.key]) {
            merged[p.key] = {
              ...merged[p.key],
              ...p,
            };
          }
        }
        setPads(merged);

        const files = await invoke<DownloadedItem[]>("list_downloaded_files", {
          directory: cfg.download_directory,
        });
        setDownloadedItems(files);
      } catch (err) {
        console.error("Failed to initialize config:", err);
      }
    };
    init();
  }, []);

  // Play a pad's sound
  const handlePlayPad = useCallback(async (key: string) => {
    const pad = padsRef.current[key];
    if (!pad || !pad.sound_path || !pad.sound_path.trim()) return;

    setPlayingKeys((prev) => ({ ...prev, [key]: true }));
    try {
      const ok = await invoke<boolean>("play_sound", {
        key,
        filePath: pad.sound_path,
        volume: pad.volume,
        startTime: pad.start_time,
        endTime: pad.end_time,
        speed: pad.speed || 100,
      });
      if (!ok) {
        setPlayingKeys((prev) => ({ ...prev, [key]: false }));
      }
    } catch {
      setPlayingKeys((prev) => ({ ...prev, [key]: false }));
    }
  }, []);

  // Stop all sounds
  const handleStopAll = useCallback(async () => {
    try {
      await invoke("stop_all_sounds");
      await invoke("stop_preview");
    } catch {}
    setPlayingKeys({});
    setSidebarPreviewPath(null);
  }, []);

  // Assign file dropped onto a key
  const handleFileDropped = useCallback(
    (key: string, filePath: string) => {
      if (!isAudioFile(filePath)) return;
      const fileName = filePath.split(/[\\/]/).pop() || "";
      const dotIdx = fileName.lastIndexOf(".");
      const soundName = dotIdx > 0 ? fileName.slice(0, dotIdx) : fileName;

      setPads((prev) => {
        const current = prev[key] || createEmptyPad(key);
        const next = {
          ...prev,
          [key]: {
            ...current,
            key,
            sound_path: filePath,
            sound_name: soundName,
          },
        };
        persistConfig(next, masterVolRef.current, downloadDirRef.current);
        return next;
      });
    },
    [persistConfig]
  );

  // Swap/move pad via drag-and-drop between two keys
  const handlePadSwapped = useCallback(
    (fromKey: string, toKey: string) => {
      if (fromKey === toKey) return;
      setPads((prev) => {
        const sourcePad = prev[fromKey] || createEmptyPad(fromKey);
        const targetPad = prev[toKey] || createEmptyPad(toKey);
        const next = {
          ...prev,
          [toKey]: { ...sourcePad, key: toKey },
          [fromKey]: { ...targetPad, key: fromKey },
        };
        persistConfig(next, masterVolRef.current, downloadDirRef.current);
        return next;
      });
    },
    [persistConfig]
  );

  // Listen to backend events & OS drag-drop
  useEffect(() => {
    const unlistenHotkey = listen<string>("hotkey-triggered", (e) => {
      handlePlayPad(e.payload);
    });

    const unlistenStop = listen("global-stop", () => {
      handleStopAll();
    });

    const unlistenSoundState = listen<SoundStateEvent>("sound-state", (e) => {
      setPlayingKeys((prev) => ({
        ...prev,
        [e.payload.key]: e.payload.playing,
      }));
    });

    const unlistenDownloadProgress = listen<DownloadProgressEvent>(
      "download-progress",
      (e) => {
        setDownloadStatus({
          message: e.payload.message,
          color: "#58a6ff",
          percentage: e.payload.percentage,
          visible: true,
        });
      }
    );

    const unlistenPreview = listen<PreviewProgressEvent>(
      "preview-progress",
      (e) => {
        if (e.payload.finished) {
          setSidebarPreviewPath(null);
        }
      }
    );

    // Tauri v2 OS file drag-and-drop handler
    const findKeyAtPosition = (physX: number, physY: number): string | null => {
      const dpr = window.devicePixelRatio || 1;
      // Try logical coordinates first, then raw physical coordinates as fallback
      const candidates = [
        [physX / dpr, physY / dpr],
        [physX, physY],
      ];
      for (const [cx, cy] of candidates) {
        const el = document.elementFromPoint(cx, cy);
        const keyEl = el?.closest("[data-key-id]") as HTMLElement | null;
        if (keyEl && keyEl.dataset.keyId) {
          return keyEl.dataset.keyId;
        }
      }
      return null;
    };

    const unlistenDragDrop = getCurrentWebview().onDragDropEvent((event) => {
      if (event.payload.type === "over" || event.payload.type === "enter") {
        const pos = event.payload.position;
        const hovered = findKeyAtPosition(pos.x, pos.y);
        setOsHoveredKey(hovered);
      } else if (event.payload.type === "leave") {
        setOsHoveredKey(null);
      } else if (event.payload.type === "drop") {
        setOsHoveredKey(null);
        const pos = event.payload.position;
        const targetKey = findKeyAtPosition(pos.x, pos.y);
        if (targetKey && event.payload.paths.length > 0) {
          const validFile = event.payload.paths.find((p) => isAudioFile(p));
          if (validFile) {
            handleFileDropped(targetKey, validFile);
          }
        }
      }
    });

    // Window keydown listener for ESC / Backspace when focused
    const onWindowKeyDown = (e: KeyboardEvent) => {
      const activeTag = document.activeElement?.tagName.toLowerCase();
      const inInput = activeTag === "input" || activeTag === "textarea";
      if (e.key === "Escape" && editingKey === null) {
        e.preventDefault();
        handleStopAll();
      } else if (e.key === "Backspace" && !inInput && editingKey === null) {
        e.preventDefault();
        handleStopAll();
      }
    };
    window.addEventListener("keydown", onWindowKeyDown);

    return () => {
      unlistenHotkey.then((f) => f());
      unlistenStop.then((f) => f());
      unlistenSoundState.then((f) => f());
      unlistenDownloadProgress.then((f) => f());
      unlistenPreview.then((f) => f());
      unlistenDragDrop.then((f) => f());
      window.removeEventListener("keydown", onWindowKeyDown);
    };
  }, [handlePlayPad, handleStopAll, handleFileDropped, editingKey]);

  // Master Volume change
  const handleMasterVolumeChange = (vol: number) => {
    setMasterVolume(vol);
    invoke("set_master_volume", { volume: vol }).catch(() => {});
    persistConfig(pads, vol, downloadDirectory);
  };

  // Settings Modal handlers
  const handleSavePadSettings = (updatedPad: SoundPad, moveToKey?: string) => {
    const oldKey = editingKey;
    setEditingKey(null);
    if (!oldKey) return;

    setPads((prev) => {
      const next = { ...prev };
      if (moveToKey && moveToKey.trim().length > 0) {
        const targetKey = moveToKey.trim();
        next[targetKey] = { ...updatedPad, key: targetKey };
        if (targetKey !== oldKey) {
          next[oldKey] = createEmptyPad(oldKey);
        }
      } else {
        next[oldKey] = { ...updatedPad, key: oldKey };
      }
      persistConfig(next, masterVolume, downloadDirectory);
      return next;
    });
  };

  const handleClearPad = (key: string) => {
    setEditingKey(null);
    setPads((prev) => {
      const next = {
        ...prev,
        [key]: createEmptyPad(key),
      };
      persistConfig(next, masterVolume, downloadDirectory);
      return next;
    });
  };

  // URL box focus auto-paste
  const handleUrlFocus = async () => {
    setIsInputFocused(true);
    if (urlInput.trim().length > 0) return;
    try {
      const text = await navigator.clipboard.readText();
      const trimmed = text.trim();
      const lower = trimmed.toLowerCase();
      if (
        lower.includes("youtube.com") ||
        lower.includes("youtu.be") ||
        lower.includes("tiktok.com")
      ) {
        setUrlInput(trimmed);
      }
    } catch {}
  };

  // Download or cancel button
  const handleDownloadClick = async () => {
    if (isDownloading) {
      try {
        await invoke("cancel_download");
      } catch {}
      setIsDownloading(false);
      setDownloadStatus({
        message: "🚫 ยกเลิกการดาวน์โหลดแล้ว",
        color: "#8b949e",
        percentage: -1,
        visible: true,
      });
      return;
    }

    const trimmed = urlInput.trim();
    const lower = trimmed.toLowerCase();
    const isSupported =
      lower.includes("youtube.com") ||
      lower.includes("youtu.be") ||
      lower.includes("tiktok.com");

    if (!trimmed || !isSupported) {
      setDownloadStatus({
        message: "⚠️ URL ไม่ถูกต้อง (รองรับ Youtube/Tiktok)",
        color: "#f0883e",
        percentage: -1,
        visible: true,
      });
      return;
    }

    setIsDownloading(true);
    setDownloadStatus({
      message: "กำลังดึงข้อมูล...",
      color: "#58a6ff",
      percentage: 0,
      visible: true,
    });

    try {
      const item = await invoke<DownloadedItem>("download_audio", {
        url: trimmed,
        downloadDir: downloadDirectory,
      });
      setDownloadStatus({
        message: "✅ ดาวน์โหลดสำเร็จ!",
        color: "#3fb950",
        percentage: 100,
        visible: true,
      });
      setDownloadedItems((prev) => [
        item,
        ...prev.filter((x) => x.path !== item.path),
      ]);
      setUrlInput("");
    } catch (err) {
      const msg = String(err);
      if (msg.includes("ยกเลิก")) {
        setDownloadStatus({
          message: "🚫 ยกเลิกการดาวน์โหลดแล้ว",
          color: "#8b949e",
          percentage: -1,
          visible: true,
        });
      } else {
        setDownloadStatus({
          message: `❌ ${msg}`,
          color: "#f85149",
          percentage: -1,
          visible: true,
        });
      }
    } finally {
      setIsDownloading(false);
    }
  };

  const handleChooseDownloadFolder = async () => {
    try {
      const folder = await invoke<string | null>("pick_download_folder", {
        initialDir: downloadDirectory,
      });
      if (folder) {
        setDownloadDirectory(folder);
        persistConfig(pads, masterVolume, folder);
        setDownloadStatus({
          message: `บันทึกที่: ${folder}`,
          color: "#3fb950",
          percentage: -1,
          visible: true,
        });
        const files = await invoke<DownloadedItem[]>("list_downloaded_files", {
          directory: folder,
        });
        setDownloadedItems(files);
      }
    } catch (err) {
      console.error("pick_download_folder error:", err);
    }
  };

  const handleToggleSidebarPreview = async (itemPath: string) => {
    if (sidebarPreviewPath === itemPath) {
      await invoke("stop_preview").catch(() => {});
      setSidebarPreviewPath(null);
      return;
    }
    try {
      setSidebarPreviewPath(itemPath);
      await invoke("play_preview", {
        filePath: itemPath,
        volume: 100,
        startTime: -1,
        endTime: -1,
        speed: 100,
      });
    } catch {
      setSidebarPreviewPath(null);
    }
  };

  const appWindow = getCurrentWindow();

  return (
    <div className="w-screen h-screen p-[6px] bg-transparent flex flex-col">
      <div
        style={{
          background:
            "linear-gradient(135deg, #0A0E14 0%, #131822 50%, #1A202C 100%)",
        }}
        className="flex-1 rounded-[12px] border border-white/10 shadow-[0_8px_24px_rgba(0,0,0,0.65)] flex flex-col overflow-hidden"
      >
        {/* ===== Row 0: Title Bar (40px) ===== */}
        <div
          data-tauri-drag-region
          onDoubleClick={() => appWindow.toggleMaximize()}
          className="h-[40px] bg-black/40 border-b border-white/5 flex items-center justify-between px-4 shrink-0"
        >
          <div
            data-tauri-drag-region
            className="flex items-center space-x-2.5 pointer-events-none"
          >
            <span className="text-[18px]">🎵</span>
            <span className="text-[15px] font-bold text-[#e6edf3]">
              TikTok Soundboard V2.3.0
            </span>
            <span className="text-[11px] font-semibold px-1.5 py-0.5 rounded bg-[#e94560]/20 text-[#ff6b81] border border-[#e94560]/40">
              Rust
            </span>
          </div>

          <div className="flex items-center space-x-1">
            <button
              onClick={() => appWindow.minimize()}
              className="w-[42px] h-[32px] rounded text-[#8b949e] hover:bg-white/10 hover:text-[#f0f6fc] flex items-center justify-center transition-colors"
            >
              ─
            </button>
            <button
              onClick={() => appWindow.toggleMaximize()}
              className="w-[42px] h-[32px] rounded text-[#8b949e] hover:bg-white/10 hover:text-[#f0f6fc] flex items-center justify-center transition-colors"
            >
              ☐
            </button>
            <button
              onClick={() => appWindow.close()}
              className="w-[42px] h-[32px] rounded text-[#8b949e] hover:bg-[#E81123] hover:text-white flex items-center justify-center transition-colors"
            >
              ✕
            </button>
          </div>
        </div>

        {/* ===== Row 1: Header (50px) ===== */}
        <div className="h-[50px] bg-white/[0.08] border-b border-white/[0.04] px-5 flex items-center justify-between shrink-0">
          {/* Stop All Button */}
          <button
            onClick={handleStopAll}
            className="px-4 py-2 rounded-[6px] font-bold text-[13px] text-white bg-gradient-to-br from-[#da3633] to-[#b92d2d] hover:from-[#ed4643] hover:to-[#d13838] active:bg-[#b92d2d] shadow-[0_0_10px_rgba(218,54,51,0.4)] flex items-center space-x-1.5 cursor-pointer transition-all"
          >
            <span>⏹</span>
            <span>STOP ALL AUDIO (ESC)</span>
          </button>

          {/* Global Hotkeys Toggle Switch */}
          <div
            onClick={() => setHotkeysEnabled((v) => !v)}
            className="flex items-center space-x-2.5 cursor-pointer"
          >
            <span className="text-[#c9d1d9] text-[15px] font-semibold">
              Global Hotkeys
            </span>
            <div
              className={`w-[44px] h-[24px] rounded-full border-[1.5px] flex items-center px-[2px] transition-colors duration-200 ${
                hotkeysEnabled
                  ? "bg-[#e94560] border-[#e94560]"
                  : "bg-[#21262d] border-[#30363d]"
              }`}
            >
              <div
                className={`w-[16px] h-[16px] rounded-full shadow-md transform transition-transform duration-200 ${
                  hotkeysEnabled
                    ? "translate-x-[21px] bg-white"
                    : "translate-x-0 bg-[#8b949e]"
                }`}
              />
            </div>
          </div>
        </div>

        {/* ===== Row 2: Volume Bar (46px) ===== */}
        <div className="h-[46px] bg-white/[0.04] border-b border-white/[0.04] px-5 flex items-center justify-between shrink-0">
          <div className="flex items-center">
            <span className="text-[18px] text-[#8b949e] mr-3 drop-shadow-[0_0_8px_rgba(139,148,158,0.3)]">
              🔊
            </span>
            <input
              type="range"
              min={0}
              max={100}
              step={1}
              value={masterVolume}
              onChange={(e) => handleMasterVolumeChange(Number(e.target.value))}
              className="w-[200px]"
            />
            <span className="ml-3 w-[50px] text-right font-mono text-[16px] font-bold text-[#e94560]">
              {masterVolume}%
            </span>
          </div>

          <span className="text-[#7d8590] text-[13px] font-medium truncate ml-4">
            ✨ 🎧 ลากไฟล์เสียงวางบนปุ่ม  |  คลิกขวาตั้งค่า  |  📥 ดาวน์โหลดจาก YouTube/TikTok
          </span>
        </div>

        {/* ===== Row 3: Main Content ===== */}
        <div className="flex-1 p-3.5 flex space-x-3 overflow-hidden">
          {/* Left: Keyboard */}
          <div className="flex-1 overflow-auto pr-1">
            {/* Function Keys */}
            <div className="text-[#484f58] text-[11px] font-bold ml-2.5 mt-1.5 mb-1">
              FUNCTION KEYS
            </div>
            <div className="flex flex-row ml-[5px] my-[2px]">
              {FKEY_ROW.map((k) => (
                <KeyButton
                  key={k}
                  keyId={k}
                  displayText={k}
                  pad={pads[k] || createEmptyPad(k)}
                  colors={SECTION_COLORS.fkey}
                  width={70}
                  height={52}
                  isPlaying={Boolean(playingKeys[k])}
                  isOsDragHovered={osHoveredKey === k}
                  onPlay={handlePlayPad}
                  onOpenSettings={setEditingKey}
                  onFileDropped={handleFileDropped}
                  onPadSwapped={handlePadSwapped}
                />
              ))}
            </div>

            {/* Divider */}
            <div className="h-[1px] mx-2.5 my-1.5 bg-gradient-to-r from-transparent via-[#21262d] to-[#21262d]" />

            {/* Main Keyboard */}
            <div className="text-[#484f58] text-[11px] font-bold ml-2.5 mt-1.5 mb-1">
              MAIN KEYBOARD
            </div>
            {KB_ROWS.map((row, rowIdx) => (
              <div
                key={rowIdx}
                style={{ marginLeft: `${KB_OFFSETS[rowIdx] + 5}px` }}
                className="flex flex-row my-[2px]"
              >
                {row.map((k) => (
                  <KeyButton
                    key={k}
                    keyId={k}
                    displayText={k}
                    pad={pads[k] || createEmptyPad(k)}
                    colors={SECTION_COLORS[`row${rowIdx}`]}
                    width={72}
                    height={68}
                    isPlaying={Boolean(playingKeys[k])}
                    isOsDragHovered={osHoveredKey === k}
                    onPlay={handlePlayPad}
                    onOpenSettings={setEditingKey}
                    onFileDropped={handleFileDropped}
                    onPadSwapped={handlePadSwapped}
                  />
                ))}
              </div>
            ))}
          </div>

          {/* Middle: Numpad (270px) */}
          <div className="w-[270px] shrink-0 bg-white/[0.04] rounded-[12px] border border-white/[0.06] p-2.5 flex flex-col items-center overflow-hidden">
            <div className="text-[#6e7681] text-[16px] font-bold mt-2 mb-3">
              NUMPAD
            </div>
            <div className="flex flex-col items-center justify-center">
              {NUMPAD_ROWS.map((row, idx) => (
                <div key={idx} className="flex flex-row my-[2px]">
                  {row.map((k) => (
                    <KeyButton
                      key={k}
                      keyId={k}
                      displayText={k.replace("Num", "")}
                      pad={pads[k] || createEmptyPad(k)}
                      colors={SECTION_COLORS.numpad}
                      width={72}
                      height={68}
                      isPlaying={Boolean(playingKeys[k])}
                      isOsDragHovered={osHoveredKey === k}
                      onPlay={handlePlayPad}
                      onOpenSettings={setEditingKey}
                      onFileDropped={handleFileDropped}
                      onPadSwapped={handlePadSwapped}
                    />
                  ))}
                </div>
              ))}
            </div>
          </div>

          {/* Right: Downloader (260px) */}
          <div className="w-[260px] shrink-0 bg-white/[0.04] rounded-[12px] border border-white/[0.06] p-3 flex flex-col overflow-hidden">
            {/* Header */}
            <div className="relative flex items-center justify-center mb-2.5">
              <span className="text-[#6e7681] text-[16px] font-bold">
                ดาวน์โหลด
              </span>
              <button
                onClick={handleChooseDownloadFolder}
                title={`เลือกโฟลเดอร์สำหรับบันทึก (${downloadDirectory})`}
                className="absolute right-0 w-6 h-6 flex items-center justify-center text-[#8b949e] hover:text-[#58a6ff] transition-colors"
              >
                📁
              </button>
            </div>

            {/* Instructions Step-by-step */}
            <div className="mx-1 mb-3.5 rounded-[6px] bg-[#58a6ff]/[0.07] border border-[#58a6ff]/20 px-2.5 py-2 leading-[1.4]">
              <div className="text-[#e6edf3] text-[13px] font-bold mb-1">
                📌 วิธีใช้งาน:
              </div>
              <div className="text-[#8b949e] text-[12px] ml-1 mb-0.5">
                1. คัดลอก URL (Youtube หรือ Tiktok)
              </div>
              <div className="text-[#8b949e] text-[12px] ml-1 mb-0.5">
                2. วางลงช่องด้านล่าง แล้วกด 📥 โหลด
              </div>
              <div className="text-[#e6edf3] text-[12px] font-semibold ml-1 mt-1">
                3. ลากไฟล์เสียงไปวางทับปุ่มที่ต้องการ
              </div>
            </div>

            {/* URL Input & Download Button */}
            <div className="flex items-center space-x-1 mb-2.5">
              <input
                type="text"
                value={urlInput}
                disabled={isDownloading}
                onFocus={handleUrlFocus}
                onBlur={() => setIsInputFocused(false)}
                onChange={(e) => setUrlInput(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter" && !isDownloading) {
                    handleDownloadClick();
                  }
                }}
                placeholder="ใส่ URL Youtube / Tiktok"
                title="ใส่ URL Youtube / Tiktok"
                className="flex-1 h-[30px] bg-[#161b22] text-[#c9d1d9] border border-[#30363d] rounded px-2 text-[12px] focus:outline-none focus:border-[#58a6ff] disabled:opacity-50"
              />
              <button
                onClick={handleDownloadClick}
                className={`w-[30px] h-[30px] rounded text-white flex items-center justify-center transition-opacity hover:opacity-90 ${
                  isDownloading ? "bg-[#da3633]" : "bg-[#238636]"
                }`}
              >
                {isDownloading ? "❌" : "📥"}
              </button>
            </div>

            {/* Status / Progress */}
            {downloadStatus.visible && (
              <div className="mb-2.5">
                {isDownloading && (
                  <div className="w-full h-[4px] bg-[#21262d] rounded overflow-hidden mb-1">
                    <div
                      style={{
                        width:
                          downloadStatus.percentage > 0
                            ? `${Math.min(100, downloadStatus.percentage)}%`
                            : "100%",
                      }}
                      className={`h-full bg-[#58a6ff] transition-all duration-200 ${
                        downloadStatus.percentage <= 0 ? "animate-pulse" : ""
                      }`}
                    />
                  </div>
                )}
                <div
                  style={{ color: downloadStatus.color }}
                  className="text-[12px] break-words leading-snug"
                >
                  {downloadStatus.message}
                </div>
              </div>
            )}

            {/* List of Downloads */}
            <div className="flex-1 overflow-y-auto space-y-1.5 pr-0.5">
              {downloadedItems.map((item) => {
                const isPreviewing = sidebarPreviewPath === item.path;
                return (
                  <div
                    key={item.path}
                    draggable
                    onDragStart={(e) => {
                      e.dataTransfer.setData(
                        "application/x-soundboard-file",
                        item.path
                      );
                      e.dataTransfer.effectAllowed = "copy";
                    }}
                    title="ลากไปวางที่ปุ่มเสียง"
                    className="bg-[#161b22] hover:bg-[#1c222b] border border-[#30363d] rounded-[6px] px-2 py-1.5 flex items-center justify-between cursor-grab active:cursor-grabbing group"
                  >
                    <div className="flex items-center min-w-0 flex-1">
                      <span className="text-[#8b949e] font-bold mr-1.5">≡</span>
                      <span className="text-[#c9d1d9] text-[13px] truncate">
                        {item.name}
                      </span>
                    </div>
                    <button
                      onClick={(e) => {
                        e.stopPropagation();
                        handleToggleSidebarPreview(item.path);
                      }}
                      title={isPreviewing ? "หยุดฟังตัวอย่าง" : "ฟังตัวอย่าง"}
                      className={`ml-1.5 w-6 h-6 rounded flex items-center justify-center text-[11px] shrink-0 transition-colors ${
                        isPreviewing
                          ? "bg-[#e94560] text-white"
                          : "bg-[#21262d] text-[#8b949e] hover:text-white hover:bg-[#30363d]"
                      }`}
                    >
                      {isPreviewing ? "⏹" : "▶"}
                    </button>
                  </div>
                );
              })}
            </div>
          </div>
        </div>
      </div>

      {/* Settings Modal */}
      {editingKey && (
        <SettingsModal
          pad={pads[editingKey] || createEmptyPad(editingKey)}
          onSave={handleSavePadSettings}
          onClear={handleClearPad}
          onClose={() => setEditingKey(null)}
        />
      )}
    </div>
  );
};


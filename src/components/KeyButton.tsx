import React, { useState } from "react";
import { ColorPair, SoundPad } from "../types";

interface KeyButtonProps {
  keyId: string;
  displayText: string;
  pad: SoundPad;
  colors: ColorPair;
  width?: number;
  height?: number;
  isPlaying: boolean;
  isOsDragHovered?: boolean;
  onPlay: (keyId: string) => void;
  onOpenSettings: (keyId: string) => void;
  onFileDropped: (keyId: string, filePath: string) => void;
  onPadSwapped: (fromKey: string, toKey: string) => void;
}

export const KeyButton: React.FC<KeyButtonProps> = ({
  keyId,
  displayText,
  pad,
  colors,
  width = 72,
  height = 68,
  isPlaying,
  isOsDragHovered = false,
  onPlay,
  onOpenSettings,
  onFileDropped,
  onPadSwapped,
}) => {
  const [isDragOver, setIsDragOver] = useState(false);

  const hasSound = Boolean(pad.sound_path && pad.sound_path.trim().length > 0);
  const rawName = pad.sound_name || "";
  const shortName = rawName.length > 7 ? rawName.slice(0, 6) + "…" : rawName;

  const isHighlighted = isDragOver || isOsDragHovered;

  const borderColor = isPlaying
    ? "#ffffff"
    : isHighlighted
    ? "#58a6ff"
    : hasSound
    ? colors.accent
    : "#2d333b";

  const backgroundColor = isHighlighted
    ? "rgba(88, 166, 255, 0.18)"
    : hasSound
    ? colors.base
    : "#1a1d24";

  const keyLabelColor = hasSound ? "#ffffff" : "#6e7681";
  const soundLabelColor = hasSound ? "#c9d1d9" : "#484f58";
  const indicatorBg = isPlaying
    ? "#ffffff"
    : hasSound
    ? colors.accent
    : "#2d333b";

  const handleDragStart = (e: React.DragEvent<HTMLDivElement>) => {
    if (!hasSound) {
      e.preventDefault();
      return;
    }
    e.dataTransfer.setData("application/x-soundboard-pad", keyId);
    e.dataTransfer.effectAllowed = "move";
  };

  const handleDragOver = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    if (!isDragOver) setIsDragOver(true);
  };

  const handleDragLeave = () => {
    setIsDragOver(false);
  };

  const handleDrop = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    setIsDragOver(false);

    const sourcePadKey = e.dataTransfer.getData("application/x-soundboard-pad");
    if (sourcePadKey && sourcePadKey !== keyId) {
      onPadSwapped(sourcePadKey, keyId);
      return;
    }

    const downloadPath = e.dataTransfer.getData("application/x-soundboard-file");
    if (downloadPath) {
      onFileDropped(keyId, downloadPath);
      return;
    }
  };

  return (
    <div
      data-key-id={keyId}
      draggable={hasSound}
      onDragStart={handleDragStart}
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
      onClick={() => onPlay(keyId)}
      onContextMenu={(e) => {
        e.preventDefault();
        onOpenSettings(keyId);
      }}
      style={{
        width: `${width}px`,
        height: `${height}px`,
        backgroundColor,
        borderColor,
      }}
      title={
        hasSound
          ? `${displayText}: ${pad.sound_name} (คลิกซ้ายเพื่อเล่น | คลิกขวาตั้งค่า | ลากเพื่อย้ายปุ่ม)`
          : `${displayText} (ว่าง — ลากไฟล์มาวาง หรือคลิกขวาเพื่อตั้งค่า)`
      }
      className="relative m-[3px] rounded-[8px] border-[1.5px] cursor-pointer transition-all duration-150 ease-out hover:-translate-y-[3px] shadow-[0_2px_5px_rgba(0,0,0,0.35)] hover:shadow-[0_5px_15px_rgba(0,0,0,0.6)] flex flex-col items-center justify-center overflow-hidden"
    >
      {/* Subtle internal top highlight for 3D effect */}
      <div className="absolute inset-[1px] rounded-[7px] border-t border-white/10 pointer-events-none" />

      {/* Center Labels */}
      <div className="flex flex-col items-center justify-center -mt-[2px] px-1 pointer-events-none">
        <span
          style={{ color: keyLabelColor }}
          className="font-semibold text-[17px] leading-tight text-center"
        >
          {displayText}
        </span>
        <span
          style={{ color: soundLabelColor }}
          className="text-[11px] leading-tight mt-[2px] max-w-[66px] truncate text-center"
        >
          {shortName}
        </span>
      </div>

      {/* Bottom Indicator Bar */}
      <div
        style={{
          backgroundColor: indicatorBg,
          boxShadow:
            hasSound && !isPlaying
              ? `0 0 8px ${colors.accent}80`
              : undefined,
        }}
        className={`absolute bottom-[6px] left-[10px] right-[10px] h-[4px] rounded-[2px] pointer-events-none ${
          isPlaying ? "indicator-playing" : ""
        }`}
      />
    </div>
  );
};


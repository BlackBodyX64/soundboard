import React, { useEffect, useRef, useState } from "react";

interface WaveformViewerProps {
  peaks: number[];
  duration: number;
  isLoading?: boolean;
  startRatio: number;
  endRatio: number;
  playbackRatio: number;
  onTrimChange: (startRatio: number, endRatio: number) => void;
}

type DragTarget = "none" | "start" | "end" | "region";

const HANDLE_WIDTH = 10;

export const WaveformViewer: React.FC<WaveformViewerProps> = ({
  peaks,
  duration,
  isLoading = false,
  startRatio,
  endRatio,
  playbackRatio,
  onTrimChange,
}) => {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const [dragTarget, setDragTarget] = useState<DragTarget>("none");
  const dragInitRef = useRef({
    startX: 0,
    startRatio: 0,
    endRatio: 1,
  });

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    const dpr = window.devicePixelRatio || 1;
    const rect = canvas.getBoundingClientRect();
    const w = rect.width;
    const h = rect.height;
    if (w <= 0 || h <= 0) return;

    canvas.width = w * dpr;
    canvas.height = h * dpr;
    ctx.scale(dpr, dpr);

    const mid = h / 2.0;
    const startX = startRatio * w;
    const endX = endRatio * w;

    // Background
    ctx.fillStyle = "#0c1018";
    ctx.fillRect(0, 0, w, h);

    // Inactive overlay (left of start)
    ctx.fillStyle = "rgba(139, 148, 158, 0.27)";
    ctx.fillRect(0, 0, startX, h);

    // Active region highlight
    ctx.fillStyle = "rgba(88, 166, 255, 0.2)";
    ctx.fillRect(startX, 0, Math.max(0, endX - startX), h);

    // Inactive overlay (right of end)
    ctx.fillStyle = "rgba(139, 148, 158, 0.27)";
    ctx.fillRect(endX, 0, Math.max(0, w - endX), h);

    // Waveform bars
    if (peaks.length > 0 && duration > 0) {
      const binW = w / peaks.length;
      const barWidth = Math.max(binW - 0.5, 0.5);
      for (let i = 0; i < peaks.length; i++) {
        const x = i * binW;
        const barH = peaks[i] * mid * 0.92;
        const active = x >= startX && x <= endX;
        ctx.fillStyle = active ? "#58a6ff" : "#2d6a9f";
        // Top & bottom mirrored bars
        ctx.fillRect(x, mid - barH, barWidth, barH);
        ctx.fillRect(x, mid, barWidth, barH);
      }
    } else {
      ctx.fillStyle = "#484f58";
      ctx.font = '13px "Segoe UI", sans-serif';
      ctx.textAlign = "center";
      ctx.textBaseline = "middle";
      ctx.fillText(
        isLoading ? "กำลังโหลดไฟล์เสียง..." : "ยังไม่ได้เลือกไฟล์เสียง",
        w / 2,
        mid
      );
    }

    // Center line
    ctx.strokeStyle = "rgba(255, 255, 255, 0.27)";
    ctx.lineWidth = 0.5;
    ctx.beginPath();
    ctx.moveTo(0, mid);
    ctx.lineTo(w, mid);
    ctx.stroke();

    // Playback line
    if (playbackRatio >= 0 && playbackRatio <= 1.0) {
      const playX = playbackRatio * w;
      ctx.strokeStyle = "#ff4d4d";
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.moveTo(playX, 0);
      ctx.lineTo(playX, h);
      ctx.stroke();
    }

    // Draw Handles
    const drawHandle = (x: number, isStart: boolean) => {
      // Shadow
      ctx.fillStyle = "rgba(0, 0, 0, 0.53)";
      ctx.fillRect(
        x + (isStart ? -HANDLE_WIDTH - 1 : 1),
        0,
        HANDLE_WIDTH + 2,
        h
      );

      // Handle body
      const hx = isStart ? x - HANDLE_WIDTH : x;
      ctx.fillStyle = "#ffd700";
      ctx.fillRect(hx, 0, HANDLE_WIDTH, h);

      // Grip lines
      ctx.strokeStyle = "rgba(0, 0, 0, 0.5)";
      ctx.lineWidth = 1;
      for (let i = 0; i < 3; i++) {
        const gy = h * (0.3 + i * 0.2);
        ctx.beginPath();
        ctx.moveTo(hx + 2, gy);
        ctx.lineTo(hx + HANDLE_WIDTH - 2, gy);
        ctx.stroke();
      }

      // Arrow triangle
      const cx = hx + HANDLE_WIDTH / 2;
      const ty = h / 2 - 6;
      ctx.fillStyle = "rgba(0, 0, 0, 0.8)";
      ctx.beginPath();
      if (isStart) {
        ctx.moveTo(cx - 3, ty + 6);
        ctx.lineTo(cx + 3, ty);
        ctx.lineTo(cx + 3, ty + 12);
      } else {
        ctx.moveTo(cx + 3, ty + 6);
        ctx.lineTo(cx - 3, ty);
        ctx.lineTo(cx - 3, ty + 12);
      }
      ctx.closePath();
      ctx.fill();
    };

    drawHandle(startX, true);
    drawHandle(endX, false);
  }, [peaks, duration, isLoading, startRatio, endRatio, playbackRatio]);

  const handlePointerDown = (e: React.PointerEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const rect = canvas.getBoundingClientRect();
    const w = rect.width;
    if (w <= 0) return;

    const x = e.clientX - rect.left;
    const startX = startRatio * w;
    const endX = endRatio * w;
    const ratio = x / w;

    dragInitRef.current = {
      startX: x,
      startRatio,
      endRatio,
    };

    let target: DragTarget = "none";
    if (Math.abs(x - startX) <= HANDLE_WIDTH + 6) {
      target = "start";
    } else if (Math.abs(x - endX) <= HANDLE_WIDTH + 6) {
      target = "end";
    } else if (ratio > startRatio && ratio < endRatio) {
      target = "region";
    }

    if (target !== "none") {
      setDragTarget(target);
      canvas.setPointerCapture(e.pointerId);
    }
  };

  const handlePointerMove = (e: React.PointerEvent<HTMLCanvasElement>) => {
    if (dragTarget === "none") return;
    const canvas = canvasRef.current;
    if (!canvas) return;
    const rect = canvas.getBoundingClientRect();
    const w = rect.width;
    if (w <= 0) return;

    const x = e.clientX - rect.left;
    const ratio = x / w;
    const delta = (x - dragInitRef.current.startX) / w;

    if (dragTarget === "start") {
      const nextStart = Math.max(0.0, Math.min(ratio, endRatio - 0.01));
      onTrimChange(nextStart, endRatio);
    } else if (dragTarget === "end") {
      const nextEnd = Math.min(1.0, Math.max(ratio, startRatio + 0.01));
      onTrimChange(startRatio, nextEnd);
    } else if (dragTarget === "region") {
      const span = dragInitRef.current.endRatio - dragInitRef.current.startRatio;
      const nextStart = Math.max(
        0.0,
        Math.min(dragInitRef.current.startRatio + delta, 1.0 - span)
      );
      onTrimChange(nextStart, nextStart + span);
    }
  };

  const handlePointerUp = (e: React.PointerEvent<HTMLCanvasElement>) => {
    if (dragTarget !== "none") {
      setDragTarget("none");
      try {
        canvasRef.current?.releasePointerCapture(e.pointerId);
      } catch {}
    }
  };

  return (
    <canvas
      ref={canvasRef}
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={handlePointerUp}
      className="w-full h-[100px] block cursor-pointer"
    />
  );
};


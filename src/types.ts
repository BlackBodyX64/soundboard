export interface SoundPad {
  key: string;
  sound_path: string;
  sound_name: string;
  volume: number;
  start_time: number;
  end_time: number;
  speed: number;
}

export interface SoundboardConfig {
  master_volume: number;
  download_directory: string;
  pads: SoundPad[];
}

export interface DownloadedItem {
  name: string;
  path: string;
}

export interface WaveformData {
  peaks: number[];
  duration: number;
}

export interface DownloadProgressEvent {
  percentage: number;
  message: string;
}

export interface SoundStateEvent {
  key: string;
  playing: boolean;
}

export interface PreviewProgressEvent {
  ratio: number;
  finished: boolean;
}

export const FKEY_ROW = [
  "F1", "F2", "F3", "F4", "F5", "F6",
  "F7", "F8", "F9", "F10", "F11", "F12",
];

export const KB_ROWS = [
  ["1", "2", "3", "4", "5", "6", "7", "8", "9", "0"],
  ["Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P"],
  ["A", "S", "D", "F", "G", "H", "J", "K", "L"],
  ["Z", "X", "C", "V", "B", "N", "M"],
];

export const KB_OFFSETS = [0, 20, 35, 55];

export const NUMPAD_ROWS = [
  ["Num7", "Num8", "Num9"],
  ["Num4", "Num5", "Num6"],
  ["Num1", "Num2", "Num3"],
  ["Num0", "Num.", "Num+"],
];

export interface ColorPair {
  base: string;
  accent: string;
}

export const SECTION_COLORS: Record<string, ColorPair> = {
  fkey:   { base: "#b8860b", accent: "#ffd700" },
  row0:   { base: "#e94560", accent: "#ff6b81" },
  row1:   { base: "#0f3460", accent: "#1a6daa" },
  row2:   { base: "#533483", accent: "#7c4dbd" },
  row3:   { base: "#e76f51", accent: "#f4a261" },
  numpad: { base: "#2d6a4f", accent: "#52b788" },
};

export const AUDIO_EXTENSIONS = [
  ".mp3", ".wav", ".ogg", ".flac", ".aac", ".wma", ".m4a", ".opus",
];

export function isAudioFile(path: string): boolean {
  const lower = path.toLowerCase();
  return AUDIO_EXTENSIONS.some((ext) => lower.endsWith(ext));
}

export function getColorsForKey(key: string): ColorPair {
  if (FKEY_ROW.includes(key)) return SECTION_COLORS.fkey;
  for (let i = 0; i < KB_ROWS.length; i++) {
    if (KB_ROWS[i].includes(key)) return SECTION_COLORS[`row${i}`];
  }
  return SECTION_COLORS.numpad;
}

export function getAllKeys(): string[] {
  const keys: string[] = [...FKEY_ROW];
  for (const row of KB_ROWS) keys.push(...row);
  for (const row of NUMPAD_ROWS) keys.push(...row);
  return keys;
}

export function createEmptyPad(key: string): SoundPad {
  return {
    key,
    sound_path: "",
    sound_name: "",
    volume: 100,
    start_time: -1,
    end_time: -1,
    speed: 100,
  };
}


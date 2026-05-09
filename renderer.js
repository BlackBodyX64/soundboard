/**
 * TikTok Livestream Soundboard - Renderer Process
 */

// ==================== Layout Definitions ====================
const FKEY_ROW = ['F1','F2','F3','F4','F5','F6','F7','F8','F9','F10','F11','F12'];

const KB_ROWS = [
  ['1','2','3','4','5','6','7','8','9','0'],
  ['Q','W','E','R','T','Y','U','I','O','P'],
  ['A','S','D','F','G','H','J','K','L'],
  ['Z','X','C','V','B','N','M'],
];
const KB_OFFSETS = [0, 1, 2, 3]; // maps to CSS offset-0..offset-3

const NUMPAD_ROWS = [
  ['Num7','Num8','Num9'],
  ['Num4','Num5','Num6'],
  ['Num1','Num2','Num3'],
  ['Num0','Num.','Num+'],
];

const AUDIO_EXTS = new Set(['.mp3','.wav','.ogg','.flac','.aac','.wma']);

// Electron accelerator mapping
const ACCELERATOR_MAP = {};
for (let i = 1; i <= 12; i++) ACCELERATOR_MAP[`F${i}`] = `F${i}`;
for (const k of '1234567890') ACCELERATOR_MAP[k] = k;
for (const k of 'QWERTYUIOPASDFGHJKLZXCVBNM') ACCELERATOR_MAP[k] = k;
for (let i = 0; i <= 9; i++) ACCELERATOR_MAP[`Num${i}`] = `num${i}`;
ACCELERATOR_MAP['Num.'] = 'numdec';
ACCELERATOR_MAP['Num+'] = 'numadd';

// Color schemes
const SECTION_COLORS = {
  fkey:   { base: '#b8860b', accent: '#ffd700', dark: '#3d2e00' },
  row0:   { base: '#e94560', accent: '#ff6b81', dark: '#4a0e1e' },
  row1:   { base: '#0f3460', accent: '#1a6daa', dark: '#071a30' },
  row2:   { base: '#533483', accent: '#7c4dbd', dark: '#2a1545' },
  row3:   { base: '#e76f51', accent: '#f4a261', dark: '#4a2218' },
  numpad: { base: '#2d6a4f', accent: '#52b788', dark: '#1a3d2e' },
};

// ==================== State ====================
const pads = {};       // key -> { key, soundPath, soundName, volume }
const keyElements = {}; // key -> DOM element
let masterVolume = 80;
let hotkeysActive = true;
let currentEditKey = null;

// Audio context for playback
const audioCtx = new (window.AudioContext || window.webkitAudioContext)();
const audioBuffers = {}; // key -> AudioBuffer (cache)
const activeSources = {}; // key -> { source, gainNode }

// Master gain node
const masterGain = audioCtx.createGain();
masterGain.gain.value = masterVolume / 100;
masterGain.connect(audioCtx.destination);

// ==================== Init ====================
async function init() {
  initPads();
  await loadConfig();
  buildUI();
  setupEvents();
  await registerShortcuts();

  // Listen for global shortcut triggers from main process
  if (window.electronAPI) {
    window.electronAPI.onShortcutTriggered((key) => {
      if (hotkeysActive) playSound(key);
    });
  }
}

function initPads() {
  const allKeys = [...FKEY_ROW, ...KB_ROWS.flat(), ...NUMPAD_ROWS.flat()];
  for (const k of allKeys) {
    pads[k] = { key: k, soundPath: '', soundName: '', volume: 100 };
  }
}

async function loadConfig() {
  if (!window.electronAPI) return;
  const data = await window.electronAPI.loadConfig();
  if (!data) return;
  masterVolume = data.master_volume ?? 80;
  if (data.pads) {
    for (const pd of data.pads) {
      if (pd.key && pads[pd.key]) {
        pads[pd.key] = {
          key: pd.key,
          soundPath: pd.sound_path || '',
          soundName: pd.sound_name || '',
          volume: pd.volume ?? 100,
        };
      }
    }
  }
}

async function saveConfig() {
  if (!window.electronAPI) return;
  const data = {
    master_volume: masterVolume,
    pads: Object.values(pads).map(p => ({
      key: p.key,
      sound_path: p.soundPath,
      sound_name: p.soundName,
      volume: p.volume,
    })),
  };
  await window.electronAPI.saveConfig(data);
}

// ==================== Build UI ====================
function buildUI() {
  // F-key row
  const fkeyRow = document.getElementById('fkey-row');
  for (const k of FKEY_ROW) {
    fkeyRow.appendChild(createKeyButton(k, SECTION_COLORS.fkey, { fkey: true }));
  }

  // Main keyboard
  const mainKb = document.getElementById('main-keyboard');
  for (let i = 0; i < KB_ROWS.length; i++) {
    const row = document.createElement('div');
    row.className = `key-row offset-${i}`;
    for (const k of KB_ROWS[i]) {
      row.appendChild(createKeyButton(k, SECTION_COLORS[`row${i}`]));
    }
    mainKb.appendChild(row);
  }

  // Numpad
  const numpadKeys = document.getElementById('numpad-keys');
  for (const rowKeys of NUMPAD_ROWS) {
    const row = document.createElement('div');
    row.className = 'key-row';
    for (const k of rowKeys) {
      const display = k.replace('Num', '');
      row.appendChild(createKeyButton(k, SECTION_COLORS.numpad, { displayText: display }));
    }
    numpadKeys.appendChild(row);
  }

  // Set master volume slider
  const volSlider = document.getElementById('master-volume');
  volSlider.value = masterVolume;
  updateVolumeSliderFill(volSlider);
  document.getElementById('volume-label').textContent = `${masterVolume}%`;
}

function createKeyButton(key, colors, opts = {}) {
  const pad = pads[key];
  const hasSound = !!pad.soundPath;

  const btn = document.createElement('div');
  btn.className = `key-btn${opts.fkey ? ' fkey' : ''}${hasSound ? ' has-sound' : ''}`;
  btn.dataset.key = key;
  btn.style.setProperty('--key-base', colors.base);
  btn.style.setProperty('--key-accent', colors.accent);

  const label = document.createElement('span');
  label.className = 'key-label';
  label.textContent = opts.displayText || key;

  const name = document.createElement('span');
  name.className = 'key-name';
  name.textContent = truncName(pad.soundName);

  const indicator = document.createElement('div');
  indicator.className = 'key-indicator';

  btn.appendChild(label);
  btn.appendChild(name);
  btn.appendChild(indicator);

  // Click to play
  btn.addEventListener('click', (e) => {
    if (e.button === 0) playSound(key);
  });

  // Right-click to settings
  btn.addEventListener('contextmenu', (e) => {
    e.preventDefault();
    openSettings(key, colors);
  });

  // Drag & drop
  btn.addEventListener('dragover', (e) => {
    e.preventDefault();
    e.stopPropagation();
    btn.classList.add('drop-hover');
  });
  btn.addEventListener('dragleave', (e) => {
    e.stopPropagation();
    btn.classList.remove('drop-hover');
  });
  btn.addEventListener('drop', (e) => {
    e.preventDefault();
    e.stopPropagation();
    btn.classList.remove('drop-hover');
    handleDrop(e, key);
  });

  keyElements[key] = { el: btn, colors };
  return btn;
}

function truncName(s) {
  if (!s) return '';
  return s.length > 7 ? s.slice(0, 6) + '…' : s;
}

function refreshKey(key) {
  const w = keyElements[key];
  if (!w) return;
  const pad = pads[key];
  const hasSound = !!pad.soundPath;
  w.el.classList.toggle('has-sound', hasSound);
  w.el.querySelector('.key-name').textContent = truncName(pad.soundName);
}

// ==================== Events ====================
function setupEvents() {
  // Window controls
  document.getElementById('btn-minimize')?.addEventListener('click', () => window.electronAPI?.windowMinimize());
  document.getElementById('btn-maximize')?.addEventListener('click', () => window.electronAPI?.windowMaximize());
  document.getElementById('btn-close')?.addEventListener('click', () => window.electronAPI?.windowClose());

  // Stop all
  document.getElementById('btn-stop-all').addEventListener('click', stopAll);

  // Master volume
  const volSlider = document.getElementById('master-volume');
  volSlider.addEventListener('input', (e) => {
    masterVolume = parseInt(e.target.value);
    document.getElementById('volume-label').textContent = `${masterVolume}%`;
    masterGain.gain.value = masterVolume / 100;
    updateVolumeSliderFill(volSlider);
  });
  volSlider.addEventListener('change', () => saveConfig());

  // Hotkeys toggle
  document.getElementById('hotkeys-switch').addEventListener('change', async (e) => {
    hotkeysActive = e.target.checked;
    if (hotkeysActive) {
      await registerShortcuts();
    } else {
      await window.electronAPI?.unregisterShortcuts();
    }
  });

  // Modal
  document.getElementById('settings-overlay').addEventListener('click', (e) => {
    if (e.target === e.currentTarget) closeSettings();
  });
  document.getElementById('modal-browse-btn').addEventListener('click', browseFile);
  document.getElementById('modal-save-btn').addEventListener('click', saveSettings);
  document.getElementById('modal-clear-btn').addEventListener('click', clearSettings);
  document.getElementById('modal-volume').addEventListener('input', (e) => {
    document.getElementById('modal-volume-label').textContent = `${e.target.value}%`;
    updateVolumeSliderFill(e.target);
  });

  // Global drag & drop (for bulk assign)
  document.body.addEventListener('dragover', (e) => {
    e.preventDefault();
    document.body.classList.add('drag-over');
  });
  document.body.addEventListener('dragleave', (e) => {
    if (e.target === document.body || !document.body.contains(e.relatedTarget)) {
      document.body.classList.remove('drag-over');
    }
  });
  document.body.addEventListener('drop', (e) => {
    e.preventDefault();
    document.body.classList.remove('drag-over');
    handleGlobalDrop(e);
  });

  // Keyboard shortcut: Escape to close modal
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') closeSettings();
  });
}

function updateVolumeSliderFill(slider) {
  const pct = ((slider.value - slider.min) / (slider.max - slider.min)) * 100;
  slider.style.setProperty('--fill', `${pct}%`);
  slider.style.background = `linear-gradient(to right, var(--accent-red) 0%, var(--accent-red) ${pct}%, var(--bg-tertiary) ${pct}%, var(--bg-tertiary) 100%)`;
}

// ==================== Drag & Drop ====================
function handleDrop(e, key) {
  const files = e.dataTransfer.files;
  if (!files.length) return;
  for (const file of files) {
    const ext = getExt(file.name);
    if (AUDIO_EXTS.has(ext)) {
      assignSound(key, file.path, getBaseName(file.name));
      return;
    }
  }
}

function handleGlobalDrop(e) {
  const files = e.dataTransfer.files;
  if (!files.length) return;

  const audioFiles = [];
  for (const file of files) {
    if (AUDIO_EXTS.has(getExt(file.name))) {
      audioFiles.push(file);
    }
  }

  // Find empty keys in order
  const allKeys = [...FKEY_ROW, ...KB_ROWS.flat(), ...NUMPAD_ROWS.flat()];
  const emptyKeys = allKeys.filter(k => !pads[k].soundPath);

  for (let i = 0; i < Math.min(audioFiles.length, emptyKeys.length); i++) {
    assignSound(emptyKeys[i], audioFiles[i].path, getBaseName(audioFiles[i].name));
  }
}

async function assignSound(key, filePath, name) {
  pads[key].soundPath = filePath;
  pads[key].soundName = name || getBaseName(filePath);
  // Clear cached buffer so it reloads
  delete audioBuffers[key];
  refreshKey(key);
  await saveConfig();
  await registerShortcuts();
}

function getExt(filename) {
  const i = filename.lastIndexOf('.');
  return i >= 0 ? filename.slice(i).toLowerCase() : '';
}

function getBaseName(filepath) {
  const name = filepath.replace(/\\/g, '/').split('/').pop();
  const i = name.lastIndexOf('.');
  return i >= 0 ? name.slice(0, i) : name;
}

// ==================== Audio Playback ====================
async function loadAudioBuffer(key) {
  const pad = pads[key];
  if (!pad.soundPath) return null;

  if (audioBuffers[key]) return audioBuffers[key];

  try {
    // Read file through main process IPC (works for both dialog and drag-drop paths)
    const arrayBuffer = await window.electronAPI.readAudioFile(pad.soundPath);
    if (!arrayBuffer) {
      console.error(`File not found or unreadable: ${pad.soundPath}`);
      return null;
    }
    const buffer = await audioCtx.decodeAudioData(arrayBuffer);
    audioBuffers[key] = buffer;
    return buffer;
  } catch (e) {
    console.error(`Failed to load audio for ${key}:`, e);
    return null;
  }
}

async function playSound(key) {
  const pad = pads[key];
  if (!pad.soundPath) return;

  // Resume context if suspended (Chrome autoplay policy)
  if (audioCtx.state === 'suspended') {
    await audioCtx.resume();
  }

  const buffer = await loadAudioBuffer(key);
  if (!buffer) return;

  // Create new source
  const source = audioCtx.createBufferSource();
  source.buffer = buffer;

  // Per-pad gain
  const gainNode = audioCtx.createGain();
  gainNode.gain.value = pad.volume / 100;

  source.connect(gainNode);
  gainNode.connect(masterGain);

  // Track active source
  activeSources[key] = { source, gainNode };

  // Visual feedback
  setPlaying(key, true);

  source.onended = () => {
    setPlaying(key, false);
    delete activeSources[key];
  };

  source.start(0);
}

function stopAll() {
  for (const key of Object.keys(activeSources)) {
    try {
      activeSources[key].source.stop();
    } catch {}
    setPlaying(key, false);
  }
  // Clear all
  for (const k in activeSources) delete activeSources[k];
}

function setPlaying(key, playing) {
  const w = keyElements[key];
  if (!w) return;
  w.el.classList.toggle('playing', playing);
}

// ==================== Settings Modal ====================
function openSettings(key, colors) {
  currentEditKey = key;
  const pad = pads[key];

  const modal = document.getElementById('settings-overlay');
  modal.classList.remove('hidden');

  document.getElementById('modal-title').textContent = `⚙ ตั้งค่าปุ่ม [ ${key} ]`;
  document.getElementById('modal-title').style.color = colors.accent;

  document.getElementById('modal-file-path').value = pad.soundPath || '';
  document.getElementById('modal-display-name').value = pad.soundName || '';

  const volSlider = document.getElementById('modal-volume');
  volSlider.value = pad.volume;
  document.getElementById('modal-volume-label').textContent = `${pad.volume}%`;
  updateVolumeSliderFill(volSlider);
}

function closeSettings() {
  document.getElementById('settings-overlay').classList.add('hidden');
  currentEditKey = null;
}

async function browseFile() {
  if (!window.electronAPI) return;
  const filePath = await window.electronAPI.openFileDialog();
  if (filePath) {
    document.getElementById('modal-file-path').value = filePath;
    // Auto-fill display name if empty
    const nameInput = document.getElementById('modal-display-name');
    if (!nameInput.value) {
      nameInput.value = getBaseName(filePath);
    }
  }
}

async function saveSettings() {
  if (!currentEditKey) return;
  const key = currentEditKey;
  const filePath = document.getElementById('modal-file-path').value.trim();
  const displayName = document.getElementById('modal-display-name').value.trim();
  const volume = parseInt(document.getElementById('modal-volume').value);

  pads[key].soundPath = filePath;
  pads[key].soundName = displayName || (filePath ? getBaseName(filePath) : '');
  pads[key].volume = volume;

  // Clear cached buffer
  delete audioBuffers[key];

  refreshKey(key);
  closeSettings();
  await saveConfig();
  await registerShortcuts();
}

async function clearSettings() {
  if (!currentEditKey) return;
  const key = currentEditKey;

  pads[key].soundPath = '';
  pads[key].soundName = '';
  pads[key].volume = 100;

  delete audioBuffers[key];

  refreshKey(key);
  closeSettings();
  await saveConfig();
  await registerShortcuts();
}

// ==================== Global Shortcuts ====================
async function registerShortcuts() {
  if (!window.electronAPI || !hotkeysActive) return;

  const shortcuts = [];
  for (const [key, pad] of Object.entries(pads)) {
    if (pad.soundPath && ACCELERATOR_MAP[key]) {
      shortcuts.push({
        key,
        accelerator: ACCELERATOR_MAP[key],
      });
    }
  }
  await window.electronAPI.registerShortcuts(shortcuts);
}

// ==================== Start ====================
init();

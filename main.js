/**
 * TikTok Livestream Soundboard - Electron Main Process
 */
const { app, BrowserWindow, ipcMain, dialog, globalShortcut } = require('electron');
const path = require('path');
const fs = require('fs');

let mainWindow = null;
let configFilePath = null;

function getConfigPath() {
  if (!configFilePath) {
    configFilePath = path.join(app.getPath('userData'), 'soundboard_config.json');
  }
  return configFilePath;
}

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1280,
    height: 720,
    minWidth: 1100,
    minHeight: 620,
    backgroundColor: '#0d1117',
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
    },
    frame: false,
    titleBarStyle: 'hidden',
  });

  mainWindow.loadFile('index.html');

  mainWindow.on('closed', () => {
    mainWindow = null;
  });
}

app.whenReady().then(() => {
  createWindow();
});

app.on('window-all-closed', () => {
  globalShortcut.unregisterAll();
  app.quit();
});

app.on('activate', () => {
  if (BrowserWindow.getAllWindows().length === 0) createWindow();
});

// ==================== IPC Handlers ====================

// File dialog
ipcMain.handle('open-file-dialog', async () => {
  const result = await dialog.showOpenDialog(mainWindow, {
    title: 'เลือกไฟล์เสียง',
    filters: [
      { name: 'Audio Files', extensions: ['mp3', 'wav', 'ogg', 'flac', 'aac', 'wma'] },
      { name: 'All Files', extensions: ['*'] },
    ],
    properties: ['openFile'],
  });
  if (result.canceled) return null;
  return result.filePaths[0];
});

// Config load/save
ipcMain.handle('load-config', async () => {
  try {
    const cfgPath = getConfigPath();
    // Try user data path first
    if (fs.existsSync(cfgPath)) {
      const data = fs.readFileSync(cfgPath, 'utf-8');
      return JSON.parse(data);
    }
    // Fallback to legacy config in app directory
    const legacyConfig = path.join(__dirname, 'soundboard_config.json');
    if (fs.existsSync(legacyConfig)) {
      const data = fs.readFileSync(legacyConfig, 'utf-8');
      return JSON.parse(data);
    }
  } catch (e) {
    console.error('Failed to load config:', e);
  }
  return null;
});

ipcMain.handle('save-config', async (_event, data) => {
  try {
    fs.writeFileSync(getConfigPath(), JSON.stringify(data, null, 2), 'utf-8');
    return true;
  } catch (e) {
    console.error('Failed to save config:', e);
    return false;
  }
});

// Global shortcuts
let registeredShortcuts = new Map();

ipcMain.handle('register-shortcuts', async (_event, shortcuts) => {
  // Unregister all existing shortcuts first
  globalShortcut.unregisterAll();
  registeredShortcuts.clear();

  for (const s of shortcuts) {
    try {
      const accelerator = s.accelerator;
      if (!accelerator) continue;
      const success = globalShortcut.register(accelerator, () => {
        if (mainWindow && !mainWindow.isDestroyed()) {
          mainWindow.webContents.send('shortcut-triggered', s.key);
        }
      });
      if (success) {
        registeredShortcuts.set(s.key, accelerator);
      }
    } catch (e) {
      // Some shortcuts may not be registerable
    }
  }
  return true;
});

ipcMain.handle('unregister-shortcuts', async () => {
  globalShortcut.unregisterAll();
  registeredShortcuts.clear();
  return true;
});

// Window controls
ipcMain.handle('window-minimize', () => mainWindow?.minimize());
ipcMain.handle('window-maximize', () => {
  if (mainWindow?.isMaximized()) {
    mainWindow.unmaximize();
  } else {
    mainWindow?.maximize();
  }
});
ipcMain.handle('window-close', () => mainWindow?.close());

// Read audio file and return as ArrayBuffer for Web Audio API
ipcMain.handle('read-audio-file', async (_event, filePath) => {
  try {
    if (!fs.existsSync(filePath)) return null;
    const buffer = fs.readFileSync(filePath);
    return buffer.buffer.slice(buffer.byteOffset, buffer.byteOffset + buffer.byteLength);
  } catch (e) {
    console.error('Failed to read audio file:', filePath, e);
    return null;
  }
});

// Check if file exists
ipcMain.handle('file-exists', async (_event, filePath) => {
  try {
    return fs.existsSync(filePath);
  } catch {
    return false;
  }
});

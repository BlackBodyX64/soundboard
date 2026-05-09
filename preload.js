/**
 * Preload script - exposes safe IPC APIs to the renderer
 */
const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('electronAPI', {
  openFileDialog: () => ipcRenderer.invoke('open-file-dialog'),
  loadConfig: () => ipcRenderer.invoke('load-config'),
  saveConfig: (data) => ipcRenderer.invoke('save-config', data),
  registerShortcuts: (shortcuts) => ipcRenderer.invoke('register-shortcuts', shortcuts),
  unregisterShortcuts: () => ipcRenderer.invoke('unregister-shortcuts'),
  fileExists: (path) => ipcRenderer.invoke('file-exists', path),
  readAudioFile: (filePath) => ipcRenderer.invoke('read-audio-file', filePath),
  windowMinimize: () => ipcRenderer.invoke('window-minimize'),
  windowMaximize: () => ipcRenderer.invoke('window-maximize'),
  windowClose: () => ipcRenderer.invoke('window-close'),
  onShortcutTriggered: (callback) => {
    ipcRenderer.on('shortcut-triggered', (_event, key) => callback(key));
  },
});

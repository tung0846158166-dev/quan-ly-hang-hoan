const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('desktopDiskAPI', {
  isDesktopApp: true,
  loadFromDisk: () => ipcRenderer.invoke('read-hard-drive-data'),
  saveToDisk: (data) => ipcRenderer.invoke('save-hard-drive-data', data),
  savePhotoFiles: (photos) => ipcRenderer.invoke('save-photo-files', photos),
  readPhotoFiles: (ids) => ipcRenderer.invoke('read-photo-files', ids),
  openDataFolder: () => ipcRenderer.invoke('open-data-folder'),
  backupToCustomFolder: (data) => ipcRenderer.invoke('backup-to-custom-folder', data),
  sendNotification: (title, body) => ipcRenderer.invoke('send-desktop-notification', { title, body }),
  getAutoStart: () => ipcRenderer.invoke('get-autostart-status'),
  setAutoStart: (enabled) => ipcRenderer.invoke('set-autostart-status', enabled),
  getAppVersion: () => ipcRenderer.invoke('get-app-version')
});

const { app, BrowserWindow, ipcMain, shell, dialog, Tray, Menu, Notification } = require('electron');
const path = require('path');
const fs = require('fs');

let mainWindow = null;
let tray = null;
let isQuitting = false;
let hasShownTrayBalloon = false;

// Dam bao chi chay 1 phien ban duy nhat (Single Instance Lock)
const gotTheLock = app.requestSingleInstanceLock();
if (!gotTheLock) {
  app.quit();
} else {
  app.on('second-instance', () => {
    if (mainWindow) {
      if (mainWindow.isMinimized()) mainWindow.restore();
      mainWindow.show();
      mainWindow.focus();
    }
  });
}

function getDataDir() {
  const documentsDir = app.getPath('documents');
  const appDataDir = path.join(documentsDir, 'QuanLyHangHoan');
  if (!fs.existsSync(appDataDir)) {
    fs.mkdirSync(appDataDir, { recursive: true });
  }
  return appDataDir;
}

function getDataFilePath() {
  return path.join(getDataDir(), 'du_lieu_hang_hoan.json');
}

function setAutoStart(enabled) {
  try {
    app.setLoginItemSettings({
      openAtLogin: enabled,
      openAsHidden: true,
      args: ['--hidden']
    });
    return true;
  } catch (e) {
    console.error('Loi cau hinh khoi dong cung Windows:', e);
    return false;
  }
}

function getAutoStart() {
  try {
    const settings = app.getLoginItemSettings();
    return settings.openAtLogin;
  } catch (e) {
    return false;
  }
}

function createTray() {
  if (tray) return;
  const iconPath = path.join(__dirname, 'app_icon.ico');
  tray = new Tray(iconPath);

  function updateTrayMenu() {
    const isAuto = getAutoStart();
    const appVer = app.getVersion();
    const contextMenu = Menu.buildFromTemplate([
      {
        label: `📦 Mở Quản Lý Hàng Hoàn (v${appVer})`,
        click: () => {
          if (mainWindow) {
            mainWindow.show();
            mainWindow.focus();
          }
        }
      },
      { type: 'separator' },
      {
        label: '📱 Quét ĐT: Đang chạy nền sẵn sàng',
        enabled: false
      },
      {
        label: '📁 Thư Mục Dữ Liệu Ổ Cứng',
        click: () => {
          shell.openPath(getDataDir());
        }
      },
      {
        label: '🚀 Tự khởi động cùng Windows',
        type: 'checkbox',
        checked: isAuto,
        click: (menuItem) => {
          setAutoStart(menuItem.checked);
          updateTrayMenu();
        }
      },
      { type: 'separator' },
      {
        label: '❌ Thoát hoàn toàn ứng dụng',
        click: () => {
          isQuitting = true;
          app.quit();
        }
      }
    ]);
    tray.setContextMenu(contextMenu);
  }

  tray.setToolTip('Quản Lý Hàng Hoàn Shopee - TikTok (Đang chạy nền)');
  updateTrayMenu();

  tray.on('double-click', () => {
    if (mainWindow) {
      mainWindow.show();
      mainWindow.focus();
    }
  });

  tray.on('click', () => {
    if (mainWindow) {
      if (mainWindow.isVisible()) {
        mainWindow.hide();
      } else {
        mainWindow.show();
        mainWindow.focus();
      }
    }
  });
}

const isHiddenStartup = process.argv.includes('--hidden');

function createWindow() {
  const appVer = app.getVersion();
  mainWindow = new BrowserWindow({
    width: 1450,
    height: 920,
    minWidth: 1024,
    minHeight: 700,
    show: !isHiddenStartup,
    title: `Quản Lý & Đối Soát Hàng Hoàn Shopee / TikTok Shop (v${appVer})`,
    icon: path.join(__dirname, 'app_icon.ico'),
    autoHideMenuBar: true,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      nodeIntegration: false,
      contextIsolation: true,
      sandbox: false,
      backgroundThrottling: false
    }
  });

  mainWindow.loadFile('Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html');
  mainWindow.setMenuBarVisibility(false);

  // Dam bao webContents luon nhan focus ban phim ngay khi cua so duoc kich hoat hoac hien thi tren Windows
  mainWindow.on('focus', () => {
    try {
      if (mainWindow && mainWindow.webContents) {
        mainWindow.webContents.focus();
      }
    } catch (e) {}
  });

  mainWindow.on('show', () => {
    try {
      if (mainWindow && mainWindow.webContents) {
        mainWindow.webContents.focus();
      }
    } catch (e) {}
  });

  // Bam nut dau X: An xuong khay he thong thay vi tat han
  mainWindow.on('close', (event) => {
    if (!isQuitting) {
      event.preventDefault();
      mainWindow.hide();

      if (!hasShownTrayBalloon) {
        hasShownTrayBalloon = true;
        try {
          if (Notification.isSupported()) {
            new Notification({
              title: 'Quản Lý Hàng Hoàn đang chạy nền',
              body: 'Ứng dụng đã thu nhỏ xuống khay hệ thống. Điện thoại vẫn tiếp tục quét và lưu bình thường!',
              icon: path.join(__dirname, 'app_icon.ico')
            }).show();
          }
        } catch (notifErr) {}
      }
      return false;
    }
  });
}

// IPC Handlers
ipcMain.handle('read-hard-drive-data', async () => {
  try {
    const dataFilePath = getDataFilePath();
    if (fs.existsSync(dataFilePath)) {
      const raw = await fs.promises.readFile(dataFilePath, 'utf8');
      const parsed = JSON.parse(raw);
      return { success: true, data: parsed, path: dataFilePath };
    }
    return { success: false, path: dataFilePath, message: 'File chua ton tai' };
  } catch (err) {
    return { success: false, error: err.message, path: getDataFilePath() };
  }
});

// Keep each caller's immutable snapshot and receipt in FIFO order. A rejected
// write must not poison later saves, and never falls back to overwriting live data.
let diskWriteTail = Promise.resolve();
let diskWriteSequence = 0;
let quitDrainStarted = false;
let quitDrainComplete = false;
const MAX_DISK_SNAPSHOT_BYTES = 512 * 1024 * 1024;

async function writeDiskSnapshot(serialized, sequence) {
  const dataFilePath = getDataFilePath();
  const tempFilePath = `${dataFilePath}.${process.pid || 'app'}.${sequence}.tmp`;
  let handle;
  try {
    handle = await fs.promises.open(tempFilePath, 'wx', 0o600);
    await handle.writeFile(serialized, 'utf8');
    await handle.sync();
    await handle.close();
    handle = null;
    await fs.promises.rename(tempFilePath, dataFilePath);
    return { success: true, path: dataFilePath, time: new Date().toLocaleTimeString('vi-VN') };
  } finally {
    if (handle) await handle.close().catch(() => {});
    await fs.promises.unlink(tempFilePath).catch(() => {});
  }
}

ipcMain.handle('save-hard-drive-data', async (event, dataToSave) => {
  try {
    if (quitDrainComplete) throw new Error('Ung dung dang thoat');
    if (!Array.isArray(dataToSave)) throw new Error('Du lieu luu phai la danh sach don hang');
    const serialized = JSON.stringify(dataToSave);
    if (Buffer.byteLength(serialized, 'utf8') > MAX_DISK_SNAPSHOT_BYTES) {
      throw new Error('Du lieu luu vuot qua gioi han 512 MB');
    }
    const sequence = ++diskWriteSequence;
    const write = diskWriteTail.then(() => writeDiskSnapshot(serialized, sequence));
    diskWriteTail = write.catch(() => {});
    return await write;
  } catch (err) {
    return { success: false, error: err.message };
  }
});

// Photos live in their own files so the main order list stays small and every
// scan no longer rewrites hundreds of MB of base64. A photo file is written
// (tmp + fsync + rename) BEFORE the renderer drops it from the order list.
function getPhotosDir() {
  const dir = path.join(getDataDir(), 'photos');
  if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true });
  return dir;
}
function photoFilePath(id) {
  const hash = require('crypto').createHash('sha1').update(String(id)).digest('hex');
  return path.join(getPhotosDir(), hash + '.json');
}
async function writePhotoFile(id, data, sequence) {
  const target = photoFilePath(id);
  const serialized = JSON.stringify({ id, data });
  try {
    // Already stored with identical content (e.g. after an app restart): nothing to rewrite.
    if ((await fs.promises.stat(target)).size === Buffer.byteLength(serialized, 'utf8') &&
        await fs.promises.readFile(target, 'utf8') === serialized) return;
  } catch (e) { /* missing file: write it below */ }
  const temp = `${target}.${process.pid || 'app'}.${sequence}.tmp`;
  let handle;
  try {
    handle = await fs.promises.open(temp, 'wx', 0o600);
    await handle.writeFile(serialized, 'utf8');
    await handle.sync();
    await handle.close();
    handle = null;
    await fs.promises.rename(temp, target);
  } finally {
    if (handle) await handle.close().catch(() => {});
    await fs.promises.unlink(temp).catch(() => {});
  }
}

ipcMain.handle('save-photo-files', async (event, photos) => {
  try {
    if (quitDrainComplete) throw new Error('Ung dung dang thoat');
    if (!Array.isArray(photos)) throw new Error('Danh sach anh khong hop le');
    const items = photos.filter(p => p && p.id && typeof p.data === 'string' && p.data);
    const sequence = ++diskWriteSequence;
    const write = diskWriteTail.then(async () => {
      const saved = [];
      for (const item of items) {
        try { await writePhotoFile(item.id, item.data, sequence); saved.push(item.id); } catch (e) { /* reported via saved list */ }
      }
      return saved;
    });
    diskWriteTail = write.catch(() => {});
    const saved = await write;
    return { success: saved.length === items.length, saved };
  } catch (err) {
    return { success: false, saved: [], error: err.message };
  }
});

ipcMain.handle('read-photo-files', async (event, ids) => {
  const photos = {};
  try {
    for (const id of Array.isArray(ids) ? ids : []) {
      try {
        const parsed = JSON.parse(await fs.promises.readFile(photoFilePath(id), 'utf8'));
        if (parsed && parsed.id === id && typeof parsed.data === 'string') photos[id] = parsed.data;
      } catch (e) { /* missing photo file is not an error */ }
    }
    return { success: true, photos };
  } catch (err) {
    return { success: false, photos, error: err.message };
  }
});

ipcMain.handle('open-data-folder', async () => {
  try {
    const dataFilePath = getDataFilePath();
    if (fs.existsSync(dataFilePath)) {
      shell.showItemInFolder(dataFilePath);
    } else {
      shell.openPath(getDataDir());
    }
    return { success: true, path: getDataDir() };
  } catch (e) {
    return { success: false, error: e.message };
  }
});

ipcMain.handle('backup-to-custom-folder', async (event, dataToSave) => {
  try {
    const now = new Date();
    const dateStr = `${now.getFullYear()}_${String(now.getMonth()+1).padStart(2,'0')}_${String(now.getDate()).padStart(2,'0')}`;
    const { filePath, canceled } = await dialog.showSaveDialog({
      title: 'Chọn nơi lưu bản sao lưu dữ liệu trên ổ cứng máy tính',
      defaultPath: path.join(app.getPath('desktop'), `Sao_Luu_Hang_Hoan_${dateStr}.json`),
      filters: [{ name: 'JSON Backup (*.json)', extensions: ['json'] }]
    });

    if (!canceled && filePath) {
      fs.writeFileSync(filePath, JSON.stringify(dataToSave, null, 2), 'utf8');
      return { success: true, path: filePath };
    }
    return { success: false, canceled: true };
  } catch (e) {
    return { success: false, error: e.message };
  }
});

ipcMain.handle('send-desktop-notification', (event, { title, body }) => {
  try {
    if (Notification.isSupported()) {
      new Notification({
        title: title || 'Quản Lý Hàng Hoàn',
        body: body || '',
        icon: path.join(__dirname, 'app_icon.ico')
      }).show();
    }
    return { success: true };
  } catch (e) {
    return { success: false, error: e.message };
  }
});

ipcMain.handle('get-autostart-status', () => {
  return { openAtLogin: getAutoStart() };
});

ipcMain.handle('set-autostart-status', (event, enabled) => {
  setAutoStart(enabled);
  return { openAtLogin: getAutoStart() };
});

ipcMain.handle('get-app-version', () => {
  return app.getVersion();
});

app.whenReady().then(() => {
  getDataDir();
  createWindow();
  createTray();

  // Mac dinh bat tu khoi dong cung may tinh
  if (!getAutoStart()) {
    setAutoStart(true);
  }

  app.on('activate', () => {
    if (mainWindow) {
      mainWindow.show();
      mainWindow.focus();
    } else {
      createWindow();
    }
  });
});

app.on('before-quit', (event) => {
  isQuitting = true;
  if (quitDrainComplete) return;
  event.preventDefault();
  if (quitDrainStarted) return;
  quitDrainStarted = true;
  (async () => {
    // Renderer saves arriving while a previous write drains extend the barrier.
    let observedTail;
    do {
      observedTail = diskWriteTail;
      await observedTail;
    } while (observedTail !== diskWriteTail);
    quitDrainComplete = true;
    app.quit();
  })();
});

app.on('window-all-closed', () => {
  // Khong thoat app tren Windows khi dong cua so, giu app chay nen
});

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
    const contextMenu = Menu.buildFromTemplate([
      {
        label: '📦 Mở Quản Lý Hàng Hoàn',
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
  mainWindow = new BrowserWindow({
    width: 1450,
    height: 920,
    minWidth: 1024,
    minHeight: 700,
    show: !isHiddenStartup,
    title: 'Quản Lý & Đối Soát Hàng Hoàn Shopee / TikTok Shop',
    icon: path.join(__dirname, 'app_icon.ico'),
    autoHideMenuBar: true,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      nodeIntegration: false,
      contextIsolation: true,
      sandbox: false
    }
  });

  mainWindow.loadFile('Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html');
  mainWindow.setMenuBarVisibility(false);

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
      const raw = fs.readFileSync(dataFilePath, 'utf8');
      const parsed = JSON.parse(raw);
      return { success: true, data: parsed, path: dataFilePath };
    }
    return { success: false, path: dataFilePath, message: 'File chua ton tai' };
  } catch (err) {
    return { success: false, error: err.message, path: getDataFilePath() };
  }
});

ipcMain.handle('save-hard-drive-data', async (event, dataToSave) => {
  try {
    const dataFilePath = getDataFilePath();
    fs.writeFileSync(dataFilePath, JSON.stringify(dataToSave, null, 2), 'utf8');
    return { success: true, path: dataFilePath, time: new Date().toLocaleTimeString('vi-VN') };
  } catch (err) {
    return { success: false, error: err.message };
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

app.on('before-quit', () => {
  isQuitting = true;
});

app.on('window-all-closed', () => {
  // Khong thoat app tren Windows khi dong cua so, giu app chay nen
});

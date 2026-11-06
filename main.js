const { app, BrowserWindow, ipcMain, shell, dialog } = require('electron');
const path = require('path');
const fs = require('fs');

// Lay duong dan thu muc luu tru vinh vien tren o cung may tinh: Documents/QuanLyHangHoan
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

function createWindow() {
  const win = new BrowserWindow({
    width: 1450,
    height: 920,
    minWidth: 1024,
    minHeight: 700,
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

  win.loadFile('Bang_Excel_Quan_Ly_Hang_Hoan_Tu_Dong_Thu_Nho_Anh.html');
  win.setMenuBarVisibility(false);
}

// 1. Doc du lieu tu file vat ly tren o cung
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

// 2. Ghi du lieu truc tiep xuong file vat ly tren o cung may tinh
ipcMain.handle('save-hard-drive-data', async (event, dataToSave) => {
  try {
    const dataFilePath = getDataFilePath();
    fs.writeFileSync(dataFilePath, JSON.stringify(dataToSave, null, 2), 'utf8');
    return { success: true, path: dataFilePath, time: new Date().toLocaleTimeString('vi-VN') };
  } catch (err) {
    return { success: false, error: err.message };
  }
});

// 3. Mo thu muc chua file du lieu tren o cung trong Windows File Explorer
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

// 4. Cho phep nguoi dung chon o dia / thu muc bat ky de xuat ban sao luu (USB, o D, E...)
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

app.whenReady().then(() => {
  // Khoi tao thu muc luu tru truoc
  getDataDir();
  createWindow();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});

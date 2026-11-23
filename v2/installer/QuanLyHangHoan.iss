; Bo cai app Quan Ly Hang Hoan v2 (Inno Setup 6). Bien dich bang build-installer.ps1.
; Cai THEO NGUOI DUNG (khong can quyen admin) vao %LocalAppData%\Programs\QuanLyHangHoan.
; DU LIEU nguoi dung (%LocalAppData%\QuanLyHangHoan\v2: data.db, settings.json, sao luu) KHONG nam trong thu muc cai
; va bo go cai KHONG BAO GIO xoa chung.
#ifndef AppVersion
  #define AppVersion "2.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\self-contained"
#endif
#ifndef OutDir
  #define OutDir "..\dist\release"
#endif

[Setup]
; AppId co dinh: cai de len ban cu = cap nhat tai cho, giu nguyen du lieu.
AppId={{6F3B9A52-7D41-4E0C-9B1A-2C5E8D7A4F10}
AppName=Quản Lý Hàng Hoàn
AppVersion={#AppVersion}
AppPublisher=Quản Lý Hàng Hoàn
DefaultDirName={localappdata}\Programs\QuanLyHangHoan
DefaultGroupName=Quản Lý Hàng Hoàn
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutDir}
OutputBaseFilename=QuanLyHangHoan-v2-{#AppVersion}-setup
SetupIconFile=app.ico
UninstallDisplayIcon={app}\QuanLyHangHoan.App.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Dong app dang chay de ghi de tep, KHONG tu mo lai (tranh mo app giua luc cap nhat).
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "default"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Tạo biểu tượng ngoài Màn hình nền"; Flags: unchecked
Name: "autostart"; Description: "Tự mở cùng Windows (thu nhỏ xuống khay)"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Quản Lý Hàng Hoàn"; Filename: "{app}\QuanLyHangHoan.App.exe"
Name: "{group}\Gỡ cài đặt Quản Lý Hàng Hoàn"; Filename: "{uninstallexe}"
Name: "{userdesktop}\Quản Lý Hàng Hoàn"; Filename: "{app}\QuanLyHangHoan.App.exe"; Tasks: desktopicon

[Registry]
; Chi khi nguoi dung tick "Tu mo cung Windows": cung khoa/gia tri/tham so nhu cong tat trong app (--tray). Go cai se xoa DUNG gia tri nay.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "QuanLyHangHoan"; ValueData: """{app}\QuanLyHangHoan.App.exe"" --tray"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\QuanLyHangHoan.App.exe"; Description: "Mở Quản Lý Hàng Hoàn ngay"; Flags: nowait postinstall skipifsilent

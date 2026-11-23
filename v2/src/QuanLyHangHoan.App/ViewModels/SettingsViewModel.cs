using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;

namespace QuanLyHangHoan.App.ViewModels;

public sealed record BackupRow(string Name, string TimeText, string SizeText);

/// <summary>
/// Màn "Cài đặt": gom các công tắc chạy nền/thông báo (trước đây nằm ở Trang chủ), mã phòng đồng bộ và SAO LƯU.
/// Bốn công tắc chạy nền được MainViewModel lưu và áp dụng (khay, registry, âm thanh): màn này chỉ phản chiếu và báo ngược qua callback.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    public static readonly int[] EveryHoursOptions = { 1, 2, 4, 6, 12, 24 };
    public static readonly int[] KeepDaysOptions = { 3, 7, 14, 30, 60, 90 };

    private readonly IBackupController _backup;
    private readonly IClipboardService? _clipboard;
    private readonly Func<string> _roomProvider;
    private readonly Func<DateTime> _clock;
    private readonly Action<string, bool>? _onBackgroundSettingChanged;
    private readonly Action<bool, int, int>? _onBackupSettingChanged;
    private readonly Action? _openPhoneSync;
    private bool _suppress;

    public SettingsViewModel(
        IBackupController backup,
        IClipboardService? clipboard,
        Func<string> roomProvider,
        Func<DateTime> clock,
        Action<string, bool>? onBackgroundSettingChanged = null,
        Action<bool, int, int>? onBackupSettingChanged = null,
        Action? openPhoneSync = null)
    {
        _backup = backup;
        _clipboard = clipboard;
        _roomProvider = roomProvider;
        _clock = clock;
        _onBackgroundSettingChanged = onBackgroundSettingChanged;
        _onBackupSettingChanged = onBackupSettingChanged;
        _openPhoneSync = openPhoneSync;
        _backup.Changed += OnBackupChanged;
        RefreshBackups();
        RefreshRoom();
    }

    public bool IsDemoMode { get; set; }

    // ---------------- chạy nền và thông báo ----------------
    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private bool _notifyAlerts = true;
    [ObservableProperty] private bool _sounds = true;

    public void SyncBackgroundSettings(bool autoStart, bool minimizeToTray, bool notifyAlerts, bool sounds)
    {
        _suppress = true;
        try { AutoStart = autoStart; MinimizeToTray = minimizeToTray; NotifyAlerts = notifyAlerts; Sounds = sounds; }
        finally { _suppress = false; }
    }

    partial void OnAutoStartChanged(bool value) { if (!_suppress) _onBackgroundSettingChanged?.Invoke(nameof(AutoStart), value); }
    partial void OnMinimizeToTrayChanged(bool value) { if (!_suppress) _onBackgroundSettingChanged?.Invoke(nameof(MinimizeToTray), value); }
    partial void OnNotifyAlertsChanged(bool value) { if (!_suppress) _onBackgroundSettingChanged?.Invoke(nameof(NotifyAlerts), value); }
    partial void OnSoundsChanged(bool value) { if (!_suppress) _onBackgroundSettingChanged?.Invoke(nameof(Sounds), value); }

    // ---------------- mã phòng ----------------
    [ObservableProperty] private string _roomCode = "";
    [ObservableProperty] private string _roomStatus = "";

    public void RefreshRoom() => RoomCode = _roomProvider() ?? "";

    [RelayCommand]
    private void CopyRoom()
    {
        if (string.IsNullOrWhiteSpace(RoomCode)) { RoomStatus = "Chưa có mã phòng."; return; }
        _clipboard?.SetText(RoomCode);
        RoomStatus = "Đã sao chép mã phòng. Dán vào app iPhone (màn Cài đặt).";
    }

    [RelayCommand]
    private void OpenPhoneSync() => _openPhoneSync?.Invoke();

    // ---------------- sao lưu ----------------
    [ObservableProperty] private bool _backupEnabled = true;
    [ObservableProperty] private int _backupEveryHours = 2;
    [ObservableProperty] private int _backupKeepDays = 7;
    [ObservableProperty] private string _latestBackupText = "Chưa có bản sao lưu nào.";
    [ObservableProperty] private string _backupStatus = "";
    [ObservableProperty] private bool _backupStatusIsError;
    [ObservableProperty] private bool _isBackupBusy;

    public ObservableCollection<BackupRow> Backups { get; } = new();
    public string BackupFolderPath => _backup.FolderPath;
    public bool HasBackups => Backups.Count > 0;
    public IReadOnlyList<int> EveryHoursChoices => WithValue(EveryHoursOptions, BackupEveryHours);
    public IReadOnlyList<int> KeepDaysChoices => WithValue(KeepDaysOptions, BackupKeepDays);

    private static IReadOnlyList<int> WithValue(int[] options, int value) =>
        options.Contains(value) ? options : options.Append(value).OrderBy(x => x).ToArray();

    public void SyncBackupSettings(bool enabled, int everyHours, int keepDays)
    {
        _suppress = true;
        try
        {
            BackupEnabled = enabled; BackupEveryHours = everyHours; BackupKeepDays = keepDays;
            OnPropertyChanged(nameof(EveryHoursChoices)); OnPropertyChanged(nameof(KeepDaysChoices));
        }
        finally { _suppress = false; }
    }

    partial void OnBackupEnabledChanged(bool value) => BackupSettingChanged();
    partial void OnBackupEveryHoursChanged(int value) => BackupSettingChanged();
    partial void OnBackupKeepDaysChanged(int value) => BackupSettingChanged();

    private void BackupSettingChanged()
    {
        if (_suppress) return;
        _onBackupSettingChanged?.Invoke(BackupEnabled, BackupEveryHours, BackupKeepDays);
    }

    private void OnBackupChanged() => RefreshBackups();

    public void RefreshBackups()
    {
        var list = _backup.List();
        Backups.Clear();
        foreach (var b in list)
            Backups.Add(new BackupRow(b.Name, b.Time.ToString("dd/MM/yyyy HH:mm", Vi), $"{b.Bytes / 1024.0 / 1024.0:N1} MB"));
        LatestBackupText = list.Count == 0
            ? "Chưa có bản sao lưu nào."
            : $"Bản gần nhất: {list[0].Time.ToString("dd/MM/yyyy HH:mm", Vi)} ({list[0].Bytes / 1024.0 / 1024.0:N1} MB), tổng {list.Count} bản.";
        OnPropertyChanged(nameof(HasBackups));
    }

    [RelayCommand]
    private async Task BackupNowAsync()
    {
        if (IsBackupBusy) return;
        IsBackupBusy = true;
        BackupStatus = "Đang sao lưu...";
        BackupStatusIsError = false;
        try
        {
            var r = await _backup.BackupNowAsync();
            BackupStatusIsError = !r.Ok;
            BackupStatus = r.Message;
            RefreshBackups();
        }
        catch (Exception ex)
        {
            BackupStatusIsError = true;
            BackupStatus = "Sao lưu lỗi: " + ex.Message;
        }
        finally { IsBackupBusy = false; }
    }

    [RelayCommand]
    private void OpenBackupFolder() => _backup.OpenFolder();

    public string RestoreHint =>
        "Muốn khôi phục: thoát hẳn app (chuột phải biểu tượng ở khay, chọn \"Thoát hẳn\"), chép một bản sao lưu đè lên data.db " +
        "(nên giữ lại bản data.db hiện tại trước), rồi mở lại app. App không tự khôi phục để tránh ghi đè dữ liệu.";
}

using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Data.Sync;

namespace QuanLyHangHoan.App.ViewModels;

public sealed partial class PhoneSyncViewModel : ObservableObject, IDisposable
{
    public const int MaxLogCount = 200;

    private readonly ISyncController _syncController;
    private readonly IClipboardService _clipboardService;
    private readonly IConfirmationService _confirmationService;
    private readonly Action<Action> _dispatchToUi;
    private readonly HashSet<string> _seenHandledKeys = new(StringComparer.Ordinal);

    public PhoneSyncViewModel(
        ISyncController syncController,
        IClipboardService? clipboardService = null,
        IConfirmationService? confirmationService = null,
        Action<Action>? dispatchToUi = null)
    {
        _syncController = syncController;
        _clipboardService = clipboardService ?? new WindowsClipboardService();
        _confirmationService = confirmationService ?? new WindowsConfirmationService();
        _dispatchToUi = dispatchToUi ?? DefaultDispatchToUi;

        _syncController.StatusChanged += OnControllerStatusChanged;
        _syncController.Handled += OnControllerHandled;

        RefreshStatusCore();
    }

    public ObservableCollection<PhoneSyncLogItem> Logs { get; } = new();

    public bool HasLogs => Logs.Count > 0;
    public bool IsDemoMode => _syncController.IsDemoMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewOrdersTodayText))]
    private int _newOrdersTodayCount;

    public string NewOrdersTodayText => $"Đơn mới từ điện thoại hôm nay: {NewOrdersTodayCount:N0}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRoomWeak))]
    private string _room = string.Empty;

    public bool IsRoomWeak => !SyncRoom.IsStrong(Room);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncToggleButtonText))]
    [NotifyPropertyChangedFor(nameof(SyncStateText))]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(ConnectionStatusText))]
    [NotifyPropertyChangedFor(nameof(ConnectionBgHex))]
    [NotifyPropertyChangedFor(nameof(ConnectionBorderHex))]
    [NotifyPropertyChangedFor(nameof(ConnectionFgHex))]
    [NotifyPropertyChangedFor(nameof(MqttBgHex))]
    [NotifyPropertyChangedFor(nameof(MqttBorderHex))]
    [NotifyPropertyChangedFor(nameof(MqttFgHex))]
    [NotifyPropertyChangedFor(nameof(NtfyStatusText))]
    [NotifyPropertyChangedFor(nameof(NtfyBgHex))]
    [NotifyPropertyChangedFor(nameof(NtfyBorderHex))]
    [NotifyPropertyChangedFor(nameof(NtfyFgHex))]
    private bool _syncEnabled;

    public string SyncToggleButtonText => SyncEnabled ? "Tắt đồng bộ" : "Bật đồng bộ";
    public string SyncStateText => SyncEnabled ? "Đang bật" : "Đang tắt";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(ConnectionStatusText))]
    [NotifyPropertyChangedFor(nameof(ConnectionBgHex))]
    [NotifyPropertyChangedFor(nameof(ConnectionBorderHex))]
    [NotifyPropertyChangedFor(nameof(ConnectionFgHex))]
    [NotifyPropertyChangedFor(nameof(NtfyStatusText))]
    [NotifyPropertyChangedFor(nameof(NtfyBgHex))]
    [NotifyPropertyChangedFor(nameof(NtfyBorderHex))]
    [NotifyPropertyChangedFor(nameof(NtfyFgHex))]
    private bool _ntfyEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(ConnectionStatusText))]
    [NotifyPropertyChangedFor(nameof(ConnectionBgHex))]
    [NotifyPropertyChangedFor(nameof(ConnectionBorderHex))]
    [NotifyPropertyChangedFor(nameof(ConnectionFgHex))]
    [NotifyPropertyChangedFor(nameof(MqttStatusText))]
    [NotifyPropertyChangedFor(nameof(MqttBgHex))]
    [NotifyPropertyChangedFor(nameof(MqttBorderHex))]
    [NotifyPropertyChangedFor(nameof(MqttFgHex))]
    private int _connectedBrokers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionStatusText))]
    [NotifyPropertyChangedFor(nameof(MqttStatusText))]
    private int _totalBrokers = 3;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(ConnectionStatusText))]
    [NotifyPropertyChangedFor(nameof(ConnectionBgHex))]
    [NotifyPropertyChangedFor(nameof(ConnectionBorderHex))]
    [NotifyPropertyChangedFor(nameof(ConnectionFgHex))]
    [NotifyPropertyChangedFor(nameof(NtfyStatusText))]
    [NotifyPropertyChangedFor(nameof(NtfyBgHex))]
    [NotifyPropertyChangedFor(nameof(NtfyBorderHex))]
    [NotifyPropertyChangedFor(nameof(NtfyFgHex))]
    private bool _ntfyConnected;

    public bool IsConnected => SyncEnabled && (ConnectedBrokers >= 1 || (NtfyEnabled && NtfyConnected));

    public string ConnectionStatusText
    {
        get
        {
            if (!SyncEnabled) return "Đồng bộ đang tắt";
            if (ConnectedBrokers >= 1 && NtfyEnabled && NtfyConnected) return "Đang kết nối (MQTT + ntfy)";
            if (ConnectedBrokers >= 1) return $"Đang kết nối (MQTT {ConnectedBrokers}/{TotalBrokers})";
            if (NtfyEnabled && NtfyConnected) return "Đang kết nối (ntfy)";
            return "Mất kết nối (0 đường)";
        }
    }

    public string ConnectionBgHex => IsConnected ? "#DCFCE7" : "#FEE2E2";
    public string ConnectionBorderHex => IsConnected ? "#86EFAC" : "#FCA5A5";
    public string ConnectionFgHex => IsConnected ? "#15803D" : "#B91C1C";

    public string MqttStatusText => $"MQTT: {ConnectedBrokers}/{TotalBrokers} broker";
    public string MqttBgHex => !SyncEnabled ? "#F1F5F9" : (ConnectedBrokers >= 1 ? "#DCFCE7" : "#FEE2E2");
    public string MqttBorderHex => !SyncEnabled ? "#CBD5E1" : (ConnectedBrokers >= 1 ? "#86EFAC" : "#FCA5A5");
    public string MqttFgHex => !SyncEnabled ? "#475569" : (ConnectedBrokers >= 1 ? "#15803D" : "#B91C1C");

    public string NtfyStatusText
    {
        get
        {
            if (!SyncEnabled || !NtfyEnabled) return "ntfy: Đã tắt";
            return NtfyConnected ? "ntfy: Đã kết nối" : "ntfy: Mất kết nối";
        }
    }

    public string NtfyBgHex => (!SyncEnabled || !NtfyEnabled) ? "#F1F5F9" : (NtfyConnected ? "#DCFCE7" : "#FEE2E2");
    public string NtfyBorderHex => (!SyncEnabled || !NtfyEnabled) ? "#CBD5E1" : (NtfyConnected ? "#86EFAC" : "#FCA5A5");
    public string NtfyFgHex => (!SyncEnabled || !NtfyEnabled) ? "#475569" : (NtfyConnected ? "#15803D" : "#B91C1C");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMqttLastError))]
    private string _mqttLastError = string.Empty;

    public bool HasMqttLastError => !string.IsNullOrWhiteSpace(MqttLastError);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNtfyLastError))]
    private string _ntfyLastError = string.Empty;

    public bool HasNtfyLastError => !string.IsNullOrWhiteSpace(NtfyLastError);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastError))]
    private string _lastError = string.Empty;

    public bool HasLastError => !string.IsNullOrWhiteSpace(LastError);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCopiedNotice))]
    private string _copiedNotice = string.Empty;

    public bool HasCopiedNotice => !string.IsNullOrWhiteSpace(CopiedNotice);

    [RelayCommand]
    private void CopyRoom()
    {
        if (string.IsNullOrWhiteSpace(Room)) return;
        _clipboardService.SetText(Room);
        CopiedNotice = "Đã sao chép mã phòng";
    }

    [RelayCommand]
    private void RegenerateRoom()
    {
        var confirmed = _confirmationService.Confirm(
            "Tạo mã phòng mới",
            "Điện thoại phải nhập lại mã mới. Bạn có chắc chắn muốn tạo mã phòng mới?");
        if (!confirmed) return;

        CopiedNotice = string.Empty;
        _syncController.RegenerateRoom();
    }

    [RelayCommand]
    private void CreateStrongRoom()
    {
        CopiedNotice = string.Empty;
        _syncController.RegenerateRoom();
    }

    [RelayCommand]
    private void ToggleSync()
    {
        _syncController.SetSyncEnabled(!SyncEnabled);
    }

    [ObservableProperty]
    private string _privateBrokerText = string.Empty;

    [RelayCommand]
    private void SavePrivateBroker()
    {
        _syncController.SetPrivateBroker(PrivateBrokerText);
        PrivateBrokerText = _syncController.PrivateBroker;
    }

    [RelayCommand]
    private void ClearPrivateBroker()
    {
        PrivateBrokerText = string.Empty;
        _syncController.SetPrivateBroker(string.Empty);
    }

    [RelayCommand]
    private void ToggleNtfy()
    {
        _syncController.SetNtfyEnabled(!NtfyEnabled);
    }

    [RelayCommand]
    private void EmitDemoSamples()
    {
        if (_syncController is FakeSyncController fake)
        {
            fake.EmitDemoSampleEvents();
        }
    }

    private void OnControllerStatusChanged()
    {
        _dispatchToUi(RefreshStatusCore);
    }

    private void OnControllerHandled(SyncHandled item)
    {
        _dispatchToUi(() => AppendLogCore(item));
    }

    private void RefreshStatusCore()
    {
        Room = _syncController.Room;
        SyncEnabled = _syncController.SyncEnabled;
        NtfyEnabled = _syncController.NtfyEnabled;
        ConnectedBrokers = _syncController.ConnectedBrokers;
        TotalBrokers = _syncController.TotalBrokers;
        NtfyConnected = _syncController.NtfyConnected;
        MqttLastError = _syncController.MqttLastError;
        NtfyLastError = _syncController.NtfyLastError;
        LastError = _syncController.LastError;
        if (string.IsNullOrEmpty(PrivateBrokerText)) PrivateBrokerText = _syncController.PrivateBroker;
    }

    private void AppendLogCore(SyncHandled item)
    {
        if (!string.IsNullOrWhiteSpace(item.RequestId))
        {
            var dedupKey = $"{item.RequestId}\0{item.Stage}";
            if (!_seenHandledKeys.Add(dedupKey))
            {
                return;
            }
        }

        var isNewOrder = string.Equals(item.Kind, "new_order", StringComparison.OrdinalIgnoreCase);
        var isSuccess = string.Equals(item.Outcome, "success", StringComparison.OrdinalIgnoreCase);
        if (isNewOrder && isSuccess)
        {
            NewOrdersTodayCount++;
        }

        var stageLabel = ResolveStageLabel(item.Stage, item.Kind);

        var (label, bg, border, fg) = (item.Outcome?.ToLowerInvariant()) switch
        {
            "success" when isNewOrder => ("THÀNH CÔNG", "#DCFCE7", "#16A34A", "#15803D"),
            "success" when string.Equals(item.Stage, "scan", StringComparison.OrdinalIgnoreCase) => ("ĐÃ NHẬN", "#DCFCE7", "#16A34A", "#15803D"),
            "success" => ("THÀNH CÔNG", "#DCFCE7", "#16A34A", "#15803D"),
            "duplicate" => ("TRÙNG", "#FEF3C7", "#D97706", "#B45309"),
            "ambiguous" => ("NHIỀU KIỆN KHỚP", "#FEE2E2", "#DC2626", "#B91C1C"),
            "not_found" => ("KHÔNG TÌM THẤY", "#FEE2E2", "#DC2626", "#B91C1C"),
            "invalid" => ("KHÔNG HỢP LỆ", "#FEE2E2", "#DC2626", "#B91C1C"),
            "locked" => ("BỊ KHÓA", "#FEE2E2", "#DC2626", "#B91C1C"),
            _ => ("LỖI", "#FEE2E2", "#DC2626", "#B91C1C")
        };

        Logs.Insert(0, new PhoneSyncLogItem(
            item.At.ToString("HH:mm:ss"),
            item.Code,
            item.Outcome ?? string.Empty,
            label,
            string.IsNullOrWhiteSpace(item.Message) ? item.RequestId : item.Message,
            bg,
            border,
            fg,
            stageLabel,
            isNewOrder));

        while (Logs.Count > MaxLogCount)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }

        OnPropertyChanged(nameof(HasLogs));
    }

    private static string ResolveStageLabel(string? stage, string? kind)
    {
        if (string.Equals(kind, "new_order", StringComparison.OrdinalIgnoreCase))
        {
            return "Đơn mới";
        }

        return (stage ?? string.Empty).ToLowerInvariant() switch
        {
            "scan" => "Nhận kiện",
            "note" => "Ghi chú",
            "edit" => "Sửa trạng thái",
            "delete" => "Xóa",
            "photo" => "Ảnh",
            "push" => "Đẩy đơn",
            _ => string.IsNullOrWhiteSpace(stage) ? "Khác" : stage
        };
    }

    private static void DefaultDispatchToUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null
            && !dispatcher.HasShutdownStarted
            && !dispatcher.HasShutdownFinished
            && dispatcher.Thread.IsAlive
            && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    public void Dispose()
    {
        _syncController.StatusChanged -= OnControllerStatusChanged;
        _syncController.Handled -= OnControllerHandled;
    }
}

public sealed record PhoneSyncLogItem(
    string TimeText,
    string Code,
    string Outcome,
    string OutcomeLabel,
    string Message,
    string BgHex,
    string BorderHex,
    string FgHex,
    string StageLabel = "Nhận kiện",
    bool IsNewOrder = false);

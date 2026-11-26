using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.App.Services;

namespace QuanLyHangHoan.App.ViewModels;

/// <summary>
/// ViewModel tập trung cho trạng thái Cloud: chấm trạng thái ở thanh trên, popup chi tiết,
/// banner "chỉ xem" khi mất kết nối, hạn mức trong ngày và thẻ Cài đặt &gt; Cloud (ghép / tạo mã / quên ghép).
/// </summary>
public sealed partial class CloudStatusViewModel : ObservableObject, IDisposable
{
    public const string QuotaStopMessage = "Hết hạn mức hôm nay: thao tác ghi tạm dừng tới 07:00 sáng mai (giờ Việt Nam)";

    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");

    private readonly ICloudLink? _link;
    private readonly WriteGate _writeGate;
    private readonly IConfirmationService _confirmationService;
    private readonly IClipboardService? _clipboardService;
    private readonly Action<Action> _dispatchToUi;
    private readonly Func<DateTime> _clock;
    private readonly Action? _openCloudSettings;
    private readonly Action<string, bool>? _onNotify;
    private Timer? _countdownTimer;
    private bool _disposed;

    public CloudStatusViewModel(
        ICloudLink? cloudLink,
        WriteGate writeGate,
        IConfirmationService? confirmationService = null,
        IClipboardService? clipboardService = null,
        Action<Action>? dispatchToUi = null,
        Func<DateTime>? clock = null,
        Action? openCloudSettings = null,
        Action<string, bool>? onNotify = null)
    {
        _link = cloudLink;
        _writeGate = writeGate ?? throw new ArgumentNullException(nameof(writeGate));
        _confirmationService = confirmationService ?? new WindowsConfirmationService();
        _clipboardService = clipboardService;
        _dispatchToUi = dispatchToUi ?? (a => a());
        _clock = clock ?? (() => DateTime.Now);
        _openCloudSettings = openCloudSettings;
        _onNotify = onNotify;

        _deviceNameInput = string.IsNullOrWhiteSpace(Environment.MachineName) ? "Máy tính kho" : Environment.MachineName;

        if (_link is not null)
        {
            if (!string.IsNullOrWhiteSpace(_link.BaseUrl))
            {
                _workerUrlInput = _link.BaseUrl;
            }
            if (!string.IsNullOrWhiteSpace(_link.DeviceName))
            {
                _deviceNameInput = _link.DeviceName;
            }
            _link.Changed += OnLinkChanged;
        }

        RefreshFromLink();
    }

    public bool IsEnabled => _link is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusColorKey))]
    [NotifyPropertyChangedFor(nameof(IsDisconnectedProminent))]
    [NotifyPropertyChangedFor(nameof(ShowPairedInfo))]
    [NotifyPropertyChangedFor(nameof(ShowPairForm))]
    [NotifyPropertyChangedFor(nameof(IsRevoked))]
    [NotifyPropertyChangedFor(nameof(ShowBannerSyncButton))]
    [NotifyPropertyChangedFor(nameof(ShowBannerSettingsButton))]
    [NotifyCanExecuteChangedFor(nameof(SyncNowCommand))]
    [NotifyCanExecuteChangedFor(nameof(UnpairCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartPairingCommand))]
    private CloudState _state = CloudState.Unpaired;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocked))]
    [NotifyPropertyChangedFor(nameof(ShowReadOnlyBanner))]
    private bool _canWrite = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastError))]
    private string _lastError = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastSyncText))]
    [NotifyPropertyChangedFor(nameof(ReadOnlyBannerText))]
    private DateTime? _lastSyncAt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuota))]
    [NotifyPropertyChangedFor(nameof(QuotaLevel))]
    [NotifyPropertyChangedFor(nameof(IsQuotaWarn))]
    [NotifyPropertyChangedFor(nameof(IsQuotaStop))]
    [NotifyPropertyChangedFor(nameof(QuotaColorKey))]
    [NotifyPropertyChangedFor(nameof(RequestsPercent))]
    [NotifyPropertyChangedFor(nameof(RowsWrittenPercent))]
    [NotifyPropertyChangedFor(nameof(RequestsQuotaText))]
    [NotifyPropertyChangedFor(nameof(RowsWrittenQuotaText))]
    [NotifyPropertyChangedFor(nameof(QuotaDayText))]
    [NotifyPropertyChangedFor(nameof(QuotaWarningText))]
    [NotifyPropertyChangedFor(nameof(HasQuotaWarning))]
    [NotifyPropertyChangedFor(nameof(ShowStatusQuotaBadge))]
    [NotifyPropertyChangedFor(nameof(StatusTooltip))]
    private CloudQuota? _quota;

    [ObservableProperty]
    private string _deviceName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoleDisplayText))]
    [NotifyPropertyChangedFor(nameof(IsOwner))]
    [NotifyPropertyChangedFor(nameof(ShowOwnerTools))]
    [NotifyCanExecuteChangedFor(nameof(StartPairingCommand))]
    private string _role = string.Empty;

    [ObservableProperty]
    private string _baseUrl = string.Empty;

    [ObservableProperty]
    private bool _isPopupOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncNowCommand))]
    private bool _isSyncingNow;

    // ---- Ghép thiết bị (Cài đặt > Cloud) ----

    [ObservableProperty]
    private string _workerUrlInput = string.Empty;

    [ObservableProperty]
    private string _pairingCodeInput = string.Empty;

    [ObservableProperty]
    private string _deviceNameInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PairCommand))]
    private bool _isPairing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPairError))]
    private string _pairError = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPairSuccess))]
    private string _pairSuccess = string.Empty;

    // ---- Tạo mã ghép cho máy khác (chỉ owner) ----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartPairingCommand))]
    private bool _isGeneratingCode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGeneratedPairCode))]
    private string _generatedPairCode = string.Empty;

    [ObservableProperty]
    private DateTime? _generatedPairExpiresAt;

    [ObservableProperty]
    private string _generatedPairCountdownText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGenerateCodeError))]
    private string _generateCodeError = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCopyCodeNotice))]
    private string _copyCodeNotice = string.Empty;

    // ---- Thuộc tính suy diễn cho giao diện & test ----

    public bool IsLocked => IsEnabled && !CanWrite;
    public bool ShowReadOnlyBanner => IsEnabled && !CanWrite;
    public bool HasLastError => !string.IsNullOrWhiteSpace(LastError);
    public bool HasPairError => !string.IsNullOrWhiteSpace(PairError);
    public bool HasPairSuccess => !string.IsNullOrWhiteSpace(PairSuccess);
    public bool HasGeneratedPairCode => !string.IsNullOrWhiteSpace(GeneratedPairCode);
    public bool HasGenerateCodeError => !string.IsNullOrWhiteSpace(GenerateCodeError);
    public bool HasCopyCodeNotice => !string.IsNullOrWhiteSpace(CopyCodeNotice);

    public bool IsRevoked => State == CloudState.Revoked;
    public bool ShowPairedInfo => IsEnabled && State != CloudState.Unpaired;
    public bool ShowPairForm => IsEnabled && State is CloudState.Unpaired or CloudState.Revoked;
    /// <summary>Thẻ "Tạo mã ghép" + "Thiết bị đã ghép": chỉ chủ cửa hàng trên bản máy CHÍNH (bản máy phụ không bao giờ hiện).</summary>
    public bool ShowOwnerTools => IsOwner && !AppEdition.IsSecondary;

    public bool IsOwner => IsEnabled
        && State is not (CloudState.Unpaired or CloudState.Revoked)
        && string.Equals(Role, "owner", StringComparison.OrdinalIgnoreCase);

    public bool ShowBannerSyncButton => State is CloudState.Disconnected or CloudState.Connecting;
    public bool ShowBannerSettingsButton => State is CloudState.Unpaired or CloudState.Revoked;

    public string StatusText => State switch
    {
        CloudState.Connected => "Đã kết nối",
        CloudState.Syncing => "Đang đồng bộ",
        CloudState.Connecting => "Đang kết nối...",
        CloudState.Disconnected => "MẤT KẾT NỐI",
        CloudState.Unpaired => "Chưa ghép thiết bị",
        CloudState.Revoked => "Thiết bị đã bị thu hồi",
        _ => "Chưa ghép thiết bị"
    };

    /// <summary>Khóa màu ngữ nghĩa cho XAML và test: Green | Blue | Yellow | Red | Gray.</summary>
    public string StatusColorKey => State switch
    {
        CloudState.Connected => "Green",
        CloudState.Syncing => "Blue",
        CloudState.Connecting => "Yellow",
        CloudState.Disconnected => "Red",
        CloudState.Revoked => "Red",
        _ => "Gray"
    };

    public bool IsDisconnectedProminent => State is CloudState.Disconnected or CloudState.Revoked;

    public string LastSyncText => LastSyncAt is DateTime dt
        ? dt.ToString("dd/MM/yyyy HH:mm:ss", ViCulture)
        : "Chưa đồng bộ lần nào";

    public string DeviceNameDisplay => string.IsNullOrWhiteSpace(DeviceName) ? "—" : DeviceName;
    public string BaseUrlDisplay => string.IsNullOrWhiteSpace(BaseUrl) ? "—" : BaseUrl;

    public string RoleDisplayText => Role.ToLowerInvariant() switch
    {
        "owner" => "Chủ cửa hàng (owner)",
        "staff" => "Nhân viên (staff)",
        _ => "—"
    };

    public string ReadOnlyBannerText => State switch
    {
        CloudState.Disconnected => LastSyncAt is DateTime dt
            ? $"Mất kết nối cloud. Đang xem dữ liệu cũ lúc {dt.ToString("HH:mm", ViCulture)}. Mọi thao tác ghi bị tạm khóa."
            : "Mất kết nối cloud. Đang xem dữ liệu cũ (chưa đồng bộ). Mọi thao tác ghi bị tạm khóa.",
        CloudState.Connecting => LastSyncAt is DateTime dt
            ? $"Đang kết nối cloud... Đang xem dữ liệu cũ lúc {dt.ToString("HH:mm", ViCulture)}. Mọi thao tác ghi bị tạm khóa."
            : "Đang kết nối cloud... Mọi thao tác ghi bị tạm khóa.",
        CloudState.Unpaired => "Chưa ghép thiết bị với cloud. Vào Cài đặt → Cloud để ghép thiết bị. Mọi thao tác ghi bị tạm khóa.",
        CloudState.Revoked => "Thiết bị đã bị thu hồi quyền truy cập cloud. Vui lòng ghép lại trong Cài đặt → Cloud. Mọi thao tác ghi bị tạm khóa.",
        _ => string.Empty
    };

    // ---- Hạn mức trong ngày (mục 5) ----

    public bool HasQuota => Quota is not null;
    public string QuotaLevel => (Quota?.Level ?? "ok").Trim().ToLowerInvariant();
    public bool IsQuotaWarn => HasQuota && QuotaLevel == "warn";
    public bool IsQuotaStop => HasQuota && QuotaLevel == "stop";
    public bool ShowStatusQuotaBadge => IsQuotaWarn || IsQuotaStop;

    /// <summary>Khóa màu hạn mức cho XAML và test: Green | Yellow | Red.</summary>
    public string QuotaColorKey => QuotaLevel switch
    {
        "stop" => "Red",
        "warn" => "Yellow",
        _ => "Green"
    };

    public double RequestsPercent => Quota is null ? 0 : Math.Clamp(Quota.RequestsPercent, 0, 100);
    public double RowsWrittenPercent => Quota is null ? 0 : Math.Clamp(Quota.RowsWrittenPercent, 0, 100);

    public string RequestsQuotaText => Quota is null
        ? "—"
        : FormatQuotaLine(Quota.RequestsUsed, Quota.RequestsLimit, Quota.RequestsPercent, "yêu cầu");

    public string RowsWrittenQuotaText => Quota is null
        ? "—"
        : FormatQuotaLine(Quota.RowsWrittenUsed, Quota.RowsWrittenLimit, Quota.RowsWrittenPercent, "hàng ghi");

    public string QuotaDayText => Quota is null || string.IsNullOrWhiteSpace(Quota.Day)
        ? "Hạn mức hôm nay"
        : $"Hạn mức ngày {Quota.Day} (ước lượng)";

    public string QuotaWarningText => QuotaLevel switch
    {
        "stop" => QuotaStopMessage,
        "warn" => "Đã dùng quá 70% hạn mức hôm nay — hạn chế nạp hàng loạt không cần thiết.",
        _ => string.Empty
    };

    public bool HasQuotaWarning => !string.IsNullOrEmpty(QuotaWarningText);

    public string StatusTooltip
    {
        get
        {
            if (!IsEnabled) return string.Empty;
            var lines = new List<string>
            {
                $"Cloud: {StatusText}",
                $"Đồng bộ cuối: {LastSyncText}"
            };
            if (HasLastError)
            {
                lines.Add($"Lỗi gần nhất: {LastError}");
            }
            if (Quota is not null)
            {
                lines.Add($"Hạn mức hôm nay: {RequestsQuotaText} • {RowsWrittenQuotaText}");
                if (IsQuotaStop)
                {
                    lines.Add(QuotaStopMessage);
                }
            }
            lines.Add("Bấm để xem chi tiết / đồng bộ ngay.");
            return string.Join(Environment.NewLine, lines);
        }
    }

    public static string FormatQuotaLine(long used, long limit, double percent, string unit)
    {
        var pct = Math.Round(percent, MidpointRounding.AwayFromZero);
        return $"{used.ToString("N0", ViCulture)} / {limit.ToString("N0", ViCulture)} {unit} ({pct:0}%)";
    }

    // ---- Commands ----

    [RelayCommand]
    public void TogglePopup()
    {
        if (!IsEnabled) return;
        IsPopupOpen = !IsPopupOpen;
    }

    [RelayCommand]
    public void ClosePopup() => IsPopupOpen = false;

    [RelayCommand]
    public void OpenCloudSettings()
    {
        IsPopupOpen = false;
        _openCloudSettings?.Invoke();
    }

    private bool CanSyncNow => IsEnabled
        && !IsSyncingNow
        && State is not (CloudState.Unpaired or CloudState.Revoked);

    [RelayCommand(CanExecute = nameof(CanSyncNow))]
    public async Task SyncNowAsync()
    {
        if (_link is null || !CanSyncNow) return;
        IsSyncingNow = true;
        try
        {
            await _link.SyncNowAsync();
        }
        catch (Exception ex)
        {
            _onNotify?.Invoke("Đồng bộ cloud thất bại: " + ex.Message, true);
        }
        finally
        {
            IsSyncingNow = false;
            RefreshFromLink();
        }
    }

    private bool CanPair => IsEnabled && !IsPairing;

    [RelayCommand(CanExecute = nameof(CanPair))]
    public async Task PairAsync()
    {
        if (_link is null || !CanPair) return;

        var url = (WorkerUrlInput ?? string.Empty).Trim();
        var code = (PairingCodeInput ?? string.Empty).Trim();
        var name = (DeviceNameInput ?? string.Empty).Trim();

        PairError = string.Empty;
        PairSuccess = string.Empty;

        if (string.IsNullOrWhiteSpace(url))
        {
            PairError = "Vui lòng nhập địa chỉ Worker (ví dụ https://ten-worker.workers.dev).";
            return;
        }
        if (string.IsNullOrWhiteSpace(code))
        {
            PairError = "Vui lòng nhập mã ghép 6 số.";
            return;
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            PairError = "Vui lòng nhập tên thiết bị.";
            return;
        }

        IsPairing = true;
        try
        {
            var error = await _link.PairAsync(url, code, name);
            if (!string.IsNullOrWhiteSpace(error))
            {
                PairError = error;
                PairSuccess = string.Empty;
            }
            else
            {
                PairError = string.Empty;
                PairingCodeInput = string.Empty;
                PairSuccess = "Đã ghép thiết bị thành công.";
                _onNotify?.Invoke("Đã ghép thiết bị với cloud thành công.", false);
            }
        }
        catch (Exception ex)
        {
            PairError = "Không ghép được thiết bị: " + ex.Message;
            PairSuccess = string.Empty;
        }
        finally
        {
            IsPairing = false;
            RefreshFromLink();
        }
    }

    private bool CanUnpair => IsEnabled && State != CloudState.Unpaired;

    // ---------------------------------------------------------------- thiết bị đã ghép (chủ cửa hàng)
    public System.Collections.ObjectModel.ObservableCollection<CloudDeviceRow> Devices { get; } = new();
    [ObservableProperty] private string _devicesStatus = string.Empty;

    [RelayCommand]
    public async Task RefreshDevicesAsync()
    {
        Devices.Clear();
        if (_link is null || !IsOwner) { DevicesStatus = string.Empty; return; }
        var (list, error) = await _link.ListDevicesAsync();
        foreach (var d in list) Devices.Add(CloudDeviceRow.From(d));
        DevicesStatus = error ?? $"{list.Count(d => !d.Revoked)} thiết bị đang dùng, {list.Count(d => d.Revoked)} đã thu hồi.";
    }

    [RelayCommand]
    public async Task RevokeDeviceAsync(CloudDeviceRow? row)
    {
        if (_link is null || row is null || !row.CanRevoke) return;
        var ok = _confirmationService.Confirm("Thu hồi thiết bị",
            $"Thu hồi \"{row.Name}\"?\n\nMáy này sẽ mất quyền vào cloud NGAY (không xem, không quét được). Muốn dùng lại phải ghép lại bằng mật khẩu cửa hàng.");
        if (!ok) return;
        var err = await _link.RevokeDeviceAsync(row.DeviceId);
        if (err is not null) { DevicesStatus = err; return; }
        var i = Devices.IndexOf(row);
        if (i >= 0) Devices[i] = row with { Revoked = true };
        _onNotify?.Invoke($"Đã thu hồi \"{row.Name}\".", false);
    }

    [RelayCommand(CanExecute = nameof(CanUnpair))]
    public void Unpair()
    {
        if (_link is null || !CanUnpair) return;

        var confirmed = _confirmationService.Confirm(
            "Quên ghép trên máy này",
            "Xóa thông tin ghép cloud trên máy tính này?\n\nBạn sẽ cần nhập mã ghép mới để kết nối lại. Thao tác này không thu hồi thiết bị trên cloud.");
        if (!confirmed) return;

        StopCountdownTimer();
        GeneratedPairCode = string.Empty;
        GeneratedPairExpiresAt = null;
        GeneratedPairCountdownText = string.Empty;
        GenerateCodeError = string.Empty;
        PairError = string.Empty;
        PairSuccess = string.Empty;

        _link.Unpair();
        RefreshFromLink();
        _onNotify?.Invoke("Đã xóa thông tin ghép cloud trên máy này.", false);
    }

    private bool CanStartPairing => IsOwner && !IsGeneratingCode;

    [RelayCommand(CanExecute = nameof(CanStartPairing))]
    public async Task StartPairingAsync()
    {
        if (_link is null || !CanStartPairing) return;

        IsGeneratingCode = true;
        GenerateCodeError = string.Empty;
        CopyCodeNotice = string.Empty;
        try
        {
            var (code, expiresAt, error) = await _link.StartPairingAsync();
            if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code))
            {
                GenerateCodeError = string.IsNullOrWhiteSpace(error) ? "Không tạo được mã ghép." : error;
                GeneratedPairCode = string.Empty;
                GeneratedPairExpiresAt = null;
                GeneratedPairCountdownText = string.Empty;
                StopCountdownTimer();
                return;
            }

            GeneratedPairCode = code;
            GeneratedPairExpiresAt = expiresAt ?? _clock().AddMinutes(10);
            UpdatePairingCountdown();
            StartCountdownTimer();
        }
        catch (Exception ex)
        {
            GenerateCodeError = "Không tạo được mã ghép: " + ex.Message;
            GeneratedPairCode = string.Empty;
            StopCountdownTimer();
        }
        finally
        {
            IsGeneratingCode = false;
        }
    }

    [RelayCommand]
    public void CopyGeneratedCode()
    {
        if (string.IsNullOrWhiteSpace(GeneratedPairCode)) return;
        _clipboardService?.SetText(GeneratedPairCode);
        CopyCodeNotice = "Đã sao chép mã ghép 6 số.";
    }

    /// <summary>Cập nhật chữ đếm ngược hết hạn mã ghép (gọi mỗi giây từ timer hoặc trực tiếp trong test).</summary>
    public void UpdatePairingCountdown()
    {
        if (GeneratedPairExpiresAt is not DateTime exp || string.IsNullOrWhiteSpace(GeneratedPairCode))
        {
            GeneratedPairCountdownText = string.Empty;
            StopCountdownTimer();
            return;
        }

        var remaining = exp - _clock();
        if (remaining <= TimeSpan.Zero)
        {
            GeneratedPairCountdownText = "Mã đã hết hạn — bấm tạo mã mới";
            StopCountdownTimer();
            return;
        }

        var mins = (int)remaining.TotalMinutes;
        var secs = remaining.Seconds;
        GeneratedPairCountdownText = $"Hết hạn sau {mins:00}:{secs:00}";
    }

    private void StartCountdownTimer()
    {
        StopCountdownTimer();
        _countdownTimer = new Timer(_ =>
        {
            _dispatchToUi(() =>
            {
                if (!_disposed) UpdatePairingCountdown();
            });
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void StopCountdownTimer()
    {
        var timer = Interlocked.Exchange(ref _countdownTimer, null);
        timer?.Dispose();
    }

    private void OnLinkChanged()
    {
        _dispatchToUi(() =>
        {
            if (!_disposed) RefreshFromLink();
        });
    }

    public void RefreshFromLink()
    {
        if (_link is null)
        {
            State = CloudState.Unpaired;
            CanWrite = true;
            LastError = string.Empty;
            LastSyncAt = null;
            Quota = null;
            DeviceName = string.Empty;
            Role = string.Empty;
            BaseUrl = string.Empty;
            _writeGate.Set(true);
            OnPropertyChanged(nameof(ReadOnlyBannerText));
            OnPropertyChanged(nameof(StatusTooltip));
            return;
        }

        State = _link.State;
        CanWrite = _link.CanWrite;
        LastError = _link.LastError ?? string.Empty;
        LastSyncAt = _link.LastSyncAt;
        Quota = _link.Quota;
        DeviceName = _link.DeviceName ?? string.Empty;
        Role = _link.Role ?? string.Empty;
        BaseUrl = _link.BaseUrl ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(WorkerUrlInput))
        {
            WorkerUrlInput = BaseUrl;
        }
        if (!string.IsNullOrWhiteSpace(DeviceName) && string.IsNullOrWhiteSpace(DeviceNameInput))
        {
            DeviceNameInput = DeviceName;
        }

        if (!IsOwner && HasGeneratedPairCode)
        {
            StopCountdownTimer();
            GeneratedPairCode = string.Empty;
            GeneratedPairExpiresAt = null;
            GeneratedPairCountdownText = string.Empty;
        }

        var lockReason = State switch
        {
            CloudState.Disconnected => WriteGate.DefaultLockedReason,
            CloudState.Connecting => "Đang kết nối cloud — chỉ xem được",
            CloudState.Unpaired => "Chưa ghép thiết bị với cloud — chỉ xem được",
            CloudState.Revoked => "Thiết bị đã bị thu hồi — chỉ xem được",
            _ => WriteGate.DefaultLockedReason
        };

        _writeGate.Set(CanWrite, lockReason);

        OnPropertyChanged(nameof(DeviceNameDisplay));
        OnPropertyChanged(nameof(BaseUrlDisplay));
        OnPropertyChanged(nameof(ReadOnlyBannerText));
        OnPropertyChanged(nameof(StatusTooltip));
        SyncNowCommand.NotifyCanExecuteChanged();
        UnpairCommand.NotifyCanExecuteChanged();
        StartPairingCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_link is not null)
        {
            _link.Changed -= OnLinkChanged;
        }
        StopCountdownTimer();
    }
}

/// <summary>Một dòng trong danh sách thiết bị đã ghép.</summary>
public sealed record CloudDeviceRow(string DeviceId, string Name, string Kind, string Role, long LastSeenAt, bool Revoked)
{
    public static CloudDeviceRow From(QuanLyHangHoan.Data.Cloud.CloudDeviceInfo d) => new(d.DeviceId, d.DeviceName, d.Kind, d.Role, d.LastSeenAt, d.Revoked);
    public string KindText => (Kind == "phone" ? "📱 Điện thoại" : "💻 Máy tính") + (Role == "owner" ? " · Chủ cửa hàng" : " · Nhân viên");
    public string StatusText => Revoked ? "Đã thu hồi"
        : LastSeenAt > 0 ? "Hoạt động lần cuối " + DateTimeOffset.FromUnixTimeMilliseconds(LastSeenAt).LocalDateTime.ToString("dd/MM HH:mm") : "Chưa hoạt động";
    /// <summary>Không thu hồi máy chủ cửa hàng và máy đã thu hồi.</summary>
    public bool CanRevoke => !Revoked && Role != "owner";
}

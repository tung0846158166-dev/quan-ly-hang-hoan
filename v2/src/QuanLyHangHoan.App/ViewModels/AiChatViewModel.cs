using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Ai;

namespace QuanLyHangHoan.App.ViewModels;

public sealed record AiChatMessage(bool IsUser, string Text, IReadOnlyList<AiOrderRef> Refs, string Trace)
{
    /// <summary>Thời điểm gửi (câu hỏi) hoặc nhận (câu trả lời).</summary>
    public DateTime Time { get; init; } = DateTime.Now;
    /// <summary>Với câu trả lời: thời gian chờ AI.</summary>
    public TimeSpan Elapsed { get; init; }
    /// <summary>Lỗi (hiện màu cảnh báo, không có danh sách đơn).</summary>
    public bool IsError => !IsUser && Text.StartsWith("⚠", StringComparison.Ordinal);

    public bool HasRefs => Refs.Count > 0;
    public string RefsHeader => $"Đơn liên quan ({Refs.Count}) — bấm để xem chi tiết";

    /// <summary>Giờ hiển thị: hôm nay chỉ giờ:phút:giây, ngày khác thêm ngày/tháng.</summary>
    public string TimeText => Time.Date == DateTime.Today ? Time.ToString("HH:mm:ss") : Time.ToString("dd/MM HH:mm:ss");
    public string ElapsedText => Elapsed.TotalSeconds >= 0.5 ? $"trả lời sau {Elapsed.TotalSeconds:0.#} giây" : string.Empty;
    public string Caption => IsUser ? TimeText : (ElapsedText.Length > 0 ? $"{TimeText} · {ElapsedText}" : TimeText);
}

/// <summary>
/// Màn "Hỏi AI": hỏi đáp về dữ liệu đơn hàng. AI chỉ gọi công cụ CHỈ ĐỌC (số liệu do mã tính), dữ liệu gửi đi đã ẩn tên/SĐT/địa chỉ người nhận.
/// Dùng nhiều mô hình miễn phí theo thứ tự (Groq rồi Google Gemini), hết hạn mức mô hình này thì tự chuyển sang mô hình kế tiếp.
/// Khóa API mã hóa DPAPI. Đơn trong câu trả lời bấm được để mở cửa sổ chi tiết đơn.
/// </summary>
public sealed partial class AiChatViewModel : ObservableObject
{
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(90) };

    private readonly AiSettingsStore? _store;
    private readonly Func<IReadOnlyList<StoredOrder>>? _loader;
    private readonly Func<DateTime> _clock;
    private readonly Action<Order> _openOrder;
    private readonly Func<IReadOnlyList<AiEndpoint>, IAiChat>? _chatFactory;
    private readonly Action? _openSettings;
    private readonly Func<int, string, string, IReadOnlyList<AuditEntry>>? _activityLoader;
    private readonly Func<IReadOnlyDictionary<string, object?>>? _statusProvider;
    private readonly Func<IEnumerable<StoredOrder>, IReadOnlyDictionary<string, (long Since, int Stage)>>? _returnWatchProvider;
    private readonly Func<IReadOnlyList<AiOrderTools.AiNotice>>? _notificationsProvider;
    private IAiChat? _chat;
    private CancellationTokenSource? _cts;

    public AiChatViewModel(
        AiSettingsStore? store,
        Func<IReadOnlyList<StoredOrder>>? loader,
        Func<DateTime> clock,
        Action<Order> openOrder,
        Func<IReadOnlyList<AiEndpoint>, IAiChat>? chatFactory = null,
        Action? openSettings = null,
        Func<int, string, string, IReadOnlyList<AuditEntry>>? activityLoader = null,
        Func<IReadOnlyDictionary<string, object?>>? statusProvider = null,
        Func<IEnumerable<StoredOrder>, IReadOnlyDictionary<string, (long Since, int Stage)>>? returnWatchProvider = null,
        Func<IReadOnlyList<AiOrderTools.AiNotice>>? notificationsProvider = null)
    {
        _store = store;
        _loader = loader;
        _clock = clock;
        _openOrder = openOrder;
        _chatFactory = chatFactory;
        _openSettings = openSettings;
        _activityLoader = activityLoader;
        _statusProvider = statusProvider;
        _returnWatchProvider = returnWatchProvider;
        _notificationsProvider = notificationsProvider;
        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        GroqModels = store?.LoadModel(AiSettingsStore.Groq) ?? AiChatService.DefaultGroqModels;
        GeminiModels = store?.LoadModel(AiSettingsStore.Gemini) ?? AiChatService.DefaultGeminiModels;
        HasKey = store?.HasKey ?? false;
        Available = store is not null && (loader is not null || chatFactory is not null);
        StatusText = Available
            ? (HasKey ? "Sẵn sàng. Hãy hỏi về đơn hàng của bạn." : "Chưa có khóa API. Bấm ⚙ Khóa API để nhập khóa Groq và/hoặc Google.")
            : "Chế độ demo: trợ lý AI chỉ hoạt động với dữ liệu thật.";
    }

    public ObservableCollection<AiChatMessage> Messages { get; } = new();

    public bool Available { get; }

    public bool IsEmpty => Messages.Count == 0;

    public IReadOnlyList<string> Suggestions { get; } =
    [
        "Hôm nay có bao nhiêu đơn về?",
        "Liệt kê đơn chưa về của Shopee",
        "Sản phẩm nào hoàn nhiều nhất tuần này?",
        "Tìm đơn có mã vận đơn chứa 5678",
    ];

    [ObservableProperty] private string _input = string.Empty;
    [ObservableProperty] private string _groqModels = AiChatService.DefaultGroqModels;
    [ObservableProperty] private string _geminiModels = AiChatService.DefaultGeminiModels;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasKey;
    [ObservableProperty] private bool _isPanelOpen;

    partial void OnIsBusyChanged(bool value) { SendCommand.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(IsNotBusy)); }

    public bool IsNotBusy => !IsBusy;
    partial void OnInputChanged(string value) => SendCommand.NotifyCanExecuteChanged();
    partial void OnHasKeyChanged(bool value) { SendCommand.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(NeedsKey)); }

    /// <summary>Có thể dùng AI nhưng chưa nhập khóa: hiện lời nhắc kèm nút mở Cài đặt.</summary>
    public bool NeedsKey => Available && !HasKey;

    public bool CanSend => Available && HasKey && !IsBusy && !string.IsNullOrWhiteSpace(Input);

    private IAiChat? EnsureChat()
    {
        if (_chat is not null) return _chat;
        var endpoints = _store?.BuildEndpoints();
        if (endpoints is null || endpoints.Count == 0) return null;
        _chat = _chatFactory is not null
            ? _chatFactory(endpoints)
            : new AiChatService(SharedHttp, endpoints, new AiOrderTools(_loader!, _clock, _activityLoader, _statusProvider, _returnWatchProvider, _notificationsProvider), _clock);
        return _chat;
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var question = Input.Trim();
        var chat = EnsureChat();
        if (chat is null || question.Length == 0) return;
        Input = string.Empty;
        var started = _clock();
        Messages.Add(new AiChatMessage(true, question, Array.Empty<AiOrderRef>(), "") { Time = started });
        IsBusy = true;
        StatusText = "Đang hỏi AI...";
        _cts = new CancellationTokenSource();
        try
        {
            var answer = await Task.Run(() => chat.AskAsync(question, _cts.Token));
            var done = _clock();
            Messages.Add(new AiChatMessage(false, answer.Text, answer.Refs, "") { Time = done, Elapsed = done - started });
            StatusText = "Sẵn sàng.";
        }
        catch (OperationCanceledException) { StatusText = "Đã hủy."; }
        catch (AiException ex)
        {
            Messages.Add(new AiChatMessage(false, "⚠ " + ex.Message, Array.Empty<AiOrderRef>(), "") { Time = _clock() });
            StatusText = "Lỗi.";
        }
        catch (Exception ex)
        {
            Messages.Add(new AiChatMessage(false, "⚠ Lỗi không mong muốn: " + ex.Message, Array.Empty<AiOrderRef>(), "") { Time = _clock() });
            StatusText = "Lỗi.";
        }
        finally { IsBusy = false; _cts?.Dispose(); _cts = null; }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>Sao chép nội dung một tin (chữ thuần, bỏ ký hiệu markdown).</summary>
    [RelayCommand]
    private void CopyMessage(AiChatMessage? m)
    {
        if (m is null) return;
        try { System.Windows.Clipboard.SetText(ChatMarkdown.ToPlainText(m.Text)); StatusText = "Đã sao chép."; }
        catch { StatusText = "Không sao chép được (clipboard đang bận)."; }
    }

    [RelayCommand]
    private void Ask(string? suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion)) return;
        Input = suggestion;
        if (SendCommand.CanExecute(null)) SendCommand.Execute(null);
    }

    [RelayCommand]
    private void NewChat()
    {
        Messages.Clear();
        _chat?.Reset();
        StatusText = HasKey ? "Đã bắt đầu cuộc trò chuyện mới." : StatusText;
    }

    [RelayCommand]
    private void TogglePanel() => IsPanelOpen = !IsPanelOpen;

    [RelayCommand]
    private void ClosePanel() => IsPanelOpen = false;

    /// <summary>Đóng cửa sổ chat và mở Cài đặt (nơi nhập khóa API).</summary>
    [RelayCommand]
    private void OpenSettings()
    {
        IsPanelOpen = false;
        _openSettings?.Invoke();
    }

    [RelayCommand]
    private void OpenRef(AiOrderRef? r)
    {
        if (r is null) return;
        var order = _chat?.TryGetOrder(r.Id);
        if (order is null) { StatusText = "Không mở được đơn này (phiên đã đổi). Hãy hỏi lại."; return; }
        _openOrder(order);
    }

    /// <summary>Lưu khóa (nếu nhập) và danh sách mô hình, rồi thử kết nối thật từng mô hình. Khóa không bao giờ hiện lại trên màn hình.</summary>
    public async Task SaveSettingsAsync(string? groqKey, string? geminiKey)
    {
        if (_store is null) return;
        if (!string.IsNullOrWhiteSpace(groqKey)) _store.SaveKey(AiSettingsStore.Groq, groqKey);
        if (!string.IsNullOrWhiteSpace(geminiKey)) _store.SaveKey(AiSettingsStore.Gemini, geminiKey);
        _store.SaveModel(AiSettingsStore.Groq, GroqModels);
        _store.SaveModel(AiSettingsStore.Gemini, GeminiModels);
        GroqModels = _store.LoadModel(AiSettingsStore.Groq);
        GeminiModels = _store.LoadModel(AiSettingsStore.Gemini);
        _chat = null;
        HasKey = _store.HasKey;
        if (!HasKey) { StatusText = "Chưa có khóa API."; return; }
        IsBusy = true;
        var usedSaved = string.IsNullOrWhiteSpace(groqKey) && string.IsNullOrWhiteSpace(geminiKey) ? "Dùng khóa đã lưu. " : "";
        var endpoints = _store.BuildEndpoints();
        StatusText = $"{usedSaved}Đang thử {endpoints.Count} mô hình cùng lúc (mỗi mô hình tối đa 20 giây)...";
        try
        {
            var svc = new AiChatService(SharedHttp, endpoints, new AiOrderTools(() => Array.Empty<StoredOrder>(), _clock), _clock) { RequestTimeout = TimeSpan.FromSeconds(20) };
            var done = new List<string>();
            // Progress tạo trên luồng giao diện nên báo từng mô hình ngay khi có kết quả.
            var progress = new Progress<(string Label, string? Error)>(r =>
            {
                done.Add((r.Error is null ? "✓ " : "✗ ") + r.Label + (r.Error is null ? "" : ": " + r.Error));
                StatusText = $"{usedSaved}Đã thử {done.Count}/{endpoints.Count} mô hình...\n" + string.Join("\n", done);
            });
            var results = await Task.Run(() => svc.TestAllAsync(default, progress));
            var ok = results.Count(r => r.Error is null);
            var lines = results.Select(r => (r.Error is null ? "✓ " : "✗ ") + r.Label + (r.Error is null ? "" : ": " + r.Error));
            StatusText = usedSaved + (ok > 0 ? $"Dùng được {ok}/{results.Count} mô hình." : "Không mô hình nào dùng được.") + "\n" + string.Join("\n", lines);
        }
        catch (Exception ex) { StatusText = "Không dùng được: " + ex.Message; }
        finally { IsBusy = false; }
    }

    public void ForgetKeys()
    {
        _store?.ClearAllKeys();
        _chat = null;
        HasKey = false;
        StatusText = "Đã xóa các khóa API khỏi máy này.";
    }
}

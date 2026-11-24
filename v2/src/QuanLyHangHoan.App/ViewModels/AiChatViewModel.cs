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
    public bool HasRefs => Refs.Count > 0;
    public string RefsHeader => $"Đơn liên quan ({Refs.Count}) — bấm để xem chi tiết";
    public bool HasTrace => !string.IsNullOrEmpty(Trace);
}

/// <summary>
/// Màn "Hỏi AI": hỏi đáp về dữ liệu đơn hàng bằng Groq. AI chỉ gọi công cụ CHỈ ĐỌC (số liệu do mã tính), dữ liệu gửi đi đã ẩn tên/SĐT/địa chỉ người nhận.
/// Khóa API mã hóa DPAPI. Đơn trong câu trả lời bấm được để mở cửa sổ chi tiết đơn.
/// </summary>
public sealed partial class AiChatViewModel : ObservableObject
{
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(90) };

    private readonly AiSettingsStore? _store;
    private readonly Func<IReadOnlyList<StoredOrder>>? _loader;
    private readonly Func<DateTime> _clock;
    private readonly Action<Order> _openOrder;
    private readonly Func<string, string, IAiChat>? _chatFactory;
    private IAiChat? _chat;
    private CancellationTokenSource? _cts;

    public AiChatViewModel(
        AiSettingsStore? store,
        Func<IReadOnlyList<StoredOrder>>? loader,
        Func<DateTime> clock,
        Action<Order> openOrder,
        Func<string, string, IAiChat>? chatFactory = null)
    {
        _store = store;
        _loader = loader;
        _clock = clock;
        _openOrder = openOrder;
        _chatFactory = chatFactory;
        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        Model = store?.LoadModel() ?? AiChatService.DefaultModel;
        HasKey = store?.HasKey ?? false;
        Available = store is not null && (loader is not null || chatFactory is not null);
        StatusText = Available
            ? (HasKey ? "Sẵn sàng. Hãy hỏi về đơn hàng của bạn." : "Chưa có khóa API Groq. Nhập khóa ở khung bên dưới để bắt đầu.")
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
    [ObservableProperty] private string _model = AiChatService.DefaultModel;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasKey;
    [ObservableProperty] private bool _showSettings;

    partial void OnIsBusyChanged(bool value) => SendCommand.NotifyCanExecuteChanged();
    partial void OnInputChanged(string value) => SendCommand.NotifyCanExecuteChanged();
    partial void OnHasKeyChanged(bool value) => SendCommand.NotifyCanExecuteChanged();

    public bool CanSend => Available && HasKey && !IsBusy && !string.IsNullOrWhiteSpace(Input);

    private IAiChat? EnsureChat()
    {
        if (_chat is not null) return _chat;
        var key = _store?.LoadKey();
        if (string.IsNullOrEmpty(key)) return null;
        _chat = _chatFactory is not null
            ? _chatFactory(key, Model)
            : new AiChatService(SharedHttp, key, Model, new AiOrderTools(_loader!, _clock), _clock);
        return _chat;
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var question = Input.Trim();
        var chat = EnsureChat();
        if (chat is null || question.Length == 0) return;
        Input = string.Empty;
        Messages.Add(new AiChatMessage(true, question, Array.Empty<AiOrderRef>(), ""));
        IsBusy = true;
        StatusText = "Đang hỏi AI...";
        _cts = new CancellationTokenSource();
        try
        {
            var answer = await Task.Run(() => chat.AskAsync(question, _cts.Token));
            Messages.Add(new AiChatMessage(false, answer.Text, answer.Refs, string.Join("\n", answer.Trace.Select(t => "• " + t))));
            StatusText = "Sẵn sàng.";
        }
        catch (OperationCanceledException) { StatusText = "Đã hủy."; }
        catch (AiException ex)
        {
            Messages.Add(new AiChatMessage(false, "⚠ " + ex.Message, Array.Empty<AiOrderRef>(), ""));
            StatusText = "Lỗi.";
        }
        catch (Exception ex)
        {
            Messages.Add(new AiChatMessage(false, "⚠ Lỗi không mong muốn: " + ex.Message, Array.Empty<AiOrderRef>(), ""));
            StatusText = "Lỗi.";
        }
        finally { IsBusy = false; _cts?.Dispose(); _cts = null; }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

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
    private void ToggleSettings() => ShowSettings = !ShowSettings;

    [RelayCommand]
    private void OpenRef(AiOrderRef? r)
    {
        if (r is null) return;
        var order = _chat?.TryGetOrder(r.Id);
        if (order is null) { StatusText = "Không mở được đơn này (phiên đã đổi). Hãy hỏi lại."; return; }
        _openOrder(order);
    }

    /// <summary>Lưu khóa (nếu nhập) và tên mô hình, rồi thử kết nối thật. Khóa không bao giờ hiện lại trên màn hình.</summary>
    public async Task SaveSettingsAsync(string? newKey)
    {
        if (_store is null) return;
        if (!string.IsNullOrWhiteSpace(newKey)) _store.SaveKey(newKey);
        _store.SaveModel(Model);
        _chat = null;
        HasKey = _store.HasKey;
        if (!HasKey) { StatusText = "Chưa có khóa API."; return; }
        IsBusy = true;
        StatusText = "Đang thử kết nối Groq...";
        try
        {
            var key = _store.LoadKey()!;
            var svc = new AiChatService(SharedHttp, key, Model, new AiOrderTools(() => Array.Empty<StoredOrder>(), _clock), _clock);
            await Task.Run(() => svc.TestConnectionAsync());
            StatusText = "Kết nối thành công. Hãy hỏi về đơn hàng của bạn.";
            ShowSettings = false;
        }
        catch (AiException ex) { StatusText = "Không dùng được: " + ex.Message; }
        catch (Exception ex) { StatusText = "Không dùng được: " + ex.Message; }
        finally { IsBusy = false; }
    }

    public void ForgetKey()
    {
        _store?.ClearKey();
        _chat = null;
        HasKey = false;
        StatusText = "Đã xóa khóa API khỏi máy này.";
    }
}

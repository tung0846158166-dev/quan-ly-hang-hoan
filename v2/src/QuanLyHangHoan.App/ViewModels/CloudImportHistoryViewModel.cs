using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuanLyHangHoan.Data.Cloud;

namespace QuanLyHangHoan.App.ViewModels;

/// <summary>Một dòng lịch sử đồng bộ (đã định dạng sẵn để hiển thị).</summary>
public sealed record CloudImportRow(string When, string Source, string Summary, bool HasAmbiguous);

/// <summary>
/// Chế độ cloud: thẻ Google Sheet chỉ còn LỊCH SỬ ĐỒNG BỘ (chủ dự án 26/11/2026). Sheet do Apps Script gửi thẳng lên cloud,
/// máy tính không nạp Sheet nữa. Mỗi dòng là một lượt (các lô gửi liền nhau đã được cloud gộp lại).
/// </summary>
public sealed partial class CloudImportHistoryViewModel : ObservableObject
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<CloudImportRun>>> _load;
    private readonly TimeZoneInfo _zone;

    public CloudImportHistoryViewModel(Func<CancellationToken, Task<IReadOnlyList<CloudImportRun>>> load, TimeZoneInfo? zone = null)
    {
        _load = load;
        _zone = zone ?? TimeZoneInfo.Local;
    }

    public ObservableCollection<CloudImportRow> Rows { get; } = new();

    [ObservableProperty] private string _statusText = "Chưa tải lịch sử.";
    [ObservableProperty] private bool _isBusy;

    public string Intro => "Google Sheet do cloud tự nạp (Apps Script gửi lên mỗi 5 phút, gửi lại toàn bộ lúc 2 giờ sáng). "
        + "Máy tính không nạp Sheet nữa. Dưới đây là các lượt đồng bộ cloud đã nhận, mới nhất trước. Lượt không có đơn đổi thì không ghi.";

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var runs = await _load(default);
            Rows.Clear();
            foreach (var r in runs) Rows.Add(ToRow(r));
            StatusText = Rows.Count == 0 ? "Chưa có lượt đồng bộ nào có đơn đổi." : $"{Rows.Count} lượt gần nhất · cập nhật lúc {DateTime.Now:HH:mm}";
        }
        catch (Exception ex) when (ex is CloudApiException or System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            StatusText = "Không tải được lịch sử: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    private CloudImportRow ToRow(CloudImportRun r)
    {
        var t = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(r.Start), _zone);
        var parts = new List<string> { $"Gửi {r.Sent:N0}" };
        if (r.Added > 0) parts.Add($"thêm {r.Added:N0}");
        if (r.Updated > 0) parts.Add($"cập nhật {r.Updated:N0}");
        if (r.Skipped > 0) parts.Add($"bỏ qua {r.Skipped:N0}");
        if (r.Ambiguous > 0) parts.Add($"nhập nhằng {r.Ambiguous:N0}");
        return new CloudImportRow(t.ToString("dd/MM HH:mm"), SourceName(r.Source), string.Join(" · ", parts), r.Ambiguous > 0);
    }

    public static string SourceName(string source) => source switch
    {
        "sheet" => "Google Sheet",
        "file" => "Nạp file (máy tính)",
        "folder" => "Thư mục (máy tính)",
        "salework" => "Salework (máy tính)",
        _ => source,
    };
}

using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Data.Cloud;

namespace QuanLyHangHoan.Tests;

/// <summary>Chế độ cloud: thẻ Google Sheet chỉ còn LỊCH SỬ ĐỒNG BỘ lấy từ cloud (chủ dự án 26/11/2026). Dữ liệu GIẢ.</summary>
public sealed class CloudImportHistoryTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 26, 9, 5, 0, TimeSpan.FromHours(7));

    [Fact]
    public void Parses_runs_from_the_cloud_json()
    {
        var runs = CloudApi.ParseImportLog(System.Text.Json.Nodes.JsonNode.Parse(
            """[{"source":"sheet","start":1795662300000,"end":1795662360000,"batches":2,"sent":60,"added":3,"updated":10,"skipped":1,"ambiguous":2},{"source":"x"}]""")!);
        Assert.Equal(2, runs.Count);
        Assert.Equal(new CloudImportRun("sheet", 1795662300000, 1795662360000, 2, 60, 3, 10, 1, 2), runs[0]);
    }

    [Fact]
    public async Task Rows_show_time_source_and_counts_in_vietnamese()
    {
        var start = T.ToUnixTimeMilliseconds();
        var vm = new CloudImportHistoryViewModel(_ => Task.FromResult<IReadOnlyList<CloudImportRun>>(new[]
        {
            new CloudImportRun("sheet", start, start + 60_000, 2, 60, 3, 10, 1, 2),
            new CloudImportRun("salework", start - 3_600_000, start - 3_600_000, 1, 5, 0, 5, 0, 0),
        }), TimeZoneInfo.CreateCustomTimeZone("VN", TimeSpan.FromHours(7), "VN", "VN"));
        await vm.RefreshAsync();
        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal("26/11 09:05", vm.Rows[0].When);
        Assert.Equal("Google Sheet", vm.Rows[0].Source);
        Assert.Equal("Gửi 60 · thêm 3 · cập nhật 10 · bỏ qua 1 · nhập nhằng 2", vm.Rows[0].Summary);
        Assert.True(vm.Rows[0].HasAmbiguous);
        Assert.Equal("Salework (máy tính)", vm.Rows[1].Source);
        Assert.Equal("Gửi 5 · cập nhật 5", vm.Rows[1].Summary);
        Assert.Contains("2 lượt", vm.StatusText);
    }

    [Fact]
    public async Task Errors_are_shown_without_crashing_and_keep_old_rows()
    {
        var fail = false;
        var vm = new CloudImportHistoryViewModel(_ => fail ? throw new CloudApiException("unavailable", "Mất kết nối", 503)
            : Task.FromResult<IReadOnlyList<CloudImportRun>>(new[] { new CloudImportRun("sheet", 1, 1, 1, 1, 1, 0, 0, 0) }), TimeZoneInfo.Utc);
        await vm.RefreshAsync();
        fail = true;
        await vm.RefreshAsync();
        Assert.Single(vm.Rows);
        Assert.Contains("Mất kết nối", vm.StatusText);
    }
}

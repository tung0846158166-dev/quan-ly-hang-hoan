using System.IO;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Sheet;

namespace QuanLyHangHoan.Tests;

/// <summary>Đồng bộ Google Sheet với bộ tải GIẢ (không mạng), DB tạm, dữ liệu GIẢ.</summary>
public sealed class SheetSyncServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private const string Url = "https://script.google.com/macros/s/FAKE_ID_FOR_TESTS/exec";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-sh-" + Guid.NewGuid().ToString("N"));
    public SheetSyncServiceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
    private string Db => Path.Combine(_dir, "t.db");
    private string State => Path.Combine(_dir, "sheet.json");

    private sealed class FakeProtector : ISecretProtector
    {
        public string Protect(string plain) => "P:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plain));
        public string? Unprotect(string t) => t.StartsWith("P:") ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(t[2..])) : null;
    }

    private const string Header = "Mã,Mã đơn,Loại,Mã vận đơn,Trạng thái về,Về shop lúc,Hạn khiếu nại,Hàng,SL,Tiền,Lý do,Ngày,Cập nhật\n";
    private static string Csv(string status = "Đang về") =>
        Header + $"RR1,SP1,Trả hàng + hoàn tiền,SPXVN0000000001,{status},21/11/2026 10:00,24/11/2026 10:00,\"Áo · Đen, M × 2\",2,250000,Sai mô tả,20/11/2026,22/11/2026\n";

    private SheetSyncService Make(Func<string, CancellationToken, Task<string>> fetch, List<string>? urls = null)
    {
        AppDatabase.Initialize(Db);
        var s = new SheetSyncService(Db, State, new FakeProtector(), () => Now, (u, ct) => { urls?.Add(u); return fetch(u, ct); });
        Assert.True(s.SetUrl(Url, out _));
        return s;
    }

    [Fact]
    public void Imports_then_skips_unchanged_sheet_and_only_reloads_changed_orders()
    {
        var csv = Csv();
        using var s = Make((_, _) => Task.FromResult(csv));
        var r1 = s.RunOnce();
        Assert.True(r1.Ok, r1.Note);
        Assert.Equal(1, r1.Import!.Added);
        Assert.Equal("Shopee", OrderRepository.Load(Db).Single().Order.Platform);

        var r2 = s.RunOnce();
        Assert.True(r2.Ok); Assert.True(r2.Unchanged);
        Assert.Equal("Sheet không thay đổi", r2.Note);

        csv = Csv("Đã về shop");
        var r3 = s.RunOnce();
        Assert.True(r3.Ok, r3.Note);
        Assert.Equal(0, r3.Import!.Added);
        Assert.Equal(1, r3.Import.Updated);
        Assert.Single(OrderRepository.Load(Db));
        Assert.Equal("Đã về shop", OrderRepository.Load(Db).Single().Order.PlatformStatus);
    }

    [Fact]
    public void Url_is_validated_stored_encrypted_and_never_leaks_into_notes_or_state_file()
    {
        AppDatabase.Initialize(Db);
        using var s = new SheetSyncService(Db, State, new FakeProtector(), () => Now, (u, _) => throw new InvalidOperationException("lỗi tại " + u));
        Assert.False(s.SetUrl("http://script.google.com/x", out _));
        Assert.False(s.SetUrl("https://evil.example.com/x", out _));
        Assert.False(s.SetUrl("https://script.google.com.evil.example/x", out _));
        Assert.True(s.SetUrl(Url, out _));
        var r = s.RunOnce(manual: true);
        Assert.False(r.Ok);
        Assert.DoesNotContain("FAKE_ID_FOR_TESTS", r.Note);
        Assert.DoesNotContain(s.Token!, r.Note);
        var saved = File.ReadAllText(State);
        Assert.DoesNotContain("FAKE_ID_FOR_TESTS", saved);
        Assert.DoesNotContain(s.Token!, saved);
        Assert.Empty(OrderRepository.Load(Db));
    }

    [Fact]
    public void Google_error_and_html_pages_are_reported_and_change_nothing()
    {
        var body = "{\"error\":\"Sai token\"}";
        using var s = Make((_, _) => Task.FromResult(body));
        var r = s.RunOnce(true);
        Assert.False(r.Ok); Assert.Contains("Sai token", r.Note);
        body = "<!DOCTYPE html><html>login</html>";
        r = s.RunOnce(true);
        Assert.False(r.Ok); Assert.Contains("Triển khai", r.Note);
        Assert.Empty(OrderRepository.Load(Db));
    }

    [Fact]
    public void Request_carries_token_and_tab_and_no_personal_columns_are_stored()
    {
        var urls = new List<string>();
        using var s = Make((_, _) => Task.FromResult(Csv()), urls);
        s.Config.Tabs = "Hàng hoàn";
        s.RunOnce(true);
        // 1 yêu cầu cho tab đã cấu hình + 1 yêu cầu cho tab ẩn "Vận đơn huỷ" (tùy chọn)
        Assert.Equal(2, urls.Count);
        Assert.Contains("tab=" + Uri.EscapeDataString("Vận đơn huỷ"), urls[1]);
        Assert.Contains("token=" + s.Token, urls[0]);
        Assert.Contains("&tab=H", urls[0]);
        Assert.StartsWith(Url + "?token=", urls[0]);
    }

    [Theory]
    [InlineData(401, "Bất kỳ ai")]
    [InlineData(403, "Bất kỳ ai")]
    [InlineData(404, "/exec")]
    [InlineData(503, "503")]
    public void Http_errors_explain_how_to_fix(int code, string hint) =>
        Assert.Contains(hint, SheetSyncService.DescribeHttpError(code));

    [Fact]
    public void Set_token_reuses_old_app_token_in_the_request_and_rejects_bad_values()
    {
        var urls = new List<string>();
        using var s = Make((_, _) => Task.FromResult(Csv()), urls);
        Assert.False(s.SetToken("ngan", out var e1)); Assert.NotEmpty(e1);
        Assert.False(s.SetToken("co dau cach 12345", out _));
        Assert.True(s.SetToken("  abc123DEF456  ", out _));
        Assert.Equal("abc123DEF456", s.Token);
        s.RunOnce(manual: true);
        Assert.Contains(urls, u => u.Contains("token=abc123DEF456"));
    }

    [Fact]
    public void Set_token_needs_url_first()
    {
        AppDatabase.Initialize(Db);
        using var s = new SheetSyncService(Db, State, new FakeProtector(), () => Now, (_, _) => Task.FromResult(""));
        Assert.False(s.SetToken("abc123DEF456", out var err));
        Assert.Contains("địa chỉ", err);
    }
}

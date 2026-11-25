using System.IO;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Sheet;

namespace QuanLyHangHoan.Tests;

/// <summary>Tab ẩn "Vận đơn huỷ" (Mã đơn -> Mã vận đơn) dùng để tra mã cho đơn huỷ/bom. Dữ liệu GIẢ.</summary>
public sealed class SheetTrackTabTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 25, 12, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-track-" + Guid.NewGuid().ToString("N"));

    public SheetTrackTabTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static Dictionary<string, string> R(params (string K, string V)[] kv) => kv.ToDictionary(x => x.K, x => x.V);

    private static List<Dictionary<string, string>> TrackTab(params (string Order, string Track)[] rows) =>
        rows.Select(r => R(("Mã đơn", r.Order), ("Mã vận đơn", r.Track))).ToList();

    private static List<Dictionary<string, string>> CancelTab(params string[] orderIds) =>
        orderIds.Select(o => R(("Mã đơn", o), ("Loại", "Hủy đơn"), ("Ai hủy", "Người mua"), ("Tên sản phẩm", "Áo thun"), ("SL", "1"))).ToList();

    // ------------------------------------------------------------ nhận ra tab
    [Fact]
    public void Tab_with_only_order_and_track_columns_is_a_lookup_tab()
    {
        Assert.Equal("vandon", SheetPipeline.TabKind(TrackTab(("A1", "T1"))));
        // tên cột khác dấu/hoa thường vẫn nhận ra
        Assert.Equal("vandon", SheetPipeline.TabKind(new() { R(("MÃ ĐƠN", "A1"), ("ma van don", "T1")) }));
    }

    [Fact]
    public void Real_tabs_are_not_mistaken_for_the_lookup_tab()
    {
        Assert.Equal("huy", SheetPipeline.TabKind(CancelTab("A1")));
        var hanghoan = new List<Dictionary<string, string>> { R(("Mã đơn", "A1"), ("Mã vận đơn", "T1"), ("Hàng", "Áo"), ("Trạng thái về", "Đang về")) };
        Assert.Equal("hanghoan", SheetPipeline.TabKind(hanghoan));
        var trahang = new List<Dictionary<string, string>> { R(("Mã đơn", "A1"), ("Mã vận đơn", "T1"), ("Mã trả hàng", "RR1")) };
        Assert.Equal("trahang", SheetPipeline.TabKind(trahang));
        Assert.Equal("", SheetPipeline.TabKind(new() { R(("Mã đơn", "A1")) }));
    }

    // ------------------------------------------------------------ bảng tra
    [Fact]
    public void Track_map_uppercases_trims_and_skips_blank_tracks()
    {
        var map = SheetPipeline.TrackMap(TrackTab((" ab12 ", " SPX001 "), ("CD34", ""), ("", "SPX003"), ("EF56", "SPX005")));
        Assert.Equal(2, map.Count);
        Assert.Equal("SPX001", map["AB12"]);
        Assert.Equal("SPX005", map["EF56"]);
    }

    // ------------------------------------------------------------ điền mã
    [Fact]
    public void Build_fills_missing_track_from_lookup_tab_and_does_not_load_it_as_orders()
    {
        var res = SheetPipeline.Build(new[] { CancelTab("A1", "B2"), TrackTab(("a1", "SPX111")) }, null, null, Now);

        Assert.Equal(2, res.Parcels.Count);
        Assert.Equal("SPX111", res.Parcels.Single(p => p.OrderId == "A1").Track);
        Assert.Equal("", res.Parcels.Single(p => p.OrderId == "B2").Track);   // đơn không có trong bảng tra: để trống
        Assert.Equal(1, res.TracksFilled);
        Assert.Equal(1, res.TrackLookupTabs);
        Assert.Equal(2, res.Rows.Count);   // tab tra mã không thành đơn hàng
        Assert.Empty(res.OtherRows);
    }

    [Fact]
    public void Build_never_overwrites_an_existing_track()
    {
        var cancel = new List<Dictionary<string, string>> { R(("Mã đơn", "A1"), ("Loại", "Hủy đơn"), ("Ai hủy", "Người mua"), ("Mã vận đơn", "SPX-CO-SAN"), ("SL", "1")) };
        var res = SheetPipeline.Build(new[] { cancel, TrackTab(("A1", "SPX-KHAC")) }, null, null, Now);

        Assert.Equal("SPX-CO-SAN", res.Parcels.Single().Track);
        Assert.Equal(0, res.TracksFilled);
    }

    [Fact]
    public void Cancel_tab_with_its_own_track_column_keeps_the_track()
    {
        var cancel = new List<Dictionary<string, string>> { R(("Mã đơn", "A1"), ("Loại", "Hủy đơn"), ("Ai hủy", "Người mua"), ("Mã vận đơn", "SPX777")) };
        Assert.Equal("SPX777", SheetPipeline.Build(new[] { cancel }, null, null, Now).Parcels.Single().Track);
    }

    [Fact]
    public void Lookup_tab_alone_or_empty_does_not_break_the_build()
    {
        var res = SheetPipeline.Build(new[] { TrackTab(("A1", "T1")), new List<Dictionary<string, string>>() }, null, null, Now);
        Assert.Empty(res.Parcels);
        Assert.Empty(res.Rows);
    }

    // ------------------------------------------------------------ đồng bộ (mạng giả)
    private sealed class FakeProtector : ISecretProtector
    {
        public string Protect(string plain) => "P:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plain));
        public string? Unprotect(string t) => t.StartsWith("P:") ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(t[2..])) : null;
    }

    private const string Header = "Mã,Mã đơn,Loại,Mã vận đơn,Trạng thái về,Về shop lúc,Hạn khiếu nại,Hàng,SL,Tiền,Lý do,Ngày,Cập nhật\n";
    private const string Row = "RR1,SP1,Trả hàng + hoàn tiền,SPXVN0000000001,Đang về,21/11/2026 10:00,24/11/2026 10:00,\"Áo · Đen, M × 2\",2,250000,Sai mô tả,20/11/2026,20/11/2026 09:00\n";

    private SheetSyncService Make(Func<string, CancellationToken, Task<string>> fetch)
    {
        var db = Path.Combine(_dir, "data.db");
        AppDatabase.Initialize(db);
        var s = new SheetSyncService(db, Path.Combine(_dir, "sheet.json"), new FakeProtector(), () => Now, fetch);
        Assert.True(s.SetUrl("https://script.google.com/macros/s/AKfycbFAKE/exec", out _));
        return s;
    }

    private static bool IsTrackTab(string url) => url.Contains("tab=" + Uri.EscapeDataString(SheetPipeline.TrackTabName));

    [Theory]
    [InlineData("error")]
    [InlineData("html")]
    [InlineData("throw")]
    public void Missing_or_forbidden_lookup_tab_is_ignored_silently(string mode)
    {
        var urls = new List<string>();
        using var s = Make((u, _) =>
        {
            urls.Add(u);
            if (!IsTrackTab(u)) return Task.FromResult(Header + Row);
            return mode switch
            {
                "error" => Task.FromResult("{\"error\":\"Không có tab\"}"),
                "html" => Task.FromResult("<!DOCTYPE html><html><body>Đăng nhập</body></html>"),
                _ => throw new InvalidOperationException("Google trả mã 404"),
            };
        });

        var r = s.RunOnce(manual: true);

        Assert.True(r.Ok, r.Note);
        Assert.Contains(urls, IsTrackTab);   // đã thử đọc tab ẩn
        Assert.DoesNotContain("Vận đơn huỷ", r.Note);
    }

    [Fact]
    public void Lookup_tab_is_requested_with_the_secret_and_sync_still_succeeds()
    {
        var urls = new List<string>();
        using var s = Make((u, _) =>
        {
            urls.Add(u);
            return Task.FromResult(IsTrackTab(u) ? "Mã đơn,Mã vận đơn\nXX9,SPX999\n" : Header + Row);
        });

        var r = s.RunOnce(manual: true);

        Assert.True(r.Ok, r.Note);
        Assert.Contains(urls, u => IsTrackTab(u) && u.Contains("token="));
        Assert.DoesNotContain("SPX999", r.Note);   // ghi chú không lộ dữ liệu tra
    }
}

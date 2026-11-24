using System.Text.Json.Nodes;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Ai;

namespace QuanLyHangHoan.Tests;

/// <summary>Công cụ AI mở rộng: tổng tiền, gom nhóm, cảnh báo vàng/đỏ, chi tiết đơn, nhật ký. Dữ liệu GIẢ.</summary>
public sealed class AiToolsExtendedTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 14, 0, 0);

    private static StoredOrder S(Order o, bool archived = false) => new(o, archived, "", false, "", "");

    private static Order O(string id, string track, string status, decimal price, string platform = "Shopee", string carrier = "SPX Express",
        string arrived = "", string recv = "", string product = "Áo thun", int qty = 1, bool watch = false, bool partial = false, string type = "Trả hàng/Hoàn tiền") => new()
    {
        Id = id, OrderId = "O" + id, Track = track, Status = status, Price = price, Platform = platform, Carrier = carrier, ReturnedAt = arrived, RecvDate = recv,
        OrderType = type, IsWatchlist = watch, IsPartialReturn = partial, ReturnDate = "20/11/2026 09:00:00", ShipDate = "18/11/2026 09:00:00",
        BuyerNote = "Khách 0912345678 địa chỉ 12 Nguyễn Trãi", Items = new() { new OrderItem { Product = product, Qty = qty, ReturnedQty = qty } },
    };

    private static readonly StoredOrder[] Data =
    {
        // đang chờ: đỏ (28h), vàng (14h), an toàn (4h), chưa có giờ về; TikTok "Chưa về"
        S(O("1", "VN001", "Chưa trả", 100_000, arrived: "24/11/2026 10:00:00", product: "Áo thun", qty: 2)),
        S(O("2", "VN002", "Chưa trả", 250_000, arrived: "25/11/2026 00:00:00", product: "Quần jean", watch: true)),
        S(O("3", "VN003", "Chưa trả", 40_000, arrived: "25/11/2026 10:00:00", product: "Áo thun", carrier: "GHN")),
        S(O("4", "VN004", "Chưa về", 400_000, platform: "TikTok Shop", carrier: "J&T", product: "Váy", partial: true)),
        // đã nhận
        S(O("5", "VN005", "Hàng đã trả", 70_000, recv: "25/11/2026 09:00:00", arrived: "24/11/2026 20:00:00")),
        S(O("6", "VN006", "Hàng đã trả", 30_000, recv: "24/11/2026 15:00:00", product: "Quần jean", carrier: "GHN")),
        S(O("7", "VN007", "Bị mất", 90_000)),
        S(O("8", "VN008", "Chưa trả", 999_000), archived: true),
    };

    private static AiOrderTools Tools(Func<int, string, string, IReadOnlyList<AuditEntry>>? activity = null) =>
        new(() => Data, () => Now, activity);

    private static JsonNode J(AiToolResult r) => JsonNode.Parse(r.Json)!;

    [Fact]
    public void Waiting_means_both_not_returned_and_not_yet_arrived_and_total_value_is_summed()
    {
        var j = J(Tools().Execute("count_orders", """{"status":"chưa về"}"""));
        Assert.Equal(4, j["total"]!.GetValue<int>());                 // 3 "Chưa trả" + 1 "Chưa về" (lưu trữ không tính)
        Assert.Equal(790_000, j["totalValue"]!.GetValue<long>());     // 100k+250k+40k+400k
        Assert.Equal(5, j["totalQuantity"]!.GetValue<int>());         // 2+1+1+1
        Assert.Equal(4, J(Tools().Execute("count_orders", """{"status":"Đang chờ"}"""))["total"]!.GetValue<int>());
        Assert.Equal(2, J(Tools().Execute("count_orders", """{"status":"Đã nhận"}"""))["total"]!.GetValue<int>());
    }

    [Fact]
    public void Aggregate_groups_by_platform_carrier_and_sorts_by_value()
    {
        var j = J(Tools().Execute("aggregate", """{"status":"Đang chờ","group_by":"platform","metric":"value"}"""));
        var g = j["groups"]!.AsArray();
        Assert.Equal("TikTok Shop", g[0]!["group"]!.GetValue<string>());   // TikTok 400k > Shopee 390k
        Assert.Equal(790_000, j["totalValue"]!.GetValue<long>());

        var byCarrier = J(Tools().Execute("aggregate", """{"status":"Đang chờ","group_by":"carrier"}""")); // mặc định theo số kiện
        var first = byCarrier["groups"]![0]!;
        Assert.Equal("SPX Express", first["group"]!.GetValue<string>());
        Assert.Equal(2, first["count"]!.GetValue<int>());
    }

    [Fact]
    public void Aggregate_by_day_is_chronological_and_by_product_counts_quantity()
    {
        var days = J(Tools().Execute("aggregate", """{"status":"Đã nhận","group_by":"day","date_field":"recv"}"""))["groups"]!.AsArray();
        Assert.Equal(new[] { "2026-11-24", "2026-11-25" }, days.Select(x => x!["group"]!.GetValue<string>()).ToArray());

        var prod = J(Tools().Execute("aggregate", """{"status":"Đang chờ","group_by":"product","metric":"quantity"}"""))["groups"]!.AsArray();
        Assert.Equal("Áo thun", prod[0]!["group"]!.GetValue<string>());
        Assert.Equal(3, prod[0]!["quantity"]!.GetValue<int>());           // 2 + 1
        Assert.Equal(2, prod[0]!["count"]!.GetValue<int>());              // 2 đơn
    }

    [Fact]
    public void Alert_filters_use_12h_yellow_and_24h_red_since_the_goods_arrived()
    {
        var red = Tools().Execute("list_orders", """{"alert":"red"}""");
        Assert.Equal(new[] { "VN001" }, red.Refs.Select(r => r.Track).ToArray());
        Assert.Equal(28.0, J(red)["orders"]![0]!["hoursWaiting"]!.GetValue<double>());

        Assert.Equal(new[] { "VN002" }, Tools().Execute("list_orders", """{"alert":"yellow"}""").Refs.Select(r => r.Track).ToArray());
        Assert.Equal(2, Tools().Execute("list_orders", """{"alert":"warning"}""").Refs.Count);
        Assert.Equal(new[] { "VN003" }, Tools().Execute("list_orders", """{"alert":"safe"}""").Refs.Select(r => r.Track).ToArray());
    }

    [Fact]
    public void List_sorts_by_value_and_waiting_defaults_to_oldest_arrival_first_and_values_are_in_rows()
    {
        var byValue = Tools().Execute("list_orders", """{"status":"Đang chờ","sort_by":"value","descending":true,"limit":"2"}""");   // limit dạng chữ vẫn hiểu
        Assert.Equal(new[] { "VN004", "VN002" }, byValue.Refs.Select(r => r.Track).ToArray());
        Assert.Equal(400_000, J(byValue)["orders"]![0]!["value"]!.GetValue<long>());
        Assert.Equal(790_000, J(byValue)["totalValue"]!.GetValue<long>());

        var oldestFirst = Tools().Execute("list_orders", """{"status":"Đang chờ"}""");
        Assert.Equal("VN001", oldestFirst.Refs[0].Track);                  // về sớm nhất
    }

    [Fact]
    public void Filters_by_watching_partial_value_range_and_order_type()
    {
        Assert.Equal(new[] { "VN002" }, Tools().Execute("list_orders", """{"watching":true}""").Refs.Select(r => r.Track).ToArray());
        Assert.Equal(new[] { "VN004" }, Tools().Execute("list_orders", """{"partial_return":true}""").Refs.Select(r => r.Track).ToArray());
        Assert.Equal(2, Tools().Execute("list_orders", """{"status":"Đang chờ","min_value":100000,"max_value":300000}""").Refs.Count);
        Assert.Empty(Tools().Execute("list_orders", """{"order_type":"Đơn hủy"}""").Refs);
    }

    [Fact]
    public void Overview_summarises_waiting_alerts_and_today()
    {
        var j = J(Tools().Execute("overview", "{}"));
        Assert.Equal(4, j["waiting"]!["count"]!.GetValue<int>());
        Assert.Equal(790_000, j["waiting"]!["value"]!.GetValue<long>());
        Assert.Equal(1, j["alerts"]!["redOver24h"]!["count"]!.GetValue<int>());
        Assert.Equal(1, j["alerts"]!["yellowOver12h"]!["count"]!.GetValue<int>());
        Assert.Equal(1, j["receivedToday"]!["count"]!.GetValue<int>());
        Assert.Equal(70_000, j["receivedToday"]!["value"]!.GetValue<long>());
        Assert.Equal(1, j["receivedYesterday"]!["count"]!.GetValue<int>());
        Assert.Equal(1, j["watching"]!.GetValue<int>());
        Assert.Equal(1, j["archivedOrders"]!.GetValue<int>());
    }

    [Fact]
    public void Order_detail_has_items_codes_and_alert_but_no_recipient_data()
    {
        var o = Data[0].Order;
        o.TrackingCodes = new() { "ALT-1" };
        var r = Tools().Execute("order_detail", """{"code":"vn001"}""");
        Assert.Single(r.Refs);
        var d = J(r)["orders"]![0]!;
        Assert.Equal("VN001", d["track"]!.GetValue<string>());
        Assert.Equal(2, d["items"]![0]!["returnedQty"]!.GetValue<int>());
        Assert.Contains("ALT-1", d["otherCodes"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Contains("đỏ", d["alert"]!.GetValue<string>());
        foreach (var banned in new[] { "0912345678", "Nguyễn Trãi", "BuyerNote", "buyerNote" }) Assert.DoesNotContain(banned, r.Json);
        Assert.Contains("error", Tools().Execute("order_detail", "{}").Json);
        Assert.Contains("error", Tools().Execute("order_detail", """{"code":"ZZZZZZ"}""").Json);
        o.TrackingCodes = new();
    }

    [Fact]
    public void Recent_activity_is_masked_and_reports_when_unavailable()
    {
        IReadOnlyList<AuditEntry> Fake(int limit, string type, string text) => new[]
        {
            new AuditEntry(2, "25/11/2026 13:50:00", "scan", "Quét nhận kiện", "Nhận VN001, gọi 0987654321"),
            new AuditEntry(1, "25/11/2026 13:00:00", "import", "Nạp đơn", "Nạp 12 đơn"),
        };
        var raw = Tools(Fake).Execute("recent_activity", """{"limit":5}""").Json;
        var j = JsonNode.Parse(raw)!;
        Assert.Equal(2, j["count"]!.GetValue<int>());
        Assert.DoesNotContain("0987654321", raw);
        Assert.Contains("ẩn SĐT", raw);
        Assert.Contains("error", Tools().Execute("recent_activity", "{}").Json);
    }

    [Fact]
    public void Definitions_are_valid_json_with_all_tools_and_every_tool_has_a_description()
    {
        var arr = JsonNode.Parse(AiOrderTools.DefinitionsJson)!.AsArray();
        var names = arr.Select(t => t!["function"]!["name"]!.GetValue<string>()).ToArray();
        Assert.Equal(new[] { "overview", "count_orders", "aggregate", "list_orders", "find_order", "order_detail", "product_stats", "recent_activity" }, names);
        foreach (var t in arr)
        {
            Assert.False(string.IsNullOrWhiteSpace(t!["function"]!["description"]!.GetValue<string>()));
            Assert.Equal("object", t["function"]!["parameters"]!["type"]!.GetValue<string>());
        }
        Assert.Contains("Đang chờ", AiOrderTools.DefinitionsJson);   // chữ có dấu không bị mã hóa thành \u
    }
}

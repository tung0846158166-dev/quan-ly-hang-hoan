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
        Assert.Equal(new[] { "overview", "count_orders", "aggregate", "list_orders", "find_order", "order_detail", "product_stats", "recent_activity",
            "compare_periods", "order_groups", "return_watch", "system_status", "app_guide" }, names);
        foreach (var t in arr)
        {
            Assert.False(string.IsNullOrWhiteSpace(t!["function"]!["description"]!.GetValue<string>()));
            Assert.Equal("object", t["function"]!["parameters"]!["type"]!.GetValue<string>());
        }
        Assert.Contains("Đang chờ", AiOrderTools.DefinitionsJson);   // chữ có dấu không bị mã hóa thành \u
    }

    [Fact]
    public void Phone_rows_carry_price_and_carrier_for_the_phone_ai()
    {
        var o = O("9", "VN009", "Chưa trả", 123_456, carrier: "SPX Express", arrived: "25/11/2026 10:00:00");
        var row = QuanLyHangHoan.Data.Sync.PhoneSnapshot.BuildRow(S(o), Now);
        Assert.Equal(123_456L, row["pr"]);
        Assert.Equal("SPX Express", row["cr"]);
        var free = QuanLyHangHoan.Data.Sync.PhoneSnapshot.BuildRow(S(O("10", "VN010", "Chưa trả", 0, carrier: "")), Now);
        Assert.False(free.ContainsKey("pr"));
        Assert.False(free.ContainsKey("cr"));
    }

    // ---------------- công cụ chạy ngầm bổ sung ----------------

    [Fact]
    public void Compare_today_vs_yesterday_gives_counts_value_and_percent()
    {
        var j = J(Tools().Execute("compare_periods", """{"preset":"today_vs_yesterday","status":"Đã nhận"}"""));
        Assert.Equal(1, j["periodA"]!["count"]!.GetValue<long>());          // VN005 nhận hôm nay
        Assert.Equal(70_000, j["periodA"]!["value"]!.GetValue<long>());
        Assert.Equal(1, j["periodB"]!["count"]!.GetValue<long>());          // VN006 nhận hôm qua
        Assert.Equal(30_000, j["periodB"]!["value"]!.GetValue<long>());
        Assert.Equal(40_000, j["change"]!["value"]!["diff"]!.GetValue<long>());
        Assert.Equal(133.3, j["change"]!["value"]!["percent"]!.GetValue<double>());
        Assert.False(j["change"]!["count"]!.AsObject().ContainsKey("percent") && j["change"]!["count"]!["percent"]!.GetValue<double>() != 0);
    }

    [Fact]
    public void Compare_custom_ranges_default_previous_period_is_equal_length_right_before()
    {
        var j = J(Tools().Execute("compare_periods", """{"a_from":"2026-11-24","a_to":"2026-11-25","status":"Đã nhận"}"""));
        Assert.Equal(2, j["periodA"]!["count"]!.GetValue<long>());
        Assert.Equal("2026-11-22", j["periodB"]!["from"]!.GetValue<string>());
        Assert.Equal("2026-11-23", j["periodB"]!["to"]!.GetValue<string>());
        Assert.Equal(0, j["periodB"]!["count"]!.GetValue<long>());
        Assert.Contains("error", Tools().Execute("compare_periods", "{}").Json);
    }

    [Fact]
    public void Order_groups_lists_multi_parcel_orders_with_each_parcel()
    {
        var rows = new[]
        {
            S(O("21", "A1", "Hàng đã trả", 10_000, recv: "25/11/2026 09:00:00")),
            S(O("22", "A2", "Chưa trả", 20_000)),
            S(O("23", "B1", "Chưa trả", 5_000)),
        };
        rows[0].Order.OrderId = "SAME"; rows[1].Order.OrderId = "SAME";
        var tools = new AiOrderTools(() => rows, () => Now);
        var r = tools.Execute("order_groups", "{}");
        var g = J(r)["groups"]![0]!;
        Assert.Equal("SAME", g["orderId"]!.GetValue<string>());
        Assert.Equal(2, g["parcels"]!.GetValue<int>());
        Assert.Equal(1, g["received"]!.GetValue<int>());
        Assert.Equal(1, g["waiting"]!.GetValue<int>());
        Assert.Equal(30_000, g["value"]!.GetValue<long>());
        Assert.Equal(2, r.Refs.Count);
        Assert.Equal(1, J(r)["groupsTotal"]!.GetValue<int>());
    }

    [Fact]
    public void Return_watch_counts_by_stage_and_reports_when_unavailable()
    {
        IReadOnlyDictionary<string, (long Since, int Stage)> Marks(IEnumerable<StoredOrder> all) => new Dictionary<string, (long, int)>
        {
            ["1"] = (new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), 2),
            ["2"] = (new DateTimeOffset(2026, 11, 25, 8, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), 3),
            ["3"] = (new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), 3),
        };
        var t = new AiOrderTools(() => Data, () => Now, null, null, Marks);
        var j = J(t.Execute("return_watch", "{}"));
        Assert.Equal(3, j["total"]!.GetValue<int>());
        Assert.Equal(1, j["byStage"]!["Đang về"]!["count"]!.GetValue<int>());
        Assert.Equal(2, j["byStage"]!["Đã về shop"]!["count"]!.GetValue<int>());
        Assert.Equal(2, J(t.Execute("return_watch", """{"stage":"3"}"""))["orders"]!.AsArray().Count);
        Assert.Contains("error", Tools().Execute("return_watch", "{}").Json);
    }

    [Fact]
    public void System_status_merges_counts_with_provider_facts()
    {
        var t = new AiOrderTools(() => Data, () => Now, null, () => new Dictionary<string, object?> { ["phoneSync"] = "đang kết nối", ["unreadNotifications"] = 3, ["lastBackup"] = null });
        var j = J(t.Execute("system_status", "{}"));
        Assert.Equal(7, j["orders"]!["active"]!.GetValue<int>());
        Assert.Equal(1, j["orders"]!["archived"]!.GetValue<int>());
        Assert.Equal("đang kết nối", j["phoneSync"]!.GetValue<string>());
        Assert.Equal(3, j["unreadNotifications"]!.GetValue<int>());
        Assert.NotNull(J(Tools().Execute("system_status", "{}"))["orders"]);
    }

    [Fact]
    public void App_guide_lists_topics_finds_by_keyword_and_never_invents()
    {
        var idx = J(Tools().Execute("app_guide", "{}"))["topics"]!.AsArray();
        Assert.True(idx.Count >= 10);
        Assert.Contains("nap_don", idx.Select(x => x!["topic"]!.GetValue<string>()));
        Assert.Contains("Google Sheet", Tools().Execute("app_guide", """{"topic":"nap_don"}""").Json);
        Assert.Contains("sao lưu", Tools().Execute("app_guide", """{"topic":"sao lưu"}""").Json);
        Assert.Contains("MQTT", Tools().Execute("app_guide", """{"topic":"điện thoại"}""").Json);
        Assert.Contains("error", Tools().Execute("app_guide", """{"topic":"zzzzqqqq"}""").Json);
    }

    [Fact]
    public void Extra_group_by_options_work()
    {
        string First(string groupBy, string extra = "") =>
            J(Tools().Execute("aggregate", $$"""{"status":"Đang chờ","group_by":"{{groupBy}}"{{extra}}}"""))["groups"]![0]!["group"]!.GetValue<string>();
        Assert.Equal("12-24h", First("waiting_age"));                    // 4 nhóm đều 1 đơn: hòa điểm thì xếp theo tên
        var ages = J(Tools().Execute("aggregate", """{"status":"Đang chờ","group_by":"waiting_age"}"""))["groups"]!.AsArray().Select(x => x!["group"]!.GetValue<string>()).ToArray();
        Assert.Contains("24-48h", ages);   // VN001: 28 giờ
        Assert.Contains("12-24h", ages);   // VN002: 14 giờ
        Assert.Contains("<12h", ages);     // VN003: 4 giờ
        Assert.Equal("100k-300k", First("price_range"));                  // 100k và 250k cùng khoảng: 2 đơn
        Assert.Equal("hoàn toàn bộ", First("partial"));
        var watching = J(Tools().Execute("aggregate", """{"status":"Đang chờ","group_by":"watching"}"""))["groups"]!.AsArray();
        Assert.Equal(2, watching.Count);
        var hours = J(Tools().Execute("aggregate", """{"status":"Đang chờ","group_by":"arrival_hour"}"""))["groups"]!.AsArray();
        Assert.True(hours.Count >= 2);
        var reason = J(Tools().Execute("aggregate", """{"group_by":"reason"}"""))["groups"]![0]!["group"]!.GetValue<string>();
        Assert.Equal("(không ghi lý do)", reason);
        Assert.NotEmpty(J(Tools().Execute("aggregate", """{"group_by":"weekday"}"""))["groups"]!.AsArray());
    }
}

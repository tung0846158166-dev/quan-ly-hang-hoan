using System.IO;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.Tests;

/// <summary>Tạo đơn mới (nhập tay / điện thoại): dữ liệu GIẢ.</summary>
public sealed class NewOrderServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 24, 12, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-no-" + Guid.NewGuid().ToString("N"));
    public NewOrderServiceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private string NewDb() { var db = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".db"); AppDatabase.Initialize(db); return db; }

    [Fact]
    public void Manual_order_is_created_in_the_main_table_with_the_marker_and_the_typed_details()
    {
        var db = NewDb();
        var r = NewOrderService.Create(db, new NewOrderInput("spxvn0000000001", "OD-1", "Shopee", "", "Áo Polo", "Nâu", "L", 2, "khách báo hoàn"), Now);
        Assert.True(r.Ok); Assert.Equal("success", r.Outcome); Assert.Equal("SPXVN0000000001", r.Code);
        var o = Assert.Single(OrderRepository.Load(db)).Order;
        Assert.Equal(("SPXVN0000000001", "OD-1", "Shopee", ReceiveStatus.NotReturned), (o.Track, o.OrderId, o.Platform, o.Status));
        Assert.True(NewOrderMarker.IsNewlyCreated(o));
        Assert.Equal("khách báo hoàn", o.Note);
        var item = Assert.Single(o.Items);
        Assert.Equal(("Áo Polo", "Nâu", "L", 2), (item.Product, item.Color, item.Size, item.Qty));
    }

    [Fact]
    public void Blank_fields_get_sensible_defaults_and_the_platform_is_detected_from_the_code()
    {
        var db = NewDb();
        var r = NewOrderService.Create(db, new NewOrderInput("SPXVN0000000002"), Now);
        Assert.True(r.Ok);
        var o = Assert.Single(OrderRepository.Load(db)).Order;
        Assert.Equal("Shopee", o.Platform);
        Assert.Equal("SPXVN0000000002", o.OrderId);                       // không nhập mã đơn: dùng mã vận đơn
        Assert.Equal("Sản phẩm mới (nhập tay)", o.Items[0].Product);
        Assert.Equal(1, o.Items[0].Qty);
        Assert.Equal("Đơn tạo tay trên máy tính", o.Note);
        Assert.False(string.IsNullOrWhiteSpace(o.Carrier));
    }

    [Fact]
    public void Empty_code_is_invalid_and_writes_nothing()
    {
        var db = NewDb();
        var r = NewOrderService.Create(db, new NewOrderInput("   "), Now);
        Assert.False(r.Ok); Assert.Equal("invalid", r.Outcome);
        Assert.Empty(OrderRepository.Load(db));
    }

    [Fact]
    public void An_existing_code_is_never_created_again_even_if_archived_or_deleted_or_typed_in_another_case()
    {
        var db = NewDb();
        Assert.True(NewOrderService.Create(db, new NewOrderInput("TTVN0000000003"), Now).Ok);
        var again = NewOrderService.Create(db, new NewOrderInput("ttvn0000000003"), Now);
        Assert.False(again.Ok); Assert.Equal("duplicate", again.Outcome);
        Assert.Contains("BẢNG CHÍNH", again.Message);
        var id = OrderRepository.Load(db).Single().Order.Id;
        OrderEditor.Delete(db, id, "main", Now);
        var afterDelete = NewOrderService.Create(db, new NewOrderInput("TTVN0000000003"), Now);
        Assert.Equal("duplicate", afterDelete.Outcome);
        Assert.Contains("ĐÃ XÓA", afterDelete.Message);                   // không "hồi sinh" đơn đã xóa
        Assert.Single(OrderRepository.Load(db));
    }

    [Fact]
    public void Phone_origin_keeps_the_phone_wording_and_is_also_marked_as_new()
    {
        var db = NewDb();
        var r = NewOrderService.Create(db, new NewOrderInput("SPXVN0000000004", Origin: "phone"), Now);
        Assert.True(r.Ok);
        var o = Assert.Single(OrderRepository.Load(db)).Order;
        Assert.Equal("Đơn mới tạo từ máy quét điện thoại", o.Note);
        Assert.Equal("Sản phẩm mới (Quét điện thoại)", o.Items[0].Product);
        Assert.True(NewOrderMarker.IsNewlyCreated(o));
    }

    [Fact]
    public void Zero_or_negative_quantity_becomes_one_and_long_text_is_clipped()
    {
        var db = NewDb();
        NewOrderService.Create(db, new NewOrderInput("SPXVN0000000005", Product: new string('A', 500), Qty: -3, Note: new string('N', 900)), Now);
        var o = OrderRepository.Load(db).Single().Order;
        Assert.Equal(1, o.Items[0].Qty);
        Assert.Equal(200, o.Items[0].Product.Length);
        Assert.Equal(300, o.Note.Length);
    }

    [Fact]
    public void Marker_recognizes_old_phone_orders_by_their_default_note_but_not_ordinary_orders()
    {
        Assert.True(NewOrderMarker.IsNewlyCreated(new Order { Note = "Đơn mới tạo từ máy quét điện thoại" }));
        Assert.True(NewOrderMarker.IsNewlyCreated(new Order { ImportWeek = NewOrderMarker.Week }));
        Assert.False(NewOrderMarker.IsNewlyCreated(new Order { Note = "Chọn nhầm size", ImportWeek = "2026-W47" }));
    }
}

using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Legacy;

namespace QuanLyHangHoan.Tests;

/// <summary>Thao tác thủ công trên đơn: mọi thay đổi có lịch sử, xóa là xóa mềm, không ghi đè/xóa dữ liệu cũ. Dữ liệu GIẢ.</summary>
public sealed class OrderEditorTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qlhh-edit-" + Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(_dir, "t.db");

    public OrderEditorTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "du_lieu_hang_hoan.json"), JsonSerializer.Serialize(new[]
        {
            new { platform = "Shopee", orderType = "Trả hàng", orderId = "O1", track = "A", status = "Chưa trả", note = "lý do gốc", price = 1, items = Array.Empty<object>() },
            new { platform = "Shopee", orderType = "Trả hàng", orderId = "O1", track = "B", status = "Chưa trả", note = "", price = 1, items = Array.Empty<object>() },
        }));
        LegacyImporter.Import(_dir, Db);
    }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private StoredOrder Get(string track) => OrderRepository.Load(Db).Single(o => o.Order.Track == track);
    private string IdOf(string track) => Get(track).Order.Id;

    private long Count(string sql)
    {
        using var c = new SqliteConnection($"Data Source={Db};Pooling=False"); c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [Fact]
    public void Status_change_is_saved_with_history_linked_to_the_order()
    {
        var id = IdOf("A");
        var r = OrderEditor.SetField(Db, id, EditField.Status, ReceiveStatus.Lost, Now);

        Assert.True(r.Ok);
        Assert.Equal(ReceiveStatus.Lost, Get("A").Order.Status);
        Assert.Equal("Chưa trả", Get("B").Order.Status);                        // kiện B không bị ảnh hưởng
        Assert.Equal(1, Count($"SELECT COUNT(*) FROM cell_edit_history WHERE linked_order_key='{id}' AND field='status' AND old_val='Chưa trả' AND new_val='Bị mất' AND edit_type='manual'"));
        Assert.Equal(1, Count("SELECT COUNT(*) FROM audit_log WHERE type='edit'"));
    }

    [Fact]
    public void Marking_received_by_hand_sets_receive_time_once()
    {
        var id = IdOf("A");
        OrderEditor.SetField(Db, id, EditField.Status, ReceiveStatus.Received, Now);
        Assert.Equal("23/11/2026 12:00:00", Get("A").Order.RecvDate);

        OrderEditor.SetField(Db, id, EditField.Status, ReceiveStatus.Received, Now.AddHours(1));
        Assert.Equal("23/11/2026 12:00:00", Get("A").Order.RecvDate);           // đã nhận: bấm lại không ghi đè giờ nhận

        // chủ dự án 28/11/2026: đổi về "Chưa trả" = chưa nhận -> bỏ ngày nhận; nhận lại thì ghi giờ mới
        OrderEditor.SetField(Db, id, EditField.Status, ReceiveStatus.NotReturned, Now.AddHours(1));
        Assert.Equal("", Get("A").Order.RecvDate);
        OrderEditor.SetField(Db, id, EditField.Status, ReceiveStatus.Received, Now.AddHours(2));
        Assert.Equal("23/11/2026 14:00:00", Get("A").Order.RecvDate);
    }

    [Fact]
    public void Invalid_status_is_rejected_and_nothing_changes()
    {
        var r = OrderEditor.SetField(Db, IdOf("A"), EditField.Status, "trạng thái bậy", Now);
        Assert.False(r.Ok);
        Assert.Equal(EditKind.Invalid, r.Kind);
        Assert.Equal("Chưa trả", Get("A").Order.Status);
        Assert.Equal(0, Count("SELECT COUNT(*) FROM cell_edit_history"));
    }

    [Fact]
    public void Expected_old_value_protects_against_lost_update()
    {
        var id = IdOf("A");
        OrderEditor.SetField(Db, id, EditField.UserNote, "ghi chú 1", Now);

        var stale = OrderEditor.SetField(Db, id, EditField.UserNote, "ghi chú 2", Now, expectedOld: "");
        Assert.Equal(EditKind.Conflict, stale.Kind);
        Assert.Equal("ghi chú 1", Get("A").Order.UserNote);

        var ok = OrderEditor.SetField(Db, id, EditField.UserNote, "ghi chú 2", Now, expectedOld: "ghi chú 1");
        Assert.True(ok.Ok);
    }

    [Fact]
    public void Same_value_is_a_noop_without_history()
    {
        var r = OrderEditor.SetField(Db, IdOf("A"), EditField.Status, "Chưa trả", Now);
        Assert.True(r.Ok);
        Assert.Equal(EditKind.NoChange, r.Kind);
        Assert.Equal(0, Count("SELECT COUNT(*) FROM cell_edit_history"));
    }

    [Fact]
    public void Note_and_watchlist_edits_keep_the_original_reason_in_history()
    {
        var id = IdOf("A");
        OrderEditor.SetField(Db, id, EditField.Note, "lý do mới", Now);
        OrderEditor.SetField(Db, id, EditField.IsWatchlist, "1", Now);
        OrderEditor.SetField(Db, id, EditField.WatchlistReason, "khách hay hoàn", Now);

        var o = Get("A").Order;
        Assert.Equal("lý do mới", o.Note);
        Assert.True(o.IsWatchlist);
        Assert.Equal("khách hay hoàn", o.WatchlistReason);
        Assert.Equal(1, Count($"SELECT COUNT(*) FROM cell_edit_history WHERE linked_order_key='{id}' AND field='note' AND old_val='lý do gốc'"));
    }

    [Fact]
    public void Soft_delete_keeps_the_row_hides_it_from_active_and_restore_brings_it_back()
    {
        var id = IdOf("A");
        var d = OrderEditor.Delete(Db, id, "Bảng chính", Now);
        Assert.True(d.Ok);

        Assert.Equal(2, OrderRepository.Load(Db).Count);                        // dòng vẫn còn
        Assert.True(Get("A").IsDeleted);
        Assert.Equal("23/11/2026 12:00:00", Get("A").DeletedAt);
        Assert.Equal("Bảng chính", Get("A").DeletedFrom);
        Assert.DoesNotContain(OrderRepository.LoadActive(Db), o => o.Track == "A");
        Assert.Contains(OrderRepository.LoadActive(Db), o => o.Track == "B");

        var r = OrderEditor.Restore(Db, id, Now.AddHours(1));
        Assert.True(r.Ok);
        Assert.False(Get("A").IsDeleted);
        Assert.Contains(OrderRepository.LoadActive(Db), o => o.Track == "A");
        Assert.Equal(2, Count("SELECT COUNT(*) FROM audit_log WHERE type IN ('delete','restore')"));
    }

    [Fact]
    public void Deleted_order_cannot_be_edited_until_restored_and_double_delete_is_harmless()
    {
        var id = IdOf("A");
        OrderEditor.Delete(Db, id, "Bảng chính", Now);

        Assert.Equal(EditKind.NotEditable, OrderEditor.SetField(Db, id, EditField.Status, ReceiveStatus.Received, Now).Kind);
        Assert.Equal(EditKind.NoChange, OrderEditor.Delete(Db, id, "Bảng chính", Now.AddHours(1)).Kind);
        Assert.Equal("23/11/2026 12:00:00", Get("A").DeletedAt);                // lần xóa đầu giữ nguyên
    }

    [Fact]
    public void Archive_and_unarchive_toggle_the_flag_not_the_data()
    {
        var id = IdOf("A");
        Assert.True(OrderEditor.Archive(Db, id, Now).Ok);
        Assert.True(Get("A").IsArchived);
        Assert.Equal("23/11/2026 12:00:00", Get("A").ArchivedAt);
        Assert.DoesNotContain(OrderRepository.LoadActive(Db), o => o.Track == "A");

        Assert.True(OrderEditor.Unarchive(Db, id, Now.AddHours(1)).Ok);
        Assert.False(Get("A").IsArchived);
        Assert.Equal("", Get("A").ArchivedAt);
        Assert.Contains(OrderRepository.LoadActive(Db), o => o.Track == "A");
    }

    [Fact]
    public void Unknown_id_is_not_found_and_changes_nothing()
    {
        Assert.Equal(EditKind.NotFound, OrderEditor.SetField(Db, "khong-co", EditField.Status, ReceiveStatus.Lost, Now).Kind);
        Assert.Equal(EditKind.NotFound, OrderEditor.Delete(Db, "khong-co", "x", Now).Kind);
        Assert.Equal(0, Count("SELECT COUNT(*) FROM audit_log WHERE type IN ('edit','delete')"));
    }

    [Fact]
    public void Missing_database_is_not_found_without_creating_a_file()
    {
        var none = Path.Combine(_dir, "none.db");
        Assert.Equal(EditKind.NotFound, OrderEditor.Delete(none, "x", "y", Now).Kind);
        Assert.False(File.Exists(none));
    }

    [Fact]
    public void History_is_readable_newest_first_for_one_order()
    {
        var id = IdOf("A");
        OrderEditor.SetField(Db, id, EditField.UserNote, "1", Now);
        OrderEditor.SetField(Db, id, EditField.UserNote, "2", Now.AddMinutes(1));
        OrderEditor.SetField(Db, IdOf("B"), EditField.UserNote, "khác", Now);

        var h = OrderEditor.History(Db, id);

        Assert.Equal(2, h.Count);
        Assert.Equal("2", h[0].NewValue);
        Assert.Equal("1", h[0].OldValue);
        Assert.Equal("1", h[1].NewValue);
    }
}

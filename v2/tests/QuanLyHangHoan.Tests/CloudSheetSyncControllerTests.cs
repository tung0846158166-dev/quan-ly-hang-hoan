using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.Data.Sheet;

namespace QuanLyHangHoan.Tests;

public sealed class CloudSheetSyncControllerTests
{
    [Fact]
    public async Task Cloud_mode_never_runs_the_pc_sheet_import_and_keeps_the_old_switch()
    {
        var ran = 0;
        var inner = new FakeSheetSyncController(_ => { ran++; return new SheetRunResult { Ran = true, Ok = true }; });
        Assert.True(inner.SetUrl("https://script.google.com/macros/s/abc/exec", out _));
        inner.UpdateConfig(true, "A,B", "30", null, true);
        var completed = 0;
        var c = new CloudSheetSyncController(inner);
        c.Completed += _ => completed++;

        var r = await c.RunOnceAsync(manual: true);
        Assert.False(r.Ran);
        Assert.Equal(CloudSheetSyncController.CloudNote, r.Note);
        Assert.Equal(0, ran);
        Assert.Equal(0, completed);
        Assert.False(c.Enabled);
        Assert.Equal(CloudSheetSyncController.CloudNote, c.LastNote);

        // đổi tab/ngày trong cài đặt vẫn lưu, nhưng KHÔNG tắt công tắc gốc (tắt cloud thì máy tính nạp lại như cũ)
        c.UpdateConfig(false, "C", "10", null, false);
        Assert.True(inner.Enabled);
        Assert.Equal("C", inner.Tabs);
        Assert.Equal("10", c.MaxDays);
    }
}

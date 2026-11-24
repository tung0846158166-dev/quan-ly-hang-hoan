using System.IO;
using System.Windows;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.App.Views;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Ai;

namespace QuanLyHangHoan.Tests.UiTests;

/// <summary>Màn "Hỏi AI": gửi câu hỏi (AI GIẢ, không gọi mạng), danh sách đơn bấm được, lỗi hiển thị rõ, khóa không lộ.</summary>
public sealed class T041AiChatUiTests
{
    private sealed class FakeChat(Func<string, AiAnswer> reply, Dictionary<string, Order> orders) : IAiChat
    {
        public int Resets;
        public Task<AiAnswer> AskAsync(string question, CancellationToken ct = default) => Task.FromResult(reply(question));
        public void Reset() => Resets++;
        public Order? TryGetOrder(string id) => orders.TryGetValue(id, out var o) ? o : null;
    }

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "qlhh-aiui-" + Guid.NewGuid().ToString("N"));

    private static (AiChatViewModel Vm, FakeChat Chat, List<Order> Opened, string Dir) Rig(Func<string, AiAnswer>? reply = null, bool withKey = true)
    {
        var dir = TempDir();
        var store = new AiSettingsStore(dir);
        if (withKey) store.SaveKey("gsk_ui_test_key");
        var o1 = new Order { Id = "a1", OrderId = "SHP-A1", Track = "VN001", Status = ReceiveStatus.Received };
        var o2 = new Order { Id = "a2", OrderId = "SHP-A2", Track = "VN002", Status = ReceiveStatus.Received };
        var chat = new FakeChat(reply ?? (_ => new AiAnswer("Hôm nay có 2 kiện về.",
            new[] { new AiOrderRef("a1", "SHP-A1", "VN001", ReceiveStatus.Received), new AiOrderRef("a2", "SHP-A2", "VN002", ReceiveStatus.Received) },
            new[] { "count_orders({\"from\":\"today\"})" })),
            new() { ["a1"] = o1, ["a2"] = o2 });
        var opened = new List<Order>();
        var vm = new AiChatViewModel(store, () => Array.Empty<StoredOrder>(), () => new DateTime(2026, 11, 25, 14, 0, 0), opened.Add, _ => chat);
        return (vm, chat, opened, dir);
    }

    private static async Task WaitIdle(AiChatViewModel vm)
    {
        for (var i = 0; i < 200 && vm.IsBusy; i++) await Task.Delay(10);
    }

    [Fact]
    public void Send_adds_question_and_answer_with_clickable_orders_and_opens_detail()
    {
        var r = Rig();
        try
        {
            WpfTestHost.Run(() =>
            {
                r.Vm.Input = "hôm nay có bao nhiêu đơn về?";
                Assert.True(r.Vm.SendCommand.CanExecute(null));
                r.Vm.SendCommand.Execute(null);
                WpfTestHost.RunTask(() => WaitIdle(r.Vm));
                Assert.Equal(2, r.Vm.Messages.Count);
                Assert.True(r.Vm.Messages[0].IsUser);
                Assert.Equal("Hôm nay có 2 kiện về.", r.Vm.Messages[1].Text);
                Assert.Equal(2, r.Vm.Messages[1].Refs.Count);
                Assert.Contains("count_orders", r.Vm.Messages[1].Trace);
                Assert.Equal(string.Empty, r.Vm.Input);

                r.Vm.OpenRefCommand.Execute(r.Vm.Messages[1].Refs[1]);
                Assert.Single(r.Opened);
                Assert.Equal("SHP-A2", r.Opened[0].OrderId);
            });
        }
        finally { try { Directory.Delete(r.Dir, true); } catch { } }
    }

    [Fact]
    public void Api_errors_show_a_clear_message_and_the_screen_keeps_working()
    {
        var r = Rig(_ => throw new AiException("Khóa API không đúng hoặc đã bị thu hồi."));
        try
        {
            WpfTestHost.Run(() =>
            {
                r.Vm.Input = "xin chào";
                r.Vm.SendCommand.Execute(null);
                WpfTestHost.RunTask(() => WaitIdle(r.Vm));
                Assert.Contains("Khóa API không đúng", r.Vm.Messages.Last().Text);
                Assert.False(r.Vm.IsBusy);
                Assert.DoesNotContain("gsk_ui_test_key", r.Vm.Messages.Last().Text);
                r.Vm.Input = "thử lại";
                Assert.True(r.Vm.SendCommand.CanExecute(null));
            });
        }
        finally { try { Directory.Delete(r.Dir, true); } catch { } }
    }

    [Fact]
    public void Without_a_key_sending_is_disabled_and_status_explains_why()
    {
        var r = Rig(withKey: false);
        try
        {
            WpfTestHost.Run(() =>
            {
                r.Vm.Input = "xin chào";
                Assert.False(r.Vm.SendCommand.CanExecute(null));
                Assert.Contains("khóa API", r.Vm.StatusText);
            });
        }
        finally { try { Directory.Delete(r.Dir, true); } catch { } }
    }

    [Fact]
    public void New_chat_clears_messages_and_resets_server_side_history()
    {
        var r = Rig();
        try
        {
            WpfTestHost.Run(() =>
            {
                r.Vm.Input = "a";
                r.Vm.SendCommand.Execute(null);
                WpfTestHost.RunTask(() => WaitIdle(r.Vm));
                r.Vm.NewChatCommand.Execute(null);
                Assert.Empty(r.Vm.Messages);
                Assert.True(r.Vm.IsEmpty);
                Assert.Equal(1, r.Chat.Resets);
            });
        }
        finally { try { Directory.Delete(r.Dir, true); } catch { } }
    }

    [Fact]
    public void Demo_mode_without_store_is_unavailable()
    {
        WpfTestHost.Run(() =>
        {
            var vm = new AiChatViewModel(null, null, () => DateTime.Now, _ => { });
            Assert.False(vm.Available);
            vm.Input = "x";
            Assert.False(vm.SendCommand.CanExecute(null));
        });
    }

    [Fact]
    public void RenderScreenshots_AiChat_ToReportsFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(Directory.Exists(Path.Combine(dir.FullName, "tasks")) && Directory.Exists(Path.Combine(dir.FullName, "v2")))) dir = dir.Parent;
        var outDir = dir is null ? Path.Combine(Path.GetTempPath(), "T041-screens") : Path.Combine(dir.FullName, "tasks", "reports", "T041-screens");
        Directory.CreateDirectory(outDir);
        var r = Rig();
        try
        {
            WpfTestHost.Run(() =>
            {
                var view = new AiChatView { DataContext = r.Vm };
                var window = new Window
                {
                    Content = view, Width = 1100, Height = 760, WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false,
                };
                try
                {
                    window.Show();
                    WpfTestHost.PumpDispatcher();
                    WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "01-empty.png"));
                    r.Vm.Input = "hôm nay có bao nhiêu đơn về?";
                    r.Vm.SendCommand.Execute(null);
                    WpfTestHost.RunTask(() => WaitIdle(r.Vm));
                    WpfTestHost.PumpDispatcher();
                    WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "02-answer-with-orders.png"));
                    r.Vm.ShowSettings = true;
                    WpfTestHost.PumpDispatcher();
                    WpfTestHost.SaveWindowPng(window, Path.Combine(outDir, "03-settings.png"));
                }
                finally { window.Close(); WpfTestHost.PumpDispatcher(); }
            });
            Assert.True(File.Exists(Path.Combine(outDir, "02-answer-with-orders.png")));
        }
        finally { try { Directory.Delete(r.Dir, true); } catch { } }
    }
}

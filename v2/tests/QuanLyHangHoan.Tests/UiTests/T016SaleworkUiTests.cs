using System.IO;
using System.Windows;
using QuanLyHangHoan.App;
using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Import;
using Xunit;

namespace QuanLyHangHoan.Tests.UiTests;

public sealed class T016SaleworkUiTests
{
    private static readonly DateTime FixedNow = new(2026, 11, 23, 15, 30, 0);
    private const string FakeSaleworkUrl = "https://salework.example.test/don-hoan";

    [Theory]
    [InlineData("Cập nhật đơn hoàn", true)]
    [InlineData("cập nhật", true)]
    [InlineData("CAP NHAT", true)]
    [InlineData("CẬP NHẬT", true)]
    [InlineData("Cập   nhật", true)]
    [InlineData("cap nhật", true)]
    [InlineData("cập nhat", true)]
    [InlineData("sync Cập nhật dữ liệu", true)]
    [InlineData("Xuất excel", false)]
    [InlineData("Đang chờ hoàn", false)]
    [InlineData("Theo bộ lọc", false)]
    [InlineData("Bắt Đầu Xuất", false)]
    [InlineData("Kiểm tra, đối soát", false)]
    public void ForbiddenFilter_RejectsCapNhatVariants_AllowsSafeButtons(string buttonText, bool expectedForbidden)
    {
        Assert.Equal(expectedForbidden, SaleworkPageScript.IsForbiddenText(buttonText));
        Assert.Equal(!expectedForbidden, SaleworkPageScript.IsSafeClickTarget(buttonText, buttonText));
    }

    [Fact]
    public void ForbiddenFilter_RejectsIfTopElementAtPointHasCapNhat()
    {
        Assert.False(SaleworkPageScript.IsSafeClickTarget("Xuất excel", "Xuất excel", "Cập nhật đơn hoàn"));
        var js = SaleworkPageScript.BuildScript("click", new { text = "Xuất excel" });
        Assert.Contains(@"FORBID = /c[aậ]p\s*nh[aậ]t/i", js);
        Assert.Contains("FORBID.test(a.text)", js);
        Assert.Contains("FORBID.test(c[0].textContent)", js);
    }

    [Fact]
    public void SaveCurrentPage_ValidAndInvalidUrls_EnforcesHttpsAndRejectsLogin()
    {
        using var controller = new FakeSaleworkController(clock: () => FixedNow);
        using var vm = new SaleworkViewModel(controller, new StubConfirm(true), clock: () => FixedNow);

        // Chưa mở cửa sổ đăng nhập
        vm.SaveCurrentPageCommand.Execute(null);
        Assert.False(vm.Configured);
        Assert.True(vm.HasActionError);

        // http:// bị từ chối
        Assert.False(controller.OpenLogin("http://salework.example.test/don-hoan", out var openErr));
        Assert.Contains("https://", openErr);

        // Trang login/signin/dang-nhap bị từ chối
        foreach (var bad in new[]
        {
            "https://salework.example.test/login",
            "https://salework.example.test/signin?next=1",
            "https://salework.example.test/dang-nhap"
        })
        {
            controller.SetFakeLoginUrl(bad);
            vm.SaveCurrentPageCommand.Execute(null);
            Assert.False(vm.Configured);
            Assert.True(vm.HasActionError);
        }

        // Trang hợp lệ được lưu, không lộ URL trên UI hay nhật ký
        controller.SetFakeLoginUrl(FakeSaleworkUrl);
        vm.SaveCurrentPageCommand.Execute(null);
        Assert.True(vm.Configured);
        Assert.False(vm.HasActionError);
        Assert.DoesNotContain(FakeSaleworkUrl, vm.ConfigStatusText);
        Assert.DoesNotContain(FakeSaleworkUrl, vm.Logs[0].SummaryText);
    }

    [Fact]
    public void RunNow_RunsInBackground_LocksButtons_AndReloadsMainViewModel()
    {
        WpfTestHost.Run(() =>
        {
            var fakeData = new FakeDataService(enableMutableDemoStore: true);
            var initialCount = fakeData.GetReturnOrders(500).Count;
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var controller = new FakeSaleworkController(
                runHandler: async ct =>
                {
                    await gate.Task.WaitAsync(ct);
                    return new SaleworkRunResult
                    {
                        Ran = true,
                        Ok = true,
                        RowCount = 5,
                        FileName = "sw_test.xlsx",
                        Import = new SaleworkImportResult
                        {
                            Ok = true,
                            Import = new TikTokImportResult { Ok = true, Added = 2, Updated = 1 },
                            SupersededMerged = 2,
                            SupersedeAmbiguous = 1
                        }
                    };
                },
                clock: () => FixedNow);

            controller.SetFakeLoginUrl(FakeSaleworkUrl);
            Assert.True(controller.SaveCurrentPage(out _));

            using var mainVm = new MainViewModel(
                fakeData,
                fakeData,
                fakeData,
                fakeData,
                new FakeLegacyImporter(fakeData.GetCurrentTime),
                new StubFolderPicker(),
                new StubFileSave(),
                isDemoMode: true,
                archiveSweeper: new NoOpArchiveSweeper(),
                photoProvider: new NoOpPhotoProvider(),
                dispatchToUi: a => WpfTestHost.Dispatcher.Invoke(a),
                saleworkController: controller);

            var swVm = mainVm.SaleworkVm;
            Assert.True(swVm.CanRunNow);
            Assert.False(swVm.CanStop);

            var runTask = swVm.RunNowCommand.ExecuteAsync(null);
            WpfTestHost.PumpDispatcher();

            Assert.True(swVm.IsRunning);
            Assert.False(swVm.CanRunNow);
            Assert.True(swVm.CanStop);
            Assert.Equal("Đang lấy...", swVm.RunNowButtonText);

            gate.SetResult(true);
            WpfTestHost.RunTask(() => runTask);
            WpfTestHost.PumpDispatcher();

            Assert.False(swVm.IsRunning);
            Assert.True(swVm.CanRunNow);
            Assert.False(swVm.CanStop);
            Assert.Equal("Lấy ngay", swVm.RunNowButtonText);
            Assert.True(swVm.LastOk);
            Assert.True(swVm.HasAmbiguousWarning);
            Assert.Contains("1 đơn có nhiều kiện khác nhau, giữ nguyên để bạn xem", swVm.AmbiguousWarningText);
            Assert.Contains("Đã lấy 5 dòng từ Salework và nạp vào bảng", mainVm.ToastMessage);
            Assert.Contains("gộp 2 dòng chờ cũ", mainVm.ToastMessage);
            Assert.Equal(initialCount, fakeData.GetReturnOrders(500).Count);
        });
    }

    [Fact]
    public async Task RunNow_ReloginError_ShowsHintAndLogsErrorStep()
    {
        using var controller = new FakeSaleworkController(
            runHandler: _ => Task.FromResult(new SaleworkRunResult
            {
                Ran = true,
                Ok = false,
                NeedsRelogin = true,
                Step = "mở trang",
                Error = "Salework yêu cầu đăng nhập lại."
            }),
            clock: () => FixedNow);

        controller.SetFakeLoginUrl(FakeSaleworkUrl);
        Assert.True(controller.SaveCurrentPage(out _));

        using var vm = new SaleworkViewModel(controller, new StubConfirm(true), clock: () => FixedNow);
        await vm.RunNowCommand.ExecuteAsync(null);

        Assert.False(vm.LastOk);
        Assert.True(vm.IsStatusError);
        Assert.True(vm.ShowReloginHint);
        Assert.Equal("mở trang", vm.LastErrorStep);
        Assert.Contains("mở trang", vm.LastStatusText);
        Assert.True(vm.Logs[0].IsError);
    }

    [Fact]
    public void Stop_CancelsRunningRun_AndLogsStopped()
    {
        WpfTestHost.Run(() =>
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var controller = new FakeSaleworkController(
                runHandler: async ct =>
                {
                    started.TrySetResult(true);
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);
                    return new SaleworkRunResult { Ran = true, Ok = true };
                },
                clock: () => FixedNow);

            controller.SetFakeLoginUrl(FakeSaleworkUrl);
            Assert.True(controller.SaveCurrentPage(out _));

            using var vm = new SaleworkViewModel(
                controller,
                new StubConfirm(true),
                dispatchToUi: a => WpfTestHost.Dispatcher.Invoke(a),
                clock: () => FixedNow);

            var runTask = vm.RunNowCommand.ExecuteAsync(null);
            WpfTestHost.RunTask(() => started.Task);
            WpfTestHost.PumpDispatcher();

            Assert.True(vm.IsRunning);
            vm.StopCommand.Execute(null);
            WpfTestHost.RunTask(() => runTask);
            WpfTestHost.PumpDispatcher();

            Assert.False(vm.IsRunning);
            Assert.False(vm.LastOk);
            Assert.Equal("Đã dừng", vm.Logs[0].StatusText);
        });
    }

    [Fact]
    public async Task Forget_RequiresConfirmation_BeforeClearingState()
    {
        using var controller = new FakeSaleworkController(clock: () => FixedNow);
        controller.SetFakeLoginUrl(FakeSaleworkUrl);
        Assert.True(controller.SaveCurrentPage(out _));

        var confirm = new StubConfirm(false);
        using var vm = new SaleworkViewModel(controller, confirm, clock: () => FixedNow);
        Assert.True(vm.Configured);

        await vm.ForgetCommand.ExecuteAsync(null);
        Assert.Equal(1, confirm.CallCount);
        Assert.True(vm.Configured);
        Assert.Equal(0, controller.ForgetCount);

        confirm.Result = true;
        await vm.ForgetCommand.ExecuteAsync(null);
        Assert.Equal(2, confirm.CallCount);
        Assert.False(vm.Configured);
        Assert.Equal(1, controller.ForgetCount);
    }

    [Fact]
    public void DemoMode_DoesNotCreateWebView2_AndSidebarHasSaleworkUnderGoogleSheet()
    {
        WpfTestHost.Run(() =>
        {
            var fakeData = new FakeDataService(enableMutableDemoStore: true);
            using var controller = new FakeSaleworkController(clock: () => FixedNow);
            using var mainVm = new MainViewModel(
                fakeData,
                fakeData,
                fakeData,
                fakeData,
                new FakeLegacyImporter(fakeData.GetCurrentTime),
                new StubFolderPicker(),
                new StubFileSave(),
                isDemoMode: true,
                archiveSweeper: new NoOpArchiveSweeper(),
                photoProvider: new NoOpPhotoProvider(),
                saleworkController: controller);

            Assert.False(controller.WebViewCreated);
            Assert.True(mainVm.SaleworkVm.IsDemoMode);

            var navs = mainVm.NavigationItems.ToList();
            var sheetIdx = navs.FindIndex(n => n.Key == "google_sheet");
            var swIdx = navs.FindIndex(n => n.Key == "salework");
            Assert.True(sheetIdx >= 0);
            Assert.Equal(sheetIdx + 1, swIdx);
            Assert.Equal("Salework", navs[swIdx].Title);
            Assert.Equal("NẠP ĐƠN TỰ ĐỘNG", navs[swIdx].Section);
        });
    }

    [Fact]
    public void CaptureT016Screenshots_AllFourStates()
    {
        var screensDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tasks", "reports", "T016-screens"));
        Directory.CreateDirectory(screensDir);

        WpfTestHost.Run(() =>
        {
            var fakeData = new FakeDataService(enableMutableDemoStore: true);
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var mode = 0;

            using var controller = new FakeSaleworkController(
                runHandler: async ct =>
                {
                    if (mode == 1)
                    {
                        await gate.Task.WaitAsync(ct);
                        return new SaleworkRunResult
                        {
                            Ran = true,
                            Ok = true,
                            RowCount = 14,
                            FileName = "salework_cho_hoan.xlsx",
                            Import = new SaleworkImportResult
                            {
                                Ok = true,
                                Import = new TikTokImportResult { Ok = true, Added = 6, Updated = 3 },
                                SupersededMerged = 4,
                                SupersedeAmbiguous = 2
                            }
                        };
                    }

                    return new SaleworkRunResult
                    {
                        Ran = true,
                        Ok = false,
                        NeedsRelogin = true,
                        Step = "mở trang",
                        Error = "Salework yêu cầu đăng nhập lại."
                    };
                },
                clock: () => FixedNow);

            using var mainVm = new MainViewModel(
                fakeData,
                fakeData,
                fakeData,
                fakeData,
                new FakeLegacyImporter(fakeData.GetCurrentTime),
                new StubFolderPicker(),
                new StubFileSave(),
                isDemoMode: true,
                archiveSweeper: new NoOpArchiveSweeper(),
                photoProvider: new NoOpPhotoProvider(),
                dispatchToUi: a => WpfTestHost.Dispatcher.Invoke(a),
                saleworkController: controller);

            mainVm.NavigateByKey("salework");
            var win = new MainWindow
            {
                DataContext = mainVm,
                Width = 1280,
                Height = 800,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000,
                Top = -20000,
                ShowActivated = false,
                ShowInTaskbar = false
            };
            win.Show();
            WpfTestHost.PumpDispatcher(win);

            // 1. Chưa cài đặt
            WpfTestHost.SaveWindowPng(win, Path.Combine(screensDir, "01-not-configured.png"));

            // 2. Đã lưu + đang lấy
            controller.SetFakeLoginUrl(FakeSaleworkUrl);
            mainVm.SaleworkVm.SaveCurrentPageCommand.Execute(null);
            mode = 1;
            var runTask = mainVm.SaleworkVm.RunNowCommand.ExecuteAsync(null);
            WpfTestHost.PumpDispatcher(win);
            WpfTestHost.SaveWindowPng(win, Path.Combine(screensDir, "02-saved-and-running.png"));

            // 3. Thành công có gộp + cảnh báo nhập nhằng
            gate.SetResult(true);
            WpfTestHost.RunTask(() => runTask);
            WpfTestHost.PumpDispatcher(win);
            WpfTestHost.SaveWindowPng(win, Path.Combine(screensDir, "03-success-merged.png"));

            // 4. Lỗi cần đăng nhập lại
            mode = 2;
            WpfTestHost.RunTask(() => mainVm.SaleworkVm.RunNowCommand.ExecuteAsync(null));
            WpfTestHost.PumpDispatcher(win);
            WpfTestHost.SaveWindowPng(win, Path.Combine(screensDir, "04-error-relogin.png"));

            win.Close();
        });

        Assert.True(File.Exists(Path.Combine(screensDir, "01-not-configured.png")));
        Assert.True(File.Exists(Path.Combine(screensDir, "02-saved-and-running.png")));
        Assert.True(File.Exists(Path.Combine(screensDir, "03-success-merged.png")));
        Assert.True(File.Exists(Path.Combine(screensDir, "04-error-relogin.png")));
    }

    private sealed class StubConfirm(bool result) : IConfirmationService
    {
        public bool Result { get; set; } = result;
        public int CallCount { get; private set; }
        public bool Confirm(string title, string message)
        {
            CallCount++;
            return Result;
        }
    }

    private sealed class StubFolderPicker : IFolderPickerService
    {
        public string? PickFolder(string description, string? initialPath = null) => null;
    }

    private sealed class StubFileSave : IFileSaveService
    {
        public string? PickSavePath(string defaultFileName, string filter, string title) => null;
        public Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

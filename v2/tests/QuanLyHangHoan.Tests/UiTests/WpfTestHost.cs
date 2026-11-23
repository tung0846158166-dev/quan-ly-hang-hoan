using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QuanLyHangHoan.App.Services;

namespace QuanLyHangHoan.Tests.UiTests;

/// <summary>
/// Fixture/host dùng chung cho mọi test WPF: một luồng STA duy nhất + Dispatcher + một Application duy nhất
/// đã nạp đầy đủ tài nguyên App.xaml (LightTheme + AppStyles). Đảm bảo test không phụ thuộc thứ tự chạy.
/// </summary>
public static class WpfTestHost
{
    private static readonly object InitLock = new();
    private static Dispatcher? _dispatcher;

    private static Dispatcher EnsureStarted()
    {
        if (_dispatcher is not null) return _dispatcher;

        lock (InitLock)
        {
            if (_dispatcher is not null) return _dispatcher;

            using var ready = new ManualResetEventSlim(false);
            Exception? initEx = null;
            Dispatcher? createdDispatcher = null;

            var thread = new Thread(() =>
            {
                try
                {
                    var d = Dispatcher.CurrentDispatcher;
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(d));

                    if (Application.Current is null)
                    {
                        var app = new QuanLyHangHoan.App.App
                        {
                            ShutdownMode = ShutdownMode.OnExplicitShutdown
                        };
                        app.InitializeComponent();
                    }
                    else
                    {
                        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    }

                    ThemeService.ApplyTheme(ThemeService.LightTheme);
                    createdDispatcher = d;
                    ready.Set();
                    Dispatcher.Run();
                }
                catch (Exception ex)
                {
                    initEx = ex;
                    ready.Set();
                }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            ready.Wait();
            if (initEx is not null)
            {
                throw new InvalidOperationException("Không thể khởi tạo WpfTestHost.", initEx);
            }

            _dispatcher = createdDispatcher!;
            return _dispatcher;
        }
    }

    public static Dispatcher Dispatcher => EnsureStarted();

    public static void Run(Action action)
    {
        var dispatcher = EnsureStarted();
        if (dispatcher.CheckAccess())
        {
            ExecuteWithCleanup(action);
            return;
        }

        Exception? captured = null;
        dispatcher.Invoke(() =>
        {
            try
            {
                ExecuteWithCleanup(action);
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        if (captured is not null)
        {
            ExceptionDispatchInfo.Capture(captured).Throw();
        }
    }

    private static void ExecuteWithCleanup(Action action)
    {
        ResetState();
        try
        {
            action();
        }
        finally
        {
            ResetState();
        }
    }

    private static void ResetState()
    {
        var app = Application.Current;
        if (app is not null)
        {
            foreach (var win in app.Windows.Cast<Window>().ToList())
            {
                try { win.Close(); } catch { /* bỏ qua khi dọn dẹp */ }
            }
        }

        ThemeService.ApplyTheme(ThemeService.LightTheme);
        PumpDispatcher();
    }

    public static void PumpDispatcher(Window? window = null)
    {
        window?.UpdateLayout();
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new DispatcherOperationCallback(_ =>
            {
                frame.Continue = false;
                return null;
            }),
            null);
        Dispatcher.PushFrame(frame);
    }

    public static void RunTask(Func<Task> action)
    {
        var frame = new DispatcherFrame();
        Exception? captured = null;
        _ = RunAsync();
        Dispatcher.PushFrame(frame);
        if (captured is not null)
        {
            ExceptionDispatchInfo.Capture(captured).Throw();
        }

        async Task RunAsync()
        {
            try { await action(); }
            catch (Exception ex) { captured = ex; }
            finally { _ = Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false); }
        }
    }

    public static void SaveWindowPng(Window window, string path)
    {
        window.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}

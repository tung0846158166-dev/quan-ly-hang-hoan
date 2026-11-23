using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Cửa sổ WebView2 riêng cho Salework (hồ sơ độc lập, giữ mọi liên kết trong một cửa sổ).
/// </summary>
internal sealed class SaleworkBrowserWindow : Window
{
    private readonly WebView2 _webView;
    private bool _initialized;

    public SaleworkBrowserWindow(string title, int width, int height, bool visibleToUser)
    {
        Title = title;
        Width = width;
        Height = height;
        _webView = new WebView2();
        Content = new Grid { Children = { _webView } };

        if (visibleToUser)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000;
            Top = -32000;
            ShowInTaskbar = false;
            ShowActivated = false;
            WindowStyle = WindowStyle.None;
            Opacity = 0;
        }
    }

    public CoreWebView2? Core => _webView.CoreWebView2;

    public string CurrentUrl =>
        _webView.CoreWebView2?.Source
        ?? _webView.Source?.ToString()
        ?? string.Empty;

    public async Task InitializeAsync(string userDataFolder)
    {
        if (_initialized) return;
        Directory.CreateDirectory(userDataFolder);
        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await _webView.EnsureCoreWebView2Async(env);
        if (_webView.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        }
        _initialized = true;
    }

    public void Navigate(string url)
    {
        if (_webView.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.Navigate(url);
        }
        else if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            _webView.Source = uri;
        }
    }

    public async Task<JsonElement> ExecutePageScriptAsync(string action, object? arg = null)
    {
        if (_webView.CoreWebView2 is null)
        {
            throw new InvalidOperationException("WebView2 chưa sẵn sàng.");
        }

        var script = SaleworkPageScript.BuildScript(action, arg);
        var raw = await _webView.CoreWebView2.ExecuteScriptAsync(script);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "null" : raw);
        return doc.RootElement.Clone();
    }

    public async Task DispatchMouseEventAsync(string type, int x, int y, string button = "none", int clickCount = 0)
    {
        if (_webView.CoreWebView2 is null) return;
        var payload = new Dictionary<string, object>
        {
            ["type"] = type,
            ["x"] = x,
            ["y"] = y
        };
        if (!string.Equals(button, "none", StringComparison.Ordinal))
        {
            payload["button"] = button;
            payload["clickCount"] = clickCount;
        }
        await _webView.CoreWebView2.CallDevToolsProtocolMethodAsync(
            "Input.dispatchMouseEvent",
            JsonSerializer.Serialize(payload));
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (SaleworkPageScript.ValidateOpenUrl(e.Uri, out _) && _webView.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.Navigate(e.Uri);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_webView.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
        }
        try { _webView.Dispose(); } catch { /* bỏ qua khi đóng */ }
        base.OnClosed(e);
    }
}

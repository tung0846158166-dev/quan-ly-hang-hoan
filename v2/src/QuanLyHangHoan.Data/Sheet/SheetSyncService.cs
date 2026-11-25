using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Data.Import;

namespace QuanLyHangHoan.Data.Sheet;

/// <summary>Che địa chỉ Apps Script + token ("chìa khóa" đọc sheet) khi lưu ra đĩa.</summary>
public interface ISecretProtector
{
    string Protect(string plain);
    string? Unprotect(string protectedText);
}

/// <summary>DPAPI của Windows (chỉ đúng người dùng Windows này giải mã được).</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class DpapiProtector : ISecretProtector
{
    public string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    public string? Unprotect(string protectedText)
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedText), null, DataProtectionScope.CurrentUser)); }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return null; }
    }
}

public sealed class SheetSyncConfig
{
    public bool Enabled { get; set; }
    /// <summary>JSON {url, token} đã mã hóa (không bao giờ ghi rõ ra đĩa, log hay báo cáo).</summary>
    public string Secret { get; set; } = "";
    /// <summary>Tên các tab, ngăn cách bằng ; hoặc xuống dòng (trống = tab đầu tiên).</summary>
    public string Tabs { get; set; } = "";
    /// <summary>Số ngày gần nhất ("30") hoặc từ ngày cố định ("01/11/2026"); trống = lấy hết.</summary>
    public string MaxDays { get; set; } = "";
    /// <summary>Các "Loại" bỏ qua ở tab Huỷ, ngăn cách bằng ;. null = mặc định ("Huỷ trước chuẩn bị").</summary>
    public string? SkipTypes { get; set; }
    public bool OnlyReturns { get; set; } = true;
    public string LastHash { get; set; } = "";
    public Dictionary<string, string> Sigs { get; set; } = new();
    public long LastAtTicks { get; set; }
    public bool LastOk { get; set; }
    public string LastNote { get; set; } = "";
}

public sealed class SheetRunResult
{
    public bool Ran { get; init; }
    public bool Ok { get; init; }
    public bool Unchanged { get; init; }
    public string Note { get; init; } = "";
    public TikTokImportResult? Import { get; init; }
}

/// <summary>
/// Tự cập nhật đơn Shopee từ Google Sheet (app cũ v1.9.23+): tải CSV từ Apps Script của chủ shop, ghép 3 tab, nạp như file Shopee.
/// An toàn: chỉ https tới host Google, không ghi URL/token ra log/ghi chú, lỗi mạng/Google đổi trang thì báo rõ và KHÔNG đổi dữ liệu.
/// Lần tự động chỉ nạp đơn MỚI hoặc có dữ liệu ĐỔI (so chữ ký theo mã đơn); bấm tay thì nạp toàn bộ.
/// </summary>
public sealed class SheetSyncService : IDisposable
{
    private readonly string _dbPath, _statePath;
    private readonly ISecretProtector _protector;
    private readonly Func<DateTime> _clock;
    private readonly Func<string, CancellationToken, Task<string>> _fetch;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;

    public SheetSyncConfig Config { get; private set; } = new();
    public event Action<SheetRunResult>? Completed;

    public SheetSyncService(string dbPath, string statePath, ISecretProtector protector, Func<DateTime> clock,
        Func<string, CancellationToken, Task<string>>? fetch = null)
    {
        _dbPath = dbPath; _statePath = statePath; _protector = protector; _clock = clock;
        _fetch = fetch ?? DefaultFetch;
        Load();
    }

    // ------------------------------------------------------------ cấu hình
    private void Load()
    {
        try { if (File.Exists(_statePath)) Config = JsonSerializer.Deserialize<SheetSyncConfig>(File.ReadAllText(_statePath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException) { Config = new(); }
    }

    public void SaveConfig()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        var tmp = _statePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Config));
        File.Move(tmp, _statePath, true);
    }

    /// <summary>Host được phép: chỉ https tới Google (Apps Script / bảng tính).</summary>
    public static bool IsAllowedUrl(string? url, out string error)
    {
        error = "";
        if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var u)) { error = "Địa chỉ không hợp lệ."; return false; }
        if (u.Scheme != Uri.UriSchemeHttps) { error = "Chỉ chấp nhận địa chỉ https."; return false; }
        var h = u.Host.ToLowerInvariant();
        if (h is "script.google.com" or "docs.google.com" || h.EndsWith(".googleusercontent.com", StringComparison.Ordinal)) return true;
        error = "Chỉ chấp nhận địa chỉ Google (script.google.com).";
        return false;
    }

    /// <summary>Lưu địa chỉ Apps Script (mã hóa). Sinh token ngẫu nhiên nếu chưa có. Trả về false + lý do nếu địa chỉ không an toàn.</summary>
    public bool SetUrl(string url, out string error)
    {
        if (!IsAllowedUrl(url, out error)) return false;
        var (_, token) = ReadSecret();
        token ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        Config.Secret = _protector.Protect(JsonSerializer.Serialize(new Dictionary<string, string> { ["url"] = url.Trim(), ["token"] = token }));
        Config.LastHash = ""; Config.Sigs = new();
        SaveConfig();
        return true;
    }

    /// <summary>Dùng token có sẵn (vd token của app cũ) để chạy chung một Apps Script. Cần đã lưu địa chỉ.</summary>
    public bool SetToken(string token, out string error)
    {
        token = (token ?? "").Trim();
        if (token.Length < 8 || token.Length > 64 || !token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        { error = "Token gồm 8-64 ký tự chữ, số, gạch ngang hoặc gạch dưới (không có dấu cách)."; return false; }
        var (url, _) = ReadSecret();
        if (url is null) { error = "Hãy lưu địa chỉ Apps Script trước."; return false; }
        Config.Secret = _protector.Protect(JsonSerializer.Serialize(new Dictionary<string, string> { ["url"] = url, ["token"] = token }));
        Config.LastHash = ""; Config.Sigs = new();
        SaveConfig();
        error = "";
        return true;
    }

    public bool HasUrl => ReadSecret().Url is not null;

    /// <summary>Token để dán vào Apps Script của chủ shop (không phải địa chỉ).</summary>
    public string? Token => ReadSecret().Token;

    private (string? Url, string? Token) ReadSecret()
    {
        if (Config.Secret.Length == 0) return (null, null);
        var plain = _protector.Unprotect(Config.Secret);
        if (plain is null) return (null, null);
        try
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(plain);
            return (d?.GetValueOrDefault("url"), d?.GetValueOrDefault("token"));
        }
        catch (JsonException) { return (null, null); }
    }

    // ------------------------------------------------------------ tải
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 }) { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>Lời báo lỗi dễ hiểu kèm cách sửa (mã 401/403 gần như luôn do Apps Script chưa mở quyền "Bất kỳ ai").</summary>
    public static string DescribeHttpError(int code) => code switch
    {
        401 or 403 => $"Google từ chối truy cập (mã {code}). Trong Apps Script chọn Triển khai > Quản lý bản triển khai > sửa: \"Người thực thi\" = Tôi, \"Ai có quyền truy cập\" = Bất kỳ ai; lưu phiên bản mới rồi dán lại địa chỉ Web App (đuôi /exec).",
        404 => "Không tìm thấy Web App (mã 404). Kiểm tra lại địa chỉ có đuôi /exec và bản triển khai chưa bị xóa.",
        >= 500 => $"Máy chủ Google đang lỗi (mã {code}), sẽ tự thử lại ở lượt sau.",
        _ => "Máy chủ Google trả mã " + code,
    };

    private static async Task<string> DefaultFetch(string url, CancellationToken ct)
    {
        using var res = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException(DescribeHttpError((int)res.StatusCode));
        if (res.Content.Headers.ContentLength is > 25_000_000) throw new InvalidOperationException("Dữ liệu quá lớn.");
        return await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static string BuildUrl(string baseUrl, string token, DateTime now, string tab)
    {
        var u = baseUrl.Trim() + (baseUrl.Contains('?') ? "&" : "?") + "token=" + Uri.EscapeDataString(token) + "&t=" + new DateTimeOffset(now).ToUnixTimeMilliseconds();
        return tab.Length > 0 ? u + "&tab=" + Uri.EscapeDataString(tab) : u;
    }

    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    private static string Sig(SheetParcel p) =>
        Hash(string.Join("\u0001", new[] { p.OrderId, p.ClaimId, p.Track, p.Type, p.TypeLabel, p.ParcelStatus, p.RequestStatus, p.Reason, p.BuyerNote, p.ReturnDate, p.DeliveredAt, p.Deadline, p.Refund }
            .Concat(p.Items.Select(i => i.Name + "|" + i.Variation + "|" + i.Qty))));

    // ------------------------------------------------------------ chạy một lượt
    public SheetRunResult RunOnce(bool manual = false) => RunOnceAsync(manual, CancellationToken.None).GetAwaiter().GetResult();

    public async Task<SheetRunResult> RunOnceAsync(bool manual, CancellationToken ct)
    {
        if (!_lock.Wait(0)) return new SheetRunResult { Ran = false, Note = "Đang nạp dữ liệu khác, thử lại sau ít giây." };
        SheetRunResult result;
        try
        {
            result = await RunCoreAsync(manual, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new SheetRunResult { Ran = true, Ok = false, Note = Scrub(ex.Message) };
        }
        finally { _lock.Release(); }

        Config.LastAtTicks = _clock().Ticks; Config.LastOk = result.Ok; Config.LastNote = result.Note;
        try { SaveConfig(); } catch (IOException) { }
        Completed?.Invoke(result);
        return result;
    }

    /// <summary>Không bao giờ để địa chỉ/token lọt vào ghi chú hiển thị.</summary>
    private string Scrub(string msg)
    {
        var (url, token) = ReadSecret();
        foreach (var secret in new[] { url, token }) if (!string.IsNullOrEmpty(secret)) msg = msg.Replace(secret, "***");
        return msg.Length > 300 ? msg[..300] : msg;
    }

    private async Task<SheetRunResult> RunCoreAsync(bool manual, CancellationToken ct)
    {
        var (url, token) = ReadSecret();
        if (url is null || token is null) return new SheetRunResult { Ran = false, Note = "Chưa dán địa chỉ Apps Script của Google Sheet." };
        if (!IsAllowedUrl(url, out var urlError)) return new SheetRunResult { Ran = true, Ok = false, Note = urlError };
        var now = _clock();

        var tabNames = Config.Tabs.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        var texts = new List<(string Tab, string Text)>();
        foreach (var tab in tabNames.Count > 0 ? tabNames : new List<string> { "" })
        {
            var text = await _fetch(BuildUrl(url, token, now, tab), ct).ConfigureAwait(false);
            var head = text.TrimStart();
            if (head.StartsWith("{\"error", StringComparison.Ordinal))
                throw new InvalidOperationException((tab.Length > 0 ? $"Tab \"{tab}\": " : "") + ExtractError(head));
            if (head.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) || head.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Google trả về trang web thay vì dữ liệu — kiểm tra lại bước \"Triển khai\" (Ai có quyền truy cập: Bất kỳ ai).");
            texts.Add((tab, text));
        }

        var hash = Hash(string.Join("\u0002", texts.Select(t => t.Tab + "\u0001" + t.Text)));
        if (!manual && hash == Config.LastHash) return new SheetRunResult { Ran = true, Ok = true, Unchanged = true, Note = "Sheet không thay đổi" };

        var tabs = texts.Select(t => { try { return SpreadsheetReader.DropPersonalColumns(SpreadsheetReader.ReadCsv(t.Text)); } catch (Exception) { return new List<Dictionary<string, string>>(); } }).ToList();
        var build = SheetPipeline.Build(tabs, Config.SkipTypes, Config.MaxDays, now);
        if (build.Rows.Count == 0)
            throw new InvalidOperationException("Không tab nào có dòng dữ liệu đơn hàng (cần dòng tiêu đề có Mã đơn hàng / Mã vận đơn).");

        var newSigs = build.Parcels.ToDictionary(p => p.OrderId, p => Sig(p), StringComparer.Ordinal);
        var parcels = build.Parcels;
        var unchangedOrders = 0;
        if (!manual && Config.Sigs.Count > 0)
        {
            var before = parcels.Count;
            parcels = parcels.Where(p => !Config.Sigs.TryGetValue(p.OrderId, out var old) || old != newSigs[p.OrderId]).ToList();
            unchangedOrders = before - parcels.Count;
        }
        var rows = SheetPipeline.ToRows(parcels).Concat(build.OtherRows).ToList();
        if (rows.Count == 0)
        {
            Config.LastHash = hash;
            return new SheetRunResult { Ran = true, Ok = true, Unchanged = true, Note = $"{build.Parcels.Count} đơn ghép từ {tabs.Count} tab, không đơn nào đổi dữ liệu" };
        }

        var import = TikTokImportService.ApplyGroups(_dbPath, new[] { (IReadOnlyList<Dictionary<string, string>>)rows },
            new TikTokMapOptions { OnlyReturns = Config.OnlyReturns, Today = now }, ImportProductMode.Supplement, now, ImportSource.Shopee);
        if (!import.Ok) return new SheetRunResult { Ran = true, Ok = false, Note = import.Message, Import = import };

        Config.LastHash = hash;
        Config.Sigs = newSigs;
        var note = $"{build.Parcels.Count} đơn ghép từ {tabs.Count} tab" + (unchangedOrders > 0 ? $" ({parcels.Count} đơn mới/đổi được nạp, {unchangedOrders} đơn không đổi)" : "")
            + (build.SkippedCancelTypes > 0 ? $", bỏ {build.SkippedCancelTypes} đơn huỷ thuộc loại bỏ qua" : "")
            + (build.TooOld > 0 ? $", bỏ {build.TooOld} đơn quá cũ" : "")
            + (build.OldRequestsDropped > 0 ? $", bỏ {build.OldRequestsDropped} dòng yêu cầu cũ / đã hủy của cùng đơn" : "")
            + $". Thêm {import.Added}, cập nhật {import.Updated}" + (import.Ambiguous.Count > 0 ? $", {import.Ambiguous.Count} dòng nhập nhằng chưa nạp" : "");
        return new SheetRunResult { Ran = true, Ok = true, Note = note, Import = import };
    }

    private static string ExtractError(string head)
    {
        try { using var d = JsonDocument.Parse(head); if (d.RootElement.TryGetProperty("error", out var e)) return e.ToString(); }
        catch (JsonException) { }
        return head.Length > 200 ? head[..200] : head;
    }

    // ------------------------------------------------------------ định kỳ
    /// <summary>Chu kỳ lấy Sheet. Nội dung không đổi thì bỏ qua (so mã băm), nên chu kỳ ngắn rất nhẹ.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(3);

    public void Start(TimeSpan? firstDelay = null, TimeSpan? interval = null)
    {
        Stop();
        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(interval ?? DefaultInterval);
        var ct = _cts.Token; var timer = _timer;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(firstDelay ?? TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                do { if (Config.Enabled && HasUrl) await RunOnceAsync(false, ct).ConfigureAwait(false); }
                while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
            }
            catch (OperationCanceledException) { }
        }, ct);
    }

    public void Stop() { _cts?.Cancel(); _timer?.Dispose(); _timer = null; _cts = null; }
    public void Dispose() => Stop();
}

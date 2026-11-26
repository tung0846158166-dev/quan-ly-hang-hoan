using System.Security.Cryptography;
using System.Text;

namespace QuanLyHangHoan.Data.Ai;

/// <summary>Cổng AI của cloud: địa chỉ API (".../v1"), token thiết bị, nhà cung cấp nào đã có khóa trên cloud.</summary>
public sealed record CloudAiAccess(string ApiBase, string Token, bool Groq, bool Gemini, bool Nvidia = false, bool Cloudflare = false,
    IReadOnlyList<string>? Order = null, IReadOnlyDictionary<string, string>? Models = null, int TimeoutSec = 0);

/// <summary>
/// Lưu khóa API và danh sách mô hình trên máy, cho từng nhà cung cấp (groq, gemini). Khóa được mã hóa bằng DPAPI
/// (chỉ tài khoản Windows này giải mã được), không bao giờ ghi dạng chữ thường, không nằm trong settings.json hay bản sao lưu dạng chữ.
/// Tên tệp của Groq giữ như bản đầu (ai-key.dat, ai-model.txt) để không mất cấu hình đã nhập.
/// </summary>
public sealed class AiSettingsStore
{
    public const string Groq = "groq";
    public const string Gemini = "gemini";
    public const string Nvidia = "nvidia";
    public const string Cloudflare = "cloudflare";

    private readonly string _dir;

    private Func<CloudAiAccess?>? _cloud;

    public AiSettingsStore(string directory) => _dir = directory;

    /// <summary>Chế độ cloud: AI đi qua cổng AI của cloud bằng token thiết bị (khóa chỉ nằm trên cloud). Trả null = chưa nối / chưa biết: dùng khóa trên máy.</summary>
    public void UseCloud(Func<CloudAiAccess?> cloud) => _cloud = cloud;

    /// <summary>Đang chạy chế độ cloud: AI CHỈ đi qua cloud, không bao giờ dùng/nhập khóa trên máy (kể cả lúc chưa hỏi được cloud).</summary>
    public bool CloudMode => _cloud is not null;

    public CloudAiAccess? Cloud => _cloud?.Invoke();
    public bool UsesCloud => Cloud is not null;

    private string KeyPath(string provider) => Path.Combine(_dir, provider == Groq ? "ai-key.dat" : $"ai-key-{provider}.dat");
    private string ModelPath(string provider) => Path.Combine(_dir, provider == Groq ? "ai-model.txt" : $"ai-model-{provider}.txt");
    private static string DefaultModels(string provider) => provider == Gemini ? AiChatService.DefaultGeminiModels
        : provider == Nvidia ? AiChatService.DefaultNvidiaModels : provider == Cloudflare ? AiChatService.DefaultCloudflareModels : AiChatService.DefaultGroqModels;

    public string? LoadKey(string provider = Groq)
    {
        try
        {
            var p = KeyPath(provider);
            if (!File.Exists(p)) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(p), Entropy, DataProtectionScope.CurrentUser);
            var s = Encoding.UTF8.GetString(plain);
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        catch { return null; }
    }

    public bool HasKey => CloudMode
        ? Cloud is { } c && (c.Groq || c.Gemini || c.Nvidia || c.Cloudflare)
        : LoadKey(Groq) is not null || LoadKey(Gemini) is not null || LoadKey(Nvidia) is not null;
    public bool HasKeyFor(string provider) => LoadKey(provider) is not null;

    public void SaveKey(string key) => SaveKey(Groq, key);

    public void SaveKey(string provider, string key)
    {
        key = (key ?? "").Trim();
        if (key.Length == 0) { ClearKey(provider); return; }
        var p = KeyPath(provider);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), Entropy, DataProtectionScope.CurrentUser);
        var tmp = p + ".tmp";
        File.WriteAllBytes(tmp, enc);
        File.Move(tmp, p, overwrite: true);
    }

    public void ClearKey(string provider = Groq)
    {
        try { var p = KeyPath(provider); if (File.Exists(p)) File.Delete(p); } catch { /* bỏ qua */ }
    }

    public void ClearAllKeys() { ClearKey(Groq); ClearKey(Gemini); ClearKey(Nvidia); }

    /// <summary>Danh sách mô hình theo thứ tự ưu tiên, cách nhau bằng dấu phẩy.</summary>
    public string LoadModel(string provider = Groq)
    {
        try
        {
            var p = ModelPath(provider);
            if (File.Exists(p) && File.ReadAllText(p).Trim() is { Length: > 0 } m) return CleanModels(provider, m);
        }
        catch { /* dùng mặc định */ }
        return DefaultModels(provider);
    }

    /// <summary>Bỏ mô hình Google đã ngừng (gemini-1.x, gemini-2.x); bỏ hết thì dùng danh sách mặc định.</summary>
    public static string CleanGeminiModels(string models) => CleanModels(Gemini, models);

    /// <summary>Bỏ mô hình nhà cung cấp đã ngừng (thử thật 26/11/2026); bỏ hết thì dùng danh sách mặc định.</summary>
    public static string CleanModels(string provider, string models)
    {
        string[] retired = provider switch
        {
            Gemini => new[] { "gemini-1.", "gemini-2." },
            Nvidia => new[] { "meta/llama-3" },
            Cloudflare => new[] { "@cf/meta/llama-3.3", "@cf/qwen/qwen3-30b" },
            _ => Array.Empty<string>(),
        };
        var kept = (models ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0)
            .Where(x => !retired.Any(r => x.StartsWith(r, StringComparison.OrdinalIgnoreCase))).ToList();
        return kept.Count == 0 ? DefaultModels(provider) : string.Join(", ", kept);
    }

    public void SaveModel(string model) => SaveModel(Groq, model);

    public void SaveModel(string provider, string model)
    {
        var p = ModelPath(provider);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, string.IsNullOrWhiteSpace(model) ? DefaultModels(provider) : model.Trim());
    }

    /// <summary>Chuỗi mô hình theo thứ tự: Groq trước, rồi Google. Chỉ gồm nhà cung cấp đã có khóa.</summary>
    public List<AiEndpoint> BuildEndpoints()
    {
        var list = new List<AiEndpoint>();
        if (Cloud is { } c)
        {
            var b = c.ApiBase.TrimEnd('/');
            // thứ tự + mô hình do CLOUD quản lý chung (chủ dự án 26/11/2026); cloud cũ chưa có thì giữ thứ tự cũ + mô hình trên máy
            var order = c.Order is { Count: > 0 } o ? o : new[] { Groq, Gemini, Nvidia, Cloudflare };
            foreach (var p in order)
            {
                var on = p switch { Groq => c.Groq, Gemini => c.Gemini, Nvidia => c.Nvidia, Cloudflare => c.Cloudflare, _ => false };
                if (!on) continue;
                var models = c.Models is not null && c.Models.TryGetValue(p, out var m) && !string.IsNullOrWhiteSpace(m) ? m : LoadModel(p);
                var fallback = p switch { Groq => AiChatService.DefaultModel, Gemini => AiChatService.FallbackGeminiModel, Nvidia => AiChatService.DefaultNvidiaModels, _ => AiChatService.DefaultCloudflareModels };
                list.AddRange(AiChatService.BuildEndpoints(p, b + "/ai/" + p, c.Token, models, fallback));
            }
            return list;
        }
        if (CloudMode) return list;   // chế độ cloud mà chưa hỏi được cloud: chưa có AI, KHÔNG dùng khóa trên máy
        if (LoadKey(Groq) is { } gk)
            list.AddRange(AiChatService.BuildEndpoints("groq", AiChatService.GroqBaseUrl, gk, LoadModel(Groq), AiChatService.DefaultModel));
        if (LoadKey(Gemini) is { } mk)
            list.AddRange(AiChatService.BuildEndpoints("gemini", AiChatService.GeminiBaseUrl, mk, LoadModel(Gemini), AiChatService.FallbackGeminiModel));
        if (LoadKey(Nvidia) is { } nk)
            list.AddRange(AiChatService.BuildEndpoints("nvidia", AiChatService.NvidiaBaseUrl, nk, LoadModel(Nvidia), AiChatService.DefaultNvidiaModels));
        return list;
    }

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("qlhh-v2-groq-key");
}

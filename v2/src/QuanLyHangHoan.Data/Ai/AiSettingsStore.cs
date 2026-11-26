using System.Security.Cryptography;
using System.Text;

namespace QuanLyHangHoan.Data.Ai;

/// <summary>Cổng AI của cloud: địa chỉ API (".../v1"), token thiết bị, nhà cung cấp nào đã có khóa trên cloud.</summary>
public sealed record CloudAiAccess(string ApiBase, string Token, bool Groq, bool Gemini, bool Nvidia = false, bool Cloudflare = false);

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

    public bool HasKey => Cloud is { } c ? c.Groq || c.Gemini || c.Nvidia || c.Cloudflare
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
            if (File.Exists(p) && File.ReadAllText(p).Trim() is { Length: > 0 } m) return provider == Gemini ? CleanGeminiModels(m) : m;
        }
        catch { /* dùng mặc định */ }
        return DefaultModels(provider);
    }

    /// <summary>Bỏ mô hình Google đã ngừng (gemini-1.x, gemini-2.x); bỏ hết thì dùng danh sách mặc định.</summary>
    public static string CleanGeminiModels(string models)
    {
        var kept = (models ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0)
            .Where(x => !System.Text.RegularExpressions.Regex.IsMatch(x, @"^gemini-[12]\.", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
        return kept.Count == 0 ? AiChatService.DefaultGeminiModels : string.Join(", ", kept);
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
            if (c.Groq) list.AddRange(AiChatService.BuildEndpoints("groq", b + "/ai/groq", c.Token, LoadModel(Groq), AiChatService.DefaultModel));
            if (c.Gemini) list.AddRange(AiChatService.BuildEndpoints("gemini", b + "/ai/gemini", c.Token, LoadModel(Gemini), AiChatService.FallbackGeminiModel));
            if (c.Nvidia) list.AddRange(AiChatService.BuildEndpoints("nvidia", b + "/ai/nvidia", c.Token, LoadModel(Nvidia), AiChatService.DefaultNvidiaModels));
            if (c.Cloudflare) list.AddRange(AiChatService.BuildEndpoints("cloudflare", b + "/ai/cloudflare", c.Token, LoadModel(Cloudflare), AiChatService.DefaultCloudflareModels));
            return list;
        }
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

using System.Security.Cryptography;
using System.Text;

namespace QuanLyHangHoan.Data.Ai;

/// <summary>
/// Lưu khóa API và danh sách mô hình trên máy, cho từng nhà cung cấp (groq, gemini). Khóa được mã hóa bằng DPAPI
/// (chỉ tài khoản Windows này giải mã được), không bao giờ ghi dạng chữ thường, không nằm trong settings.json hay bản sao lưu dạng chữ.
/// Tên tệp của Groq giữ như bản đầu (ai-key.dat, ai-model.txt) để không mất cấu hình đã nhập.
/// </summary>
public sealed class AiSettingsStore
{
    public const string Groq = "groq";
    public const string Gemini = "gemini";

    private readonly string _dir;

    public AiSettingsStore(string directory) => _dir = directory;

    private string KeyPath(string provider) => Path.Combine(_dir, provider == Groq ? "ai-key.dat" : $"ai-key-{provider}.dat");
    private string ModelPath(string provider) => Path.Combine(_dir, provider == Groq ? "ai-model.txt" : $"ai-model-{provider}.txt");
    private static string DefaultModels(string provider) => provider == Gemini ? AiChatService.DefaultGeminiModels : AiChatService.DefaultGroqModels;

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

    public bool HasKey => LoadKey(Groq) is not null || LoadKey(Gemini) is not null;
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

    public void ClearAllKeys() { ClearKey(Groq); ClearKey(Gemini); }

    /// <summary>Danh sách mô hình theo thứ tự ưu tiên, cách nhau bằng dấu phẩy.</summary>
    public string LoadModel(string provider = Groq)
    {
        try
        {
            var p = ModelPath(provider);
            if (File.Exists(p) && File.ReadAllText(p).Trim() is { Length: > 0 } m) return m;
        }
        catch { /* dùng mặc định */ }
        return DefaultModels(provider);
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
        if (LoadKey(Groq) is { } gk)
            list.AddRange(AiChatService.BuildEndpoints("groq", AiChatService.GroqBaseUrl, gk, LoadModel(Groq), AiChatService.DefaultModel));
        if (LoadKey(Gemini) is { } mk)
            list.AddRange(AiChatService.BuildEndpoints("gemini", AiChatService.GeminiBaseUrl, mk, LoadModel(Gemini), "gemini-2.5-flash"));
        return list;
    }

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("qlhh-v2-groq-key");
}

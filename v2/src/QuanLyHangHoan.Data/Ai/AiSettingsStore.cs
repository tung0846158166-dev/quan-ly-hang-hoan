using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuanLyHangHoan.Data.Ai;

/// <summary>
/// Lưu khóa API và tên mô hình trên máy. Khóa được mã hóa bằng DPAPI (chỉ tài khoản Windows này giải mã được),
/// không bao giờ ghi dạng chữ thường, không nằm trong settings.json hay bản sao lưu dạng chữ.
/// </summary>
public sealed class AiSettingsStore
{
    private readonly string _keyPath;
    private readonly string _modelPath;

    public AiSettingsStore(string directory)
    {
        _keyPath = Path.Combine(directory, "ai-key.dat");
        _modelPath = Path.Combine(directory, "ai-model.txt");
    }

    public string? LoadKey()
    {
        try
        {
            if (!File.Exists(_keyPath)) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(_keyPath), Entropy, DataProtectionScope.CurrentUser);
            var s = Encoding.UTF8.GetString(plain);
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        catch { return null; }
    }

    public bool HasKey => LoadKey() is not null;

    public void SaveKey(string key)
    {
        key = (key ?? "").Trim();
        if (key.Length == 0) { ClearKey(); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(_keyPath)!);
        var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), Entropy, DataProtectionScope.CurrentUser);
        var tmp = _keyPath + ".tmp";
        File.WriteAllBytes(tmp, enc);
        File.Move(tmp, _keyPath, overwrite: true);
    }

    public void ClearKey()
    {
        try { if (File.Exists(_keyPath)) File.Delete(_keyPath); } catch { /* bỏ qua */ }
    }

    public string LoadModel()
    {
        try { return File.Exists(_modelPath) ? File.ReadAllText(_modelPath).Trim() is { Length: > 0 } m ? m : AiChatService.DefaultModel : AiChatService.DefaultModel; }
        catch { return AiChatService.DefaultModel; }
    }

    public void SaveModel(string model)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_modelPath)!);
        File.WriteAllText(_modelPath, string.IsNullOrWhiteSpace(model) ? AiChatService.DefaultModel : model.Trim());
    }

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("qlhh-v2-groq-key");
}

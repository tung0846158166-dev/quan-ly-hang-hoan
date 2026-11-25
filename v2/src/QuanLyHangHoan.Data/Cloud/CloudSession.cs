using System.Text.Json;
using QuanLyHangHoan.Data.Sheet;

namespace QuanLyHangHoan.Data.Cloud;

/// <summary>Thông tin ghép thiết bị lưu trên máy. Token được mã hóa bằng <see cref="ISecretProtector"/> (DPAPI trên Windows) khi lưu.</summary>
public sealed record CloudSession(string BaseUrl, string Token, string DeviceId, string DeviceName, string Role);

public sealed class CloudSessionStore
{
    private sealed record Disk(string BaseUrl, string TokenProtected, string DeviceId, string DeviceName, string Role);

    private readonly string _path;
    private readonly ISecretProtector _protector;

    public CloudSessionStore(string path, ISecretProtector protector) { _path = path; _protector = protector; }

    public CloudSession? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var d = JsonSerializer.Deserialize<Disk>(File.ReadAllText(_path));
            if (d is null || string.IsNullOrEmpty(d.BaseUrl) || string.IsNullOrEmpty(d.TokenProtected)) return null;
            var token = _protector.Unprotect(d.TokenProtected);
            return string.IsNullOrEmpty(token) ? null : new CloudSession(d.BaseUrl, token, d.DeviceId, d.DeviceName, d.Role);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public void Save(CloudSession s)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new Disk(s.BaseUrl, _protector.Protect(s.Token), s.DeviceId, s.DeviceName, s.Role)));
        File.Move(tmp, _path, overwrite: true);   // ghi nguyên tử: không bao giờ để lại tệp nửa vời
    }

    public void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { /* bỏ qua */ }
    }
}

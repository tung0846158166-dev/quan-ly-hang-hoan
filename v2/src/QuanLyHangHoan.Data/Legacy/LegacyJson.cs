using System.Text.Json;

namespace QuanLyHangHoan.Data.Legacy;

/// <summary>Đọc tệp JSON của app cũ. Chỉ ĐỌC, không ghi.</summary>
public static class LegacyJson
{
    public enum Kind { Json, Verbatim, Missing }

    /// <summary>
    /// Tệp "phu_*" bọc dạng {"v":"<chuỗi>"}: lấy v rồi parse tiếp. Nếu v không phải JSON thì coi là
    /// giá trị thô (Verbatim). Tệp không bọc (du_lieu_hang_hoan.json) đọc thẳng.
    /// </summary>
    public static (Kind Kind, JsonDocument? Doc, string? Text) Read(string path)
    {
        if (!File.Exists(path)) return (Kind.Missing, null, null);

        var bytes = File.ReadAllBytes(path);
        var outer = JsonDocument.Parse(bytes);
        var root = outer.RootElement;

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("v", out var v)
            && v.ValueKind == JsonValueKind.String && root.EnumerateObject().Count() == 1)
        {
            var text = v.GetString() ?? "";
            outer.Dispose();
            try { return (Kind.Json, JsonDocument.Parse(text), text); }
            catch (JsonException) { return (Kind.Verbatim, null, text); }
        }

        return (Kind.Json, outer, null);
    }
}

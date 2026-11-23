using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace QuanLyHangHoan.Data.Sync;

/// <summary>Mã phòng: quy ước đặt tên topic (tương thích app cũ) và kiểm tra độ mạnh.</summary>
public static class SyncRoom
{
    public const string LegacyDefault = "SHP-2026";
    public const int MinStrongLength = 16;

    /// <summary>Topic ntfy của app cũ: agy_shopee_ + mã phòng viết thường, chỉ giữ a-z 0-9 _ -.</summary>
    public static string NtfyTopic(string? room)
    {
        var clean = Regex.Replace((string.IsNullOrEmpty(room) ? LegacyDefault : room).ToLowerInvariant(), "[^a-z0-9_-]", "");
        return "agy_shopee_" + clean;
    }

    /// <summary>Mạnh = tối thiểu 16 ký tự và không phải mã mặc định của app cũ.</summary>
    public static bool IsStrong(string? room) =>
        !string.IsNullOrEmpty(room) && room.Length >= MinStrongLength
        && !room.Equals(LegacyDefault, StringComparison.OrdinalIgnoreCase);

    /// <summary>Mã phòng ngẫu nhiên 24 ký tự (a-z 0-9), đủ mạnh và an toàn khi đưa vào topic/URL.</summary>
    public static string Generate()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        var chars = new char[24];
        for (var i = 0; i < chars.Length; i++) chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(chars);
    }
}

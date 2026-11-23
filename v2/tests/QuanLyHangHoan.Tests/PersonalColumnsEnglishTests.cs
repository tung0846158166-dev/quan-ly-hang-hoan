using QuanLyHangHoan.Data.Import;

namespace QuanLyHangHoan.Tests;

/// <summary>File TikTok tiếng Anh có cột người nhận/SĐT/địa chỉ: phải bị bỏ ngay khi đọc, các cột nghiệp vụ giữ nguyên. Dữ liệu giả.</summary>
public sealed class PersonalColumnsEnglishTests
{
    [Fact]
    public void English_tiktok_personal_columns_are_dropped_and_business_columns_kept()
    {
        var row = new Dictionary<string, string>
        {
            ["Order ID"] = "5780000000001", ["Tracking ID"] = "TTVN000000001", ["Product Name"] = "Áo giả", ["Seller Note"] = "ghi chú shop",
            ["Recipient"] = "Nguyen Van A", ["Phone #"] = "(+84)900000000", ["Province"] = "P", ["District"] = "D", ["Commune"] = "C",
            ["Detail Address"] = "1 Duong Gia", ["Additional address information"] = "x", ["Buyer Message"] = "tin nhắn", ["Buyer Username"] = "u1",
            ["Country"] = "Vietnam",
        };
        var kept = SpreadsheetReader.DropPersonalColumns(new[] { row }).Single();
        Assert.Equal(new[] { "Country", "Order ID", "Product Name", "Seller Note", "Tracking ID" }, kept.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }
}

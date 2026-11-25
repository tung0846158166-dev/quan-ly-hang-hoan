using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuanLyHangHoan.Data.Import;

namespace QuanLyHangHoan.Tests;

/// <summary>
/// Bộ ca thử CHUNG cho bước ĐỌC TAB Google Sheet: máy tính nhận CSV từ Sheets API rồi SpreadsheetReader.ReadCsv + DropPersonalColumns
/// (như SheetSyncService; lỗi "không phải bảng đơn" -> tab rỗng). Apps Script (cloud/sheet-script, qlhhTable_) đọc thẳng ô của tab và phải ra
/// CÙNG các dòng. Sinh ra cloud/golden/table-vectors.json (sinh lại: QLHH_REGEN_GOLDEN=1). Ra đời sau sự cố 26/11/2026: Apps Script lấy nhầm
/// dòng tiêu đề phụ ở dòng 1 nên mất mã đơn.
/// </summary>
public sealed class GoldenTableVectorsTests
{
    private static string Here([CallerFilePath] string here = "") => Path.GetDirectoryName(here)!;
    private static string VectorFile() => Path.GetFullPath(Path.Combine(Here(), "..", "..", "..", "cloud", "golden", "table-vectors.json"));
    private static readonly JsonSerializerOptions Opt = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static T Pick<T>(Random r, params T[] a) => a[r.Next(a.Length)];

    private static readonly string[] Headers =
    {
        "Mã", "Mã đơn", "Mã đơn hàng", "Order ID", "Loại", "Mã vận đơn", "Trạng thái về", "Trạng thái", "Hàng", "Tên sản phẩm", "SL", "Số lượng",
        "Tiền", "Lý do", "Ngày", "Cập nhật", "Tên người nhận", "Số điện thoại", "Địa chỉ", "Tên người mua", "Ghi chú", "", " Mã đơn ", "MÃ ĐƠN",
    };
    private static readonly string[] Values =
    {
        "", "", "SP1", "261120ABCDEF", "SPXVN000000101", "Đang về", "Áo thun · Đen, M × 2", "2", "199.000", "Nguyễn Văn Giả", "0900000000",
        "Có \"ngoặc\" kép", "dòng 1\ndòng 2", "a, b", "20/11/2026 09:00", " cách ", "x",
    };

    private static string[][] RandomMatrix(Random r)
    {
        var width = r.Next(1, 9);
        var rows = new List<string[]>();
        for (var t = r.Next(0, 3); t > 0; t--)                                   // dòng tiêu đề phụ / mô tả phía trên
        {
            var row = new string[width];
            for (var c = 0; c < width; c++) row[c] = c == 0 ? Pick(r, "Hàng hoàn", "Huỷ · chi tiết", "", "BÁO CÁO") : c == 2 ? Pick(r, "", "Mỗi dòng là 1 gói hàng. Cập nhật mỗi giờ.") : "";
            rows.Add(row);
        }
        var header = new string[width];
        for (var c = 0; c < width; c++) header[c] = Pick(r, Headers);
        if (r.Next(5) != 0) rows.Add(header);
        for (var n = r.Next(0, 5); n > 0; n--)
        {
            var row = new string[width];
            for (var c = 0; c < width; c++) row[c] = Pick(r, Values);
            rows.Add(row);
        }
        if (r.Next(4) == 0 && rows.Count > 0) { var h2 = new string[width]; for (var c = 0; c < width; c++) h2[c] = Pick(r, Headers); rows.Add(h2); }
        for (var n = r.Next(0, 3); n > 0; n--) rows.Add(Enumerable.Repeat("", width).ToArray());   // dòng trống cuối
        return rows.ToArray();
    }

    /// <summary>CSV như Sheets API (Values.get) + doGet cũ: bỏ ô trống cuối mỗi dòng, bỏ dòng trống cuối bảng, dấu phẩy, ngoặc kép khi cần.</summary>
    private static string ToApiCsv(string[][] m)
    {
        var rows = m.Select(r => { var n = r.Length; while (n > 0 && r[n - 1] == "") n--; return r.Take(n).ToArray(); }).ToList();
        while (rows.Count > 0 && rows[^1].Length == 0) rows.RemoveAt(rows.Count - 1);
        static string Q(string v) => v.IndexOfAny(new[] { '"', ',', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        return string.Join("\r\n", rows.Select(r => string.Join(",", r.Select(Q))));
    }

    private static JsonArray Build()
    {
        var cases = new JsonArray();
        var r = new Random(20261126);
        for (var i = 0; i < 600; i++)
        {
            var m = RandomMatrix(r);
            List<Dictionary<string, string>> rows;
            try { rows = SpreadsheetReader.DropPersonalColumns(SpreadsheetReader.ReadCsv(ToApiCsv(m))); }
            catch (Exception) { rows = new(); }                                     // SheetSyncService: lỗi đọc -> tab rỗng
            cases.Add(new JsonObject
            {
                ["matrix"] = new JsonArray(m.Select(row => (JsonNode)new JsonArray(row.Select(v => (JsonNode)v).ToArray())).ToArray()),
                ["rows"] = new JsonArray(rows.Select(d => (JsonNode)new JsonArray(d.Select(kv => (JsonNode)new JsonArray(kv.Key, kv.Value)).ToArray())).ToArray()),
            });
        }
        return cases;
    }

    [Fact]
    public void Table_vectors_file_matches_how_the_pc_reads_google_sheet_tabs()
    {
        var text = new JsonObject
        {
            ["description"] = "Bộ ca thử CHUNG: ô của một tab Google Sheet -> các dòng {cột: chữ} như máy tính đọc (ReadCsv + DropPersonalColumns). Do C# sinh (GoldenTableVectorsTests). Dữ liệu GIẢ. rows: mảng các cặp [cột, chữ] theo đúng thứ tự cột.",
            ["cases"] = Build(),
        }.ToJsonString(Opt);
        var path = VectorFile();
        if (Environment.GetEnvironmentVariable("QLHH_REGEN_GOLDEN") == "1" || !File.Exists(path)) File.WriteAllText(path, text, new UTF8Encoding(false));
        Assert.True(File.ReadAllText(path) == text, "cloud/golden/table-vectors.json lệch với C#: chạy lại với QLHH_REGEN_GOLDEN=1 nếu đổi quy tắc có chủ ý");
    }
}

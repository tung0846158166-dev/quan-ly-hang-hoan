using System.IO.Compression;
using System.Security;
using System.Text;
using QuanLyHangHoan.Core.Models;

namespace QuanLyHangHoan.Data;

/// <summary>
/// Xuất danh sách đơn ra CSV (UTF-8 có BOM để Excel đọc đúng tiếng Việt) hoặc XLSX tối giản (không cần thư viện ngoài).
/// Mỗi KIỆN một dòng (không gộp A/B/C). Không có cột thông tin khách (v2 không lưu tên/SĐT/địa chỉ).
/// Ô văn bản bắt đầu bằng = + - @ được chặn công thức (thêm dấu nháy) trong CSV; XLSX ghi chuỗi thuần nên an toàn.
/// Chỉ trả byte: việc ghi tệp do người gọi quyết định (giao diện hỏi nơi lưu); không đụng dữ liệu đơn.
/// </summary>
public static class OrderExporter
{
    public static readonly string[] Headers =
    {
        "STT", "Sàn", "Loại đơn", "Mã đơn hàng", "Mã vận đơn", "Mã vận đơn hoàn", "Mã yêu cầu hoàn", "Trạng thái kiện (sàn)", "Nhận tại kho",
        "Ngày giao", "Ngày hoàn/hủy", "Giờ hàng về shop", "Ngày nhận kho", "ĐVVC", "Sản phẩm", "Phân loại", "SL", "Giá trị",
        "Ghi chú / Lý do", "Ghi chú của tôi", "Đang theo dõi", "Lý do theo dõi", "Vị trí",
    };

    public static string LocationOf(StoredOrder s) => s.IsDeleted ? "Đã xóa" : s.IsArchived ? "Kho lưu trữ" : "Bảng chính";

    private static List<object?> Row(int stt, StoredOrder s)
    {
        var o = s.Order;
        var items = o.Items;
        return new List<object?>
        {
            stt, o.Platform, o.OrderType, o.OrderId, o.Track, o.ReturnTrack, o.ReturnOrderId, o.PlatformStatus, o.Status,
            o.ShipDate, o.ReturnDate, o.ReturnedAt, o.RecvDate, o.Carrier,
            string.Join(" | ", items.Select(i => i.Product)), string.Join(" | ", items.Select(i => string.Join(", ", new[] { i.Color, i.Size }.Where(x => !string.IsNullOrEmpty(x))))),
            items.Count == 0 ? 0 : items.Sum(i => i.Qty), o.Price,
            o.Note, o.UserNote, o.IsWatchlist ? "Có" : "", o.WatchlistReason, LocationOf(s),
        };
    }

    public static byte[] ToCsv(IEnumerable<StoredOrder> orders)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Headers.Select(Csv)));
        var n = 0;
        foreach (var s in orders) sb.AppendLine(string.Join(",", Row(++n, s).Select(v => Csv(v))));
        return new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string Csv(object? v)
    {
        if (v is null) return "";
        if (v is decimal d) return d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        if (v is int i) return i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var s = v.ToString() ?? "";
        if (s.Length > 0 && s[0] is '=' or '+' or '@' or '\t' or '\r') s = "'" + s;
        else if (s.Length > 1 && s[0] == '-' && !char.IsDigit(s[1])) s = "'" + s;
        return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    public static byte[] ToXlsx(IEnumerable<StoredOrder> orders, string sheetName = "Đơn hàng")
    {
        var rows = new List<List<object?>> { Headers.Cast<object?>().ToList() };
        var n = 0;
        foreach (var s in orders) rows.Add(Row(++n, s));

        var sheet = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (var r = 0; r < rows.Count; r++)
        {
            sheet.Append("<row r=\"").Append(r + 1).Append("\">");
            for (var c = 0; c < rows[r].Count; c++)
            {
                var v = rows[r][c];
                var cell = Col(c) + (r + 1);
                if (v is int or decimal or long)
                    sheet.Append("<c r=\"").Append(cell).Append("\"><v>").Append(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)).Append("</v></c>");
                else
                    sheet.Append("<c r=\"").Append(cell).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">").Append(SecurityElement.Escape(Clean(v?.ToString() ?? ""))).Append("</t></is></c>");
            }
            sheet.Append("</row>");
        }
        sheet.Append("</sheetData></worksheet>");

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            Add(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add(zip, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"" + SecurityElement.Escape(SafeSheet(sheetName)) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add(zip, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            Add(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
        }
        return ms.ToArray();
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    private static string Col(int i) { var s = ""; for (i++; i > 0; i = (i - 1) / 26) s = (char)('A' + (i - 1) % 26) + s; return s; }
    private static string SafeSheet(string n) { var s = new string(n.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray()); return s.Length == 0 ? "Sheet1" : (s.Length > 31 ? s[..31] : s); }
    /// <summary>Bỏ ký tự điều khiển không hợp lệ trong XML.</summary>
    private static string Clean(string s) => new(s.Where(ch => ch is '\t' or '\n' or '\r' || ch >= ' ').ToArray());
}

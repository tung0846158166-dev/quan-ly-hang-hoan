using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace QuanLyHangHoan.Data.Import;

public sealed class SpreadsheetFormatException(string message, bool notAnOrderTable = false) : Exception(message)
{
    /// <summary>true = đọc được bảng tính nhưng không có bảng đơn hàng (file không liên quan), KHÁC với file hỏng.</summary>
    public bool NotAnOrderTable { get; } = notAnOrderTable;
}

/// <summary>
/// Bộ đọc bảng tính (xlsx / csv) dùng chung cho nạp từ thư mục, Google Sheet và Salework (thay SheetJS + salework_xlsx.js của app cũ).
/// Mọi giá trị là CHUỖI (giữ nguyên mã dài 18 chữ số). Tự tìm dòng tiêu đề (bỏ qua dòng tiêu đề/hướng dẫn phía trên), chọn
/// trang tính + dòng tiêu đề có nhiều cột nhận diện nhất. Ô chữ không khai báo kiểu (file Salework) được coi là chữ.
/// Ô có định dạng ngày được đổi thành chuỗi "yyyy-MM-dd HH:mm:ss" (app cũ dùng SheetJS định dạng theo ô, khó đoán).
/// </summary>
public static class SpreadsheetReader
{
    // Tập tên cột nhận diện: giống readWorkbookRowsFromBuffer của app cũ.
    private static readonly HashSet<string> Identities = new()
    {
        "orderid", "madonhang", "iddonhang", "madon", "trackingid", "trackingnumber", "mavandon", "mavandontrahang", "mvdtrahang",
        "returnlogisticstrackingid", "returntrackingid", "returnorderid", "returnid", "masokhieunai", "makhieunai",
    };
    private static readonly HashSet<string> Products = new()
    {
        "productname", "product", "tensanpham", "tensanphamtrensan", "tenhanghoa", "itemname", "sellersku", "masku", "skuname",
        "tenphanloai", "variation", "hang", "sanpham",
    };
    private static readonly HashSet<string> Quantities = new() { "quantity", "qty", "soluong", "returnquantity", "soluonghoan", "skuquantityofreturn", "sl" };
    private static readonly HashSet<string> Statuses = new()
    {
        "orderstatus", "returnstatus", "trangthaidonhang", "trangthaitrahang", "trangthaitrahanghoantien", "trangthaihoan", "trangthaive", "trangthai",
    };

    /// <summary>Cột thông tin cá nhân khách hàng: app không cần, bỏ trước khi đi tiếp.</summary>
    public static readonly string[] PersonalHeaders = { "Tên khách hàng", "Số điện thoại", "Địa chỉ", "Tỉnh", "Huyện", "Ghi chú vận chuyển", "Người tạo đơn" };

    public static string NormalizeHeader(string? value)
    {
        var s = (value ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var c = ch switch { 'đ' => 'd', 'Đ' => 'D', _ => ch };
            c = char.ToLowerInvariant(c);
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
        }
        return sb.ToString();
    }

    public static List<Dictionary<string, string>> Read(string fileName, byte[] bytes)
    {
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        return ext switch
        {
            ".xlsx" or ".xlsm" => ReadXlsx(bytes),
            ".csv" => ReadCsv(Encoding.UTF8.GetString(bytes)),
            _ => throw new SpreadsheetFormatException($"Định dạng file không hỗ trợ: \"{ext}\" (chỉ nhận .xlsx, .csv)."),
        };
    }

    // v2 BỔ SUNG so với app cũ (chỉ bỏ cột Salework): file Shopee có tên/tài khoản người mua và địa chỉ nhận/lấy hàng; app cũ lưu chúng vào rawDetails,
    // v2 không cần nên bỏ ngay khi đọc (so khớp theo tên cột đã chuẩn hóa, không phân biệt hoa thường/dấu).
    private static readonly HashSet<string> PersonalNormalized = new(PersonalHeaders.Select(NormalizeHeader))
    {
        "tennguoimua", "nguoimua", "tennguoidungnguoimua", "tendangnhapnguoimua", "buyerusername", "buyername", "diachinhanhang",
        "diachilayhanghoantra", "tennguoinhan", "deliveryaddress", "returnaddress", "recipientname", "buyerphone",
        // Cột cá nhân của file TikTok tiếng Anh (đã kiểm tra: importer không đọc các cột này và không lưu vào DB; bỏ ngay khi đọc cho chắc).
        "recipient", "phone", "province", "district", "commune", "detailaddress", "additionaladdressinformation", "buyermessage",
    };

    public static List<Dictionary<string, string>> DropPersonalColumns(IEnumerable<Dictionary<string, string>> rows) =>
        rows.Select(r => r.Where(kv => !PersonalNormalized.Contains(NormalizeHeader(kv.Key))).ToDictionary(kv => kv.Key, kv => kv.Value)).ToList();

    // ================= xlsx =================
    public static List<Dictionary<string, string>> ReadXlsx(byte[] bytes)
    {
        List<string[][]> sheets;
        try { sheets = LoadXlsxSheets(bytes); }
        catch (InvalidDataException) { throw new SpreadsheetFormatException("Không phải file .xlsx hợp lệ."); }
        catch (System.Xml.XmlException) { throw new SpreadsheetFormatException("File .xlsx bị hỏng."); }
        return BestTable(sheets);
    }

    private static List<string[][]> LoadXlsxSheets(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        XDocument Doc(string name)
        {
            var e = zip.GetEntry(name);
            if (e is null) throw new InvalidDataException(name);
            using var s = e.Open();
            return XDocument.Load(s);
        }
        static IEnumerable<XElement> D(XElement e, string local) => e.Descendants().Where(x => x.Name.LocalName == local);

        var sheetKeys = zip.Entries
            .Select(e => (e.FullName, m: Regex.Match(e.FullName, @"^xl/worksheets/sheet(\d+)\.xml$")))
            .Where(x => x.m.Success).OrderBy(x => int.Parse(x.m.Groups[1].Value)).Select(x => x.FullName).ToList();
        if (sheetKeys.Count == 0) throw new InvalidDataException("sheet");

        var shared = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") is not null)
            foreach (var si in D(Doc("xl/sharedStrings.xml").Root!, "si"))
                shared.Add(string.Concat(D(si, "t").Select(t => t.Value)));

        var dateStyles = LoadDateStyles(zip);

        var result = new List<string[][]>();
        foreach (var key in sheetKeys)
        {
            var matrix = new List<string[]>();
            foreach (var row in D(Doc(key).Root!, "row"))
            {
                var cells = new List<string>();
                foreach (var c in row.Elements().Where(x => x.Name.LocalName == "c"))
                {
                    var rf = (string?)c.Attribute("r");
                    if (rf is null) continue;
                    var idx = ColIndex(rf);
                    var t = (string?)c.Attribute("t");
                    var val = "";
                    if (t == "inlineStr") val = string.Concat(D(c, "t").Select(x => x.Value));
                    else
                    {
                        var v = c.Elements().FirstOrDefault(x => x.Name.LocalName == "v")?.Value;
                        if (v is not null)
                        {
                            if (t == "s") val = int.TryParse(v, out var si) && si >= 0 && si < shared.Count ? shared[si] : "";
                            else
                            {
                                val = v;
                                var sAttr = (string?)c.Attribute("s");
                                if (t is null or "n" && sAttr is not null && int.TryParse(sAttr, out var sidx) && dateStyles.Contains(sidx)
                                    && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
                                    val = SerialToText(serial);
                            }
                        }
                    }
                    while (cells.Count <= idx) cells.Add("");
                    cells[idx] = val;
                }
                matrix.Add(cells.ToArray());
            }
            result.Add(matrix.ToArray());
        }
        return result;
    }

    private static int ColIndex(string cellRef)
    {
        var n = 0;
        foreach (var ch in cellRef) { if (ch is < 'A' or > 'Z') break; n = n * 26 + (ch - 'A' + 1); }
        return n - 1;
    }

    private static string SerialToText(double serial)
    {
        var dt = new DateTime(1899, 12, 30).AddDays(serial);
        return Math.Abs(serial - Math.Floor(serial)) < 1e-9
            ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dt.AddMilliseconds(0.5).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>Chỉ số kiểu ô (cellXfs) có định dạng ngày/giờ.</summary>
    private static HashSet<int> LoadDateStyles(ZipArchive zip)
    {
        var result = new HashSet<int>();
        var entry = zip.GetEntry("xl/styles.xml");
        if (entry is null) return result;
        using var s = entry.Open();
        var root = XDocument.Load(s).Root!;
        var custom = root.Descendants().Where(x => x.Name.LocalName == "numFmt")
            .Where(x => x.Attribute("numFmtId") is not null)
            .ToDictionary(x => (int)x.Attribute("numFmtId")!, x => (string?)x.Attribute("formatCode") ?? "");
        var xfs = root.Descendants().FirstOrDefault(x => x.Name.LocalName == "cellXfs")?.Elements().Where(x => x.Name.LocalName == "xf").ToList()
                  ?? new List<XElement>();
        for (var i = 0; i < xfs.Count; i++)
        {
            var id = (int?)xfs[i].Attribute("numFmtId") ?? 0;
            var isDate = id is (>= 14 and <= 22) or (>= 27 and <= 36) or (>= 45 and <= 47) or (>= 50 and <= 58);
            if (!isDate && custom.TryGetValue(id, out var code))
            {
                var stripped = Regex.Replace(code, "\"[^\"]*\"|\\\\.|\\[[^\\]]*\\]", "");
                isDate = Regex.IsMatch(stripped, "[dmyhs]", RegexOptions.IgnoreCase) && !stripped.Contains('%');
            }
            if (isDate) result.Add(i);
        }
        return result;
    }

    // ================= csv =================
    public static List<Dictionary<string, string>> ReadCsv(string text)
    {
        text = text.TrimStart('﻿');
        var firstLine = text.Split('\n', 2)[0];
        var delim = new[] { ',', ';', '\t' }.OrderByDescending(d => firstLine.Count(c => c == d)).First();
        var matrix = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else quoted = false; }
                else cell.Append(ch);
            }
            else if (ch == '"' && cell.Length == 0) quoted = true;
            else if (ch == delim) { row.Add(cell.ToString()); cell.Clear(); }
            else if (ch == '\r') { /* bỏ qua, xử lý ở \n */ }
            else if (ch == '\n') { row.Add(cell.ToString()); cell.Clear(); matrix.Add(row.ToArray()); row.Clear(); }
            else cell.Append(ch);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); matrix.Add(row.ToArray()); }
        return BestTable(new List<string[][]> { matrix.ToArray() });
    }

    // ================= tìm bảng đơn hàng =================
    private static List<Dictionary<string, string>> BestTable(List<string[][]> sheets)
    {
        List<Dictionary<string, string>>? best = null;
        var bestScore = -1;
        var hasCells = false;

        foreach (var matrix in sheets)
        {
            for (var h = 0; h < matrix.Length; h++)
            {
                var cells = matrix[h];
                if (cells.Any(c => !string.IsNullOrWhiteSpace(c))) hasCells = true;
                var keys = cells.Select(NormalizeHeader).ToList();
                var identityCount = keys.Count(Identities.Contains);
                var categories = new[] { Products, Quantities, Statuses }.Count(set => keys.Any(set.Contains));
                if (identityCount == 0 || categories == 0) continue;

                var names = HeaderNames(cells);
                var rows = new List<Dictionary<string, string>>();
                for (var r = h + 1; r < matrix.Length; r++)
                {
                    var d = new Dictionary<string, string>();
                    var any = false;
                    for (var c = 0; c < names.Count; c++)
                    {
                        var v = c < matrix[r].Length ? matrix[r][c] ?? "" : "";
                        d[names[c]] = v;
                        if (!string.IsNullOrWhiteSpace(v)) any = true;
                    }
                    if (any) rows.Add(d);
                }
                var score = categories * 10 + identityCount;
                if (rows.Count > 0 && score > bestScore) { best = rows; bestScore = score; }
            }
        }

        if (best is not null) return best;
        if (hasCells) throw new SpreadsheetFormatException("Không tìm thấy trang tính có tiêu đề dữ liệu đơn hàng và dòng dữ liệu.", notAnOrderTable: true);
        return new List<Dictionary<string, string>>();
    }

    /// <summary>Tên cột: cột trùng tên được đánh _1, _2 (giống SheetJS); cột tên trống thành __col{i}.</summary>
    private static List<string> HeaderNames(string[] cells)
    {
        var seen = new Dictionary<string, int>();
        var names = new List<string>();
        for (var i = 0; i < cells.Length; i++)
        {
            var name = (cells[i] ?? "").Trim();
            if (name.Length == 0) name = "__col" + i;
            if (!seen.ContainsKey(name)) seen[name] = 0;
            else name = name + "_" + (++seen[name]);
            names.Add(name);
        }
        return names;
    }
}

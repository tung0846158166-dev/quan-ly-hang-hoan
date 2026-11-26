using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Data.Import;

namespace QuanLyHangHoan.Tests;

/// <summary>
/// Bộ ca thử CHUNG cho nạp Salework trên ĐIỆN THOẠI (26/11/2026): bản Swift (ScannerCore) phải ra cùng kết quả với C#.
///  - salework-vectors.json: dòng đọc từ file -> bỏ cột cá nhân -> PlatformImportPrep -> TikTokMapper.Map (kiện gửi lên cloud /import/mapped).
///  - xlsx/*.xlsx + xlsx-vectors.json: đọc file Excel (SpreadsheetReader.ReadXlsx: ô chữ dùng chung/chữ trong ô, số, ngày giờ, cột trùng tên, chọn bảng).
/// Sinh lại: QLHH_REGEN_GOLDEN=1. Dữ liệu GIẢ.
/// </summary>
public sealed class GoldenSaleworkVectorsTests
{
    private static string Golden(string name, [CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "cloud", "golden", name));
    private static string LocalGolden(string name, [CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Golden", name);
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly DateTime Today = new(2026, 11, 23);
    private static bool Regen => Environment.GetEnvironmentVariable("QLHH_REGEN_GOLDEN") == "1";

    private static string Str(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => "",
    };

    private static List<Dictionary<string, string>> Rows(JsonElement rows) =>
        rows.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object).Select(r => r.EnumerateObject().ToDictionary(p => p.Name, p => Str(p.Value))).ToList();

    private static JsonArray RowsJson(IEnumerable<Dictionary<string, string>> rows) =>
        new(rows.Select(r => (JsonNode)new JsonObject(r.Select(kv => new KeyValuePair<string, JsonNode?>(kv.Key, kv.Value)))).ToArray());

    /// <summary>Mẫu Salework tiếng Việt (cùng tên cột file thật), có cột cá nhân để kiểm tra bỏ cột.</summary>
    private static IEnumerable<(string Name, List<Dictionary<string, string>> Rows)> SaleworkSamples()
    {
        Dictionary<string, string> Row(string orderId, string status, string kind, string track, string product, string returnCode = "", string qty = "1") => new()
        {
            ["Mã đơn hàng"] = orderId, ["Trạng thái"] = status, ["Ngày tạo đơn"] = "2026-11-10 08:00:00", ["Mã sản phẩm"] = "SKU-" + product,
            ["Số lượng"] = qty, ["Giá Salework"] = "100000", ["Tên sản phẩm trên sàn"] = product + " - Đen, L",
            ["Số lượng_1"] = qty, ["Giá sàn TMĐT"] = "120000", ["Tổng COD"] = orderId.Length == 0 ? "" : "120000", ["Lý do hoàn"] = orderId.Length == 0 ? "" : "Change of mind",
            ["Mã vận đơn"] = track, ["Đơn vị vận chuyển"] = orderId.Length == 0 ? "" : "J&T Express", ["Cập nhật lúc"] = orderId.Length == 0 ? "" : "2026-11-20 10:00:00",
            ["Shop"] = orderId.Length == 0 ? "" : "Shop Giả", ["Tổng giá trị"] = orderId.Length == 0 ? "" : "120000", ["Mã trả hàng"] = returnCode,
            ["Loại đơn hoàn"] = kind,
            ["Tên khách hàng"] = orderId.Length == 0 ? "" : "Nguyễn Văn Giả", ["Số điện thoại"] = orderId.Length == 0 ? "" : "0900000000",
            ["Địa chỉ"] = orderId.Length == 0 ? "" : "1 Đường Giả", ["Tỉnh"] = "Hà Nội", ["Huyện"] = "Quận Giả", ["Ghi chú vận chuyển"] = "", ["Người tạo đơn"] = "admin",
        };
        yield return ("salework:mixed", new()
        {
            Row("ORD-A", "THẤT THU", "Trả hàng hoàn tiền", "840000000001", "AoA", "RET-A"),
            Row("ORD-B", "THÀNH CÔNG", "Trả hàng hoàn tiền - Một phần", "840000000002", "AoB1", "RET-B"),
            Row("", "", "", "", "QuanB2"),
            Row("ORD-C", "THÀNH CÔNG", "", "840000000003", "AoC"),
            Row("ORD-D", "ĐANG HOÀN", "Giao không thành công", "840000000004", "AoD", "", "2"),
            Row("ORD-E", "ĐÃ HỦY", "", "", "AoE"),
            Row("ORD-F", "ĐANG HOÀN", "Trả hàng hoàn tiền", "", "AoF", "RET-F"),
        });
        yield return ("salework:no-kind-column", new()
        {
            new() { ["Mã đơn hàng"] = "ORD-G", ["Trạng thái"] = "ĐANG HOÀN", ["Mã vận đơn"] = "840000000007", ["Tên sản phẩm trên sàn"] = "Váy - Trắng, S", ["Số lượng"] = "1", ["Tổng giá trị"] = "99000", ["Số điện thoại"] = "0911111111" },
            new() { ["Mã đơn hàng"] = "ORD-H", ["Trạng thái"] = "THẤT THU", ["Mã vận đơn"] = "840000000008", ["Tên sản phẩm trên sàn"] = "Quần", ["Số lượng"] = "3", ["Tổng giá trị"] = "150000" },
        });
    }

    private static IEnumerable<(string Name, List<Dictionary<string, string>> Rows)> AllInputs()
    {
        foreach (var file in new[] { "tiktok.cases.json", "tiktok2.cases.json", "tiktok3.cases.json" })
            foreach (var c in JsonDocument.Parse(File.ReadAllText(LocalGolden(file))).RootElement.EnumerateArray())
                yield return (file.Replace(".cases.json", "") + ":" + c.GetProperty("name").GetString(), Rows(c.GetProperty("rows")));
        foreach (var c in JsonDocument.Parse(File.ReadAllText(LocalGolden("prepare.cases.json"))).RootElement.EnumerateArray())
        {
            var k = 0;
            foreach (var f in c.GetProperty("files").EnumerateArray())
                yield return ("prepare:" + c.GetProperty("name").GetString() + "#" + (k++), Rows(f.GetProperty("rows")));
        }
        foreach (var s in SaleworkSamples()) yield return s;
    }

    private static JsonArray BuildMapCases()
    {
        var cases = new JsonArray();
        foreach (var (name, rows) in AllInputs())
        {
            // giống SaleworkImportService.Apply (máy tính) + RealSaleworkController (TikTokMapOptions { Today })
            var clean = SpreadsheetReader.DropPersonalColumns(rows);
            var prep = PlatformImportPrep.Prepare(new[] { new SourceFile("salework_dang_cho_hoan", clean) });
            var opts = new TikTokMapOptions { Today = Today };
            var groups = new JsonArray();
            foreach (var g in prep.Groups)
            {
                var m = TikTokMapper.Map(g, opts);
                groups.Add(new JsonObject
                {
                    ["rows"] = RowsJson(g),
                    ["parcels"] = new JsonArray(m.Parcels.Select(p => (JsonNode)GoldenMergeVectorsTests.IncomingJson(p)).ToArray()),
                    ["skipped"] = m.Skipped, ["failedDelivery"] = m.FailedDelivery,
                });
            }
            var idKey = clean.SelectMany(r => r.Keys).FirstOrDefault(k => LegacyText.Key(k) is "madonhang" or "orderid");
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            if (idKey is not null) foreach (var r in clean) if (r.TryGetValue(idKey, out var v) && v.Trim().Length > 0) ids.Add(v.Trim().ToUpperInvariant());
            cases.Add(new JsonObject
            {
                ["name"] = name,
                ["input"] = new JsonObject { ["rows"] = RowsJson(rows), ["today"] = "2026-11-23" },
                ["expected"] = new JsonObject
                {
                    ["clean"] = RowsJson(clean), ["duplicateCount"] = prep.DuplicateCount, ["groups"] = groups,
                    ["orderIds"] = new JsonArray(ids.Select(i => (JsonNode)i).ToArray()),
                },
            });
        }
        return cases;
    }

    private static void Check(string file, string text)
    {
        var path = Golden(file);
        if (Regen || !File.Exists(path)) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
        Assert.True(File.ReadAllText(path) == text, $"cloud/golden/{file} lệch với C#: chạy lại với QLHH_REGEN_GOLDEN=1 nếu đổi quy tắc có chủ ý");
    }

    [Fact]
    public void Salework_mapping_vectors_match_csharp()
    {
        var cases = BuildMapCases();
        Assert.True(cases.Count >= 40, "cần đủ ca thử");
        Check("salework-vectors.json", new JsonObject
        {
            ["description"] = "Bộ ca thử CHUNG: nạp Salework trên điện thoại = bỏ cột cá nhân -> PlatformImportPrep -> TikTokMapper.Map (Today 23/11/2026). Do C# sinh (GoldenSaleworkVectorsTests). Dữ liệu GIẢ.",
            ["cases"] = cases,
        }.ToJsonString(Opt));
    }

    // ================================================================ xlsx
    /// <summary>Một ô: chữ dùng chung (s), chữ trong ô (inline), số (n, có thể kèm kiểu ngày).</summary>
    private sealed record Cell(string Ref, string Kind, string Value, int Style = 0);

    /// <summary>Tạo file .xlsx tối giản (đủ phần SpreadsheetReader đọc). Kiểu ô: 0 thường, 1 ngày (14), 2 ngày giờ (22), 3 định dạng tự đặt dd/mm/yyyy hh:mm, 4 phần trăm tự đặt.</summary>
    private static byte[] BuildXlsx(params List<List<Cell>>[] sheets)
    {
        var shared = new List<string>();
        int S(string v) { var i = shared.IndexOf(v); if (i < 0) { shared.Add(v); i = shared.Count - 1; } return i; }
        string X(string v) => SecurityElement.Escape(v)!;
        var sheetXml = new List<string>();
        foreach (var rows in sheets)
        {
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            var rn = 0;
            foreach (var row in rows)
            {
                rn++;
                sb.Append($"<row r=\"{rn}\">");
                foreach (var c in row)
                {
                    var st = c.Style > 0 ? $" s=\"{c.Style}\"" : "";
                    sb.Append(c.Kind switch
                    {
                        "s" => $"<c r=\"{c.Ref}{rn}\" t=\"s\"{st}><v>{S(c.Value)}</v></c>",
                        "inline" => $"<c r=\"{c.Ref}{rn}\" t=\"inlineStr\"{st}><is><t>{X(c.Value)}</t></is></c>",
                        _ => $"<c r=\"{c.Ref}{rn}\"{st}><v>{c.Value}</v></c>",
                    });
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData></worksheet>");
            sheetXml.Add(sb.ToString());
        }
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Put(string name, string text, CompressionLevel level = CompressionLevel.Optimal)
            {
                var e = zip.CreateEntry(name, level);
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(text);
            }
            Put("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
            Put("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"/>");
            for (var i = 0; i < sheetXml.Count; i++) Put($"xl/worksheets/sheet{i + 1}.xml", sheetXml[i], i == 0 ? CompressionLevel.Optimal : CompressionLevel.NoCompression);
            Put("xl/sharedStrings.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                string.Concat(shared.Select(s => $"<si><t xml:space=\"preserve\">{X(s)}</t></si>")) + "</sst>");
            Put("xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<numFmts count=\"2\"><numFmt numFmtId=\"164\" formatCode=\"dd/mm/yyyy hh:mm\"/><numFmt numFmtId=\"165\" formatCode=\"0.00%\"/></numFmts>" +
                "<cellXfs count=\"5\"><xf numFmtId=\"0\"/><xf numFmtId=\"14\"/><xf numFmtId=\"22\"/><xf numFmtId=\"164\"/><xf numFmtId=\"165\"/></cellXfs></styleSheet>");
        }
        return ms.ToArray();
    }

    private static List<Cell> R(params (string Ref, string Kind, string Value, int Style)[] cells) => cells.Select(c => new Cell(c.Ref, c.Kind, c.Value, c.Style)).ToList();

    private static IEnumerable<(string Name, byte[] Bytes)> XlsxFixtures()
    {
        // 1) kiểu file Salework: cột trùng tên "Số lượng", số, ngày giờ (kiểu 22 và tự đặt), chữ trong ô, ô bỏ trống giữa chừng, dòng trống
        yield return ("salework-basic", BuildXlsx(new List<List<Cell>>
        {
            R(("A", "s", "Mã đơn hàng", 0), ("B", "s", "Trạng thái", 0), ("C", "s", "Ngày tạo đơn", 0), ("D", "s", "Số lượng", 0), ("E", "s", "Tên sản phẩm trên sàn", 0),
              ("F", "s", "Số lượng", 0), ("G", "s", "Mã vận đơn", 0), ("H", "s", "Tổng giá trị", 0), ("I", "s", "Cập nhật lúc", 0), ("J", "s", "Số điện thoại", 0)),
            R(("A", "s", "587069307928479483", 0), ("B", "s", "THẤT THU", 0), ("C", "n", "46336.3541666667", 2), ("D", "n", "1", 0), ("E", "inline", "Áo & Quần <Đen>, L", 0),
              ("F", "n", "1", 0), ("G", "s", "840000000001", 0), ("H", "n", "120000", 0), ("I", "n", "46346.5", 3), ("J", "s", "0900000000", 0)),
            R(("E", "s", "Quần phụ", 0), ("F", "n", "2", 0)),
            new List<Cell>(),
            R(("A", "s", "ORD-2", 0), ("B", "s", "ĐANG HOÀN", 0), ("C", "n", "46336", 1), ("D", "n", "2.5", 0), ("G", "n", "840000000002", 0), ("H", "n", "0.15", 4)),
        }));
        // 2) có dòng tiêu đề phía trên + trang 1 không phải bảng đơn, bảng thật ở trang 2
        yield return ("title-rows-second-sheet", BuildXlsx(
            new List<List<Cell>> { R(("A", "s", "Ghi chú", 0)), R(("A", "s", "Không phải bảng đơn", 0), ("B", "n", "5", 0)) },
            new List<List<Cell>>
            {
                R(("A", "s", "BÁO CÁO ĐƠN HOÀN", 0)),
                R(("A", "s", "Xuất lúc", 0), ("B", "n", "46349.75", 2)),
                R(("A", "s", "Order ID", 0), ("B", "s", "Return Order ID", 0), ("C", "s", "Return Logistics Tracking ID", 0), ("D", "s", "Product Name", 0),
                  ("E", "s", "Quantity", 0), ("F", "s", "Return Status", 0), ("G", "s", "", 0), ("H", "s", "Quantity", 0)),
                R(("A", "s", "O1", 0), ("B", "s", "C1", 0), ("C", "s", "RT1", 0), ("D", "s", "Shirt", 0), ("E", "n", "1", 0), ("F", "s", "In Process", 0), ("G", "s", "x", 0), ("H", "n", "3", 0)),
                R(("A", "s", "O2", 0), ("C", "inline", "RT2", 0), ("D", "s", "Pants", 0), ("E", "n", "2", 0)),
            }));
    }

    [Fact]
    public void Xlsx_reader_vectors_match_csharp()
    {
        var list = new JsonArray();
        foreach (var (name, bytes) in XlsxFixtures())
        {
            var file = $"xlsx/{name}.xlsx";
            var path = Golden(file);
            if (Regen || !File.Exists(path)) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
            var rows = SpreadsheetReader.ReadXlsx(File.ReadAllBytes(path));
            list.Add(new JsonObject { ["file"] = file, ["rows"] = RowsJson(rows) });
        }
        Check("xlsx-vectors.json", new JsonObject
        {
            ["description"] = "Bộ ca thử CHUNG: đọc file .xlsx (SpreadsheetReader.ReadXlsx). Do C# sinh (GoldenSaleworkVectorsTests). Dữ liệu GIẢ.",
            ["cases"] = list,
        }.ToJsonString(Opt));
    }
}

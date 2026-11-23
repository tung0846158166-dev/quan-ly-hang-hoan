using System.IO;
using System.IO.Compression;
using System.Text;
using QuanLyHangHoan.Data.Import;

namespace QuanLyHangHoan.Tests;

/// <summary>Bộ đọc bảng tính (xlsx/csv) dùng chung cho nạp thư mục, Google Sheet, Salework. File dựng bằng mã, dữ liệu GIẢ.</summary>
public sealed class SpreadsheetReaderTests
{
    // ---- dựng xlsx tối giản ----
    private sealed record Sheet(string Name, string[][] Rows);

    private static byte[] Xlsx(IEnumerable<Sheet> sheets, string[]? sharedOverride = null, string? stylesXml = null, Func<string[][], string>? custom = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Add(string name, string content)
            {
                var e = zip.CreateEntry(name);
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(content);
            }
            var list = sheets.ToList();
            Add("xl/workbook.xml", "<workbook><sheets>" + string.Join("", list.Select((s, i) => $"<sheet name=\"{s.Name}\" sheetId=\"{i + 1}\"/>")) + "</sheets></workbook>");
            if (stylesXml is not null) Add("xl/styles.xml", stylesXml);
            for (var i = 0; i < list.Count; i++)
                Add($"xl/worksheets/sheet{i + 1}.xml", custom is not null ? custom(list[i].Rows) : SheetXml(list[i].Rows));
        }
        return ms.ToArray();
    }

    private static string Col(int i) { var s = ""; i++; while (i > 0) { s = (char)('A' + (i - 1) % 26) + s; i = (i - 1) / 26; } return s; }
    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>Ô chữ dùng inlineStr; ô bắt đầu bằng '#' là số thô (không khai báo kiểu), '~' là ô chữ KHÔNG khai báo kiểu.</summary>
    private static string SheetXml(string[][] rows)
    {
        var sb = new StringBuilder("<worksheet><sheetData>");
        for (var r = 0; r < rows.Length; r++)
        {
            sb.Append($"<row r=\"{r + 1}\">");
            for (var c = 0; c < rows[r].Length; c++)
            {
                var v = rows[r][c]; var rf = Col(c) + (r + 1);
                if (v.StartsWith('#')) sb.Append($"<c r=\"{rf}\"><v>{Esc(v[1..])}</v></c>");
                else if (v.StartsWith('~')) sb.Append($"<c r=\"{rf}\"><v>{Esc(v[1..])}</v></c>");
                else sb.Append($"<c r=\"{rf}\" t=\"inlineStr\"><is><t>{Esc(v)}</t></is></c>");
            }
            sb.Append("</row>");
        }
        return sb.Append("</sheetData></worksheet>").ToString();
    }

    private static readonly string[] Header = { "Order ID", "Product Name", "Quantity", "Order Status" };

    [Fact]
    public void Reads_rows_as_dictionaries_keyed_by_header()
    {
        var bytes = Xlsx(new[] { new Sheet("S", new[] { Header, new[] { "O1", "Áo giả", "2", "Done" }, new[] { "O2", "Quần giả", "1", "Done" } }) });
        var t = SpreadsheetReader.ReadXlsx(bytes);

        Assert.Equal(2, t.Count);
        Assert.Equal("O1", t[0]["Order ID"]);
        Assert.Equal("Quần giả", t[1]["Product Name"]);
    }

    [Fact]
    public void Title_and_instruction_rows_above_the_header_are_skipped()
    {
        var rows = new[]
        {
            new[] { "BÁO CÁO ĐƠN HOÀN" }, new[] { "Xuất ngày 22/11" }, new string[0], Header, new[] { "O1", "Áo giả", "1", "Done" },
        };
        var t = SpreadsheetReader.ReadXlsx(Xlsx(new[] { new Sheet("S", rows) }));

        Assert.Single(t);
        Assert.Equal("O1", t[0]["Order ID"]);
    }

    [Fact]
    public void Long_codes_stay_exact_strings_not_numbers()
    {
        var rows = new[] { Header, new[] { "#580123456789012345", "Áo giả", "1", "Done" } };
        var t = SpreadsheetReader.ReadXlsx(Xlsx(new[] { new Sheet("S", rows) }));
        Assert.Equal("580123456789012345", t[0]["Order ID"]);
    }

    [Fact]
    public void Untyped_non_numeric_cell_is_text_not_an_error()
    {
        // file Salework: ô chữ KHÔNG khai báo kiểu (không có t=...), nội dung không phải số
        var rows = new[] { Header, new[] { "~SPXVN0123456789A", "Áo giả", "#2", "Done" } };
        var t = SpreadsheetReader.ReadXlsx(Xlsx(new[] { new Sheet("S", rows) }));
        Assert.Equal("SPXVN0123456789A", t[0]["Order ID"]);
        Assert.Equal("2", t[0]["Quantity"]);
    }

    [Fact]
    public void Shared_strings_and_xml_entities_are_decoded()
    {
        const string sheet = """
            <worksheet><sheetData>
            <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c><c r="C1" t="s"><v>2</v></c><c r="D1" t="s"><v>3</v></c></row>
            <row r="2"><c r="A2" t="s"><v>4</v></c><c r="B2" t="s"><v>5</v></c><c r="C2"><v>3</v></c><c r="D2" t="s"><v>6</v></c></row>
            </sheetData></worksheet>
            """;
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Add(string n, string c) { var e = zip.CreateEntry(n); using var w = new StreamWriter(e.Open(), new UTF8Encoding(false)); w.Write(c); }
            Add("xl/workbook.xml", "<workbook><sheets><sheet name=\"S\"/></sheets></workbook>");
            Add("xl/sharedStrings.xml", "<sst><si><t>Order ID</t></si><si><t>Product Name</t></si><si><t>Quantity</t></si><si><t>Order Status</t></si><si><t>O1</t></si><si><t>Áo &amp; Quần &lt;giả&gt;</t></si><si><t>Done</t></si></sst>");
            Add("xl/worksheets/sheet1.xml", sheet);
        }
        var t = SpreadsheetReader.ReadXlsx(ms.ToArray());
        Assert.Equal("Áo & Quần <giả>", t[0]["Product Name"]);
        Assert.Equal("3", t[0]["Quantity"]);
    }

    [Fact]
    public void Duplicate_headers_get_numbered_suffixes_and_blank_rows_are_dropped()
    {
        var rows = new[]
        {
            new[] { "Order ID", "Product Name", "Quantity", "Order Status", "Product Name" },
            new[] { "O1", "A", "1", "Done", "B" },
            new[] { "", "", "", "", "" },
            new[] { "O2", "C", "1", "Done", "D" },
        };
        var t = SpreadsheetReader.ReadXlsx(Xlsx(new[] { new Sheet("S", rows) }));

        Assert.Equal(2, t.Count);
        Assert.Equal("B", t[0]["Product Name_1"]);
        Assert.Equal("D", t[1]["Product Name_1"]);
    }

    [Fact]
    public void Header_aliases_are_recognised_with_vietnamese_tones_and_picks_best_sheet()
    {
        var weak = new Sheet("Hướng dẫn", new[] { new[] { "Ghi chú" }, new[] { "đọc kỹ" } });
        var good = new Sheet("Dữ liệu", new[]
        {
            new[] { "Mã đơn hàng", "Tên sản phẩm", "Số lượng", "Trạng thái đơn hàng" },
            new[] { "O1", "Áo giả", "1", "Hoàn thành" },
        });
        var t = SpreadsheetReader.ReadXlsx(Xlsx(new[] { weak, good }));

        Assert.Single(t);
        Assert.Equal("O1", t[0]["Mã đơn hàng"]);
    }

    [Fact]
    public void File_with_cells_but_no_order_table_throws_a_clear_error_and_empty_file_returns_nothing()
    {
        var junk = Xlsx(new[] { new Sheet("S", new[] { new[] { "abc", "def" }, new[] { "1", "2" } }) });
        var ex = Assert.Throws<SpreadsheetFormatException>(() => SpreadsheetReader.ReadXlsx(junk));
        Assert.Contains("tiêu đề", ex.Message);

        Assert.Empty(SpreadsheetReader.ReadXlsx(Xlsx(new[] { new Sheet("S", Array.Empty<string[]>()) })));
    }

    [Fact]
    public void Not_a_zip_is_reported_not_crashed()
    {
        Assert.Throws<SpreadsheetFormatException>(() => SpreadsheetReader.ReadXlsx(Encoding.UTF8.GetBytes("không phải xlsx")));
    }

    [Fact]
    public void Date_styled_numeric_cells_become_iso_text()
    {
        const string styles = """
            <styleSheet><cellXfs count="2"><xf numFmtId="0"/><xf numFmtId="14"/></cellXfs></styleSheet>
            """;
        string Custom(string[][] _) => """
            <worksheet><sheetData>
            <row r="1"><c r="A1" t="inlineStr"><is><t>Order ID</t></is></c><c r="B1" t="inlineStr"><is><t>Product Name</t></is></c><c r="C1" t="inlineStr"><is><t>Quantity</t></is></c><c r="D1" t="inlineStr"><is><t>Order Status</t></is></c><c r="E1" t="inlineStr"><is><t>Ship Time</t></is></c></row>
            <row r="2"><c r="A2" t="inlineStr"><is><t>O1</t></is></c><c r="B2" t="inlineStr"><is><t>A</t></is></c><c r="C2"><v>1</v></c><c r="D2" t="inlineStr"><is><t>Done</t></is></c><c r="E2" s="1"><v>46349.5</v></c></row>
            </sheetData></worksheet>
            """;
        var t = SpreadsheetReader.ReadXlsx(Xlsx(new[] { new Sheet("S", Array.Empty<string[]>()) }, stylesXml: styles, custom: Custom));

        Assert.Equal("1", t[0]["Quantity"]);                                // ô số thường giữ nguyên
        Assert.Equal("2026-11-23 12:00:00", t[0]["Ship Time"]);             // ô ngày có kiểu ngày -> chuỗi ISO
    }

    [Fact]
    public void Personal_columns_can_be_dropped()
    {
        var rows = new[]
        {
            new[] { "Order ID", "Product Name", "Quantity", "Order Status", "Tên khách hàng", "Số điện thoại", "Địa chỉ" },
            new[] { "O1", "A", "1", "Done", "Nguyễn Văn Giả", "0900000000", "Số 1 Đường Giả" },
        };
        var t = SpreadsheetReader.DropPersonalColumns(SpreadsheetReader.ReadXlsx(Xlsx(new[] { new Sheet("S", rows) })));

        Assert.False(t[0].ContainsKey("Tên khách hàng"));
        Assert.False(t[0].ContainsKey("Số điện thoại"));
        Assert.False(t[0].ContainsKey("Địa chỉ"));
        Assert.Equal("O1", t[0]["Order ID"]);
    }

    // ---- CSV ----
    [Fact]
    public void Csv_handles_quotes_commas_newlines_and_bom()
    {
        var csv = "﻿Order ID,Product Name,Quantity,Order Status\r\nO1,\"Áo, \"\"giả\"\"\",2,Done\r\nO2,\"dòng 1\ndòng 2\",1,Done\r\n";
        var t = SpreadsheetReader.ReadCsv(csv);

        Assert.Equal(2, t.Count);
        Assert.Equal("Áo, \"giả\"", t[0]["Product Name"]);
        Assert.Equal("dòng 1\ndòng 2", t[1]["Product Name"]);
        Assert.Equal("O1", t[0]["Order ID"]);   // BOM không làm hỏng tên cột đầu
    }

    [Fact]
    public void Csv_keeps_long_codes_as_text()
    {
        var t = SpreadsheetReader.ReadCsv("Order ID,Product Name,Quantity,Order Status\n580123456789012345,A,1,Done\n");
        Assert.Equal("580123456789012345", t[0]["Order ID"]);
    }

    [Fact]
    public void Read_dispatches_by_extension()
    {
        var x = Xlsx(new[] { new Sheet("S", new[] { Header, new[] { "O1", "A", "1", "Done" } }) });
        Assert.Single(SpreadsheetReader.Read("a.xlsx", x));
        Assert.Single(SpreadsheetReader.Read("b.CSV", Encoding.UTF8.GetBytes("Order ID,Product Name,Quantity,Order Status\nO1,A,1,Done\n")));
        Assert.Throws<SpreadsheetFormatException>(() => SpreadsheetReader.Read("c.pdf", x));
    }

    [Fact]
    public void Vietnamese_tone_removal_normalizes_headers()
    {
        Assert.Equal("madonhang", SpreadsheetReader.NormalizeHeader("Mã đơn hàng"));
        Assert.Equal("trangthaidonhang", SpreadsheetReader.NormalizeHeader("Trạng thái đơn hàng"));
        Assert.Equal("orderid", SpreadsheetReader.NormalizeHeader("Order ID"));
    }
}

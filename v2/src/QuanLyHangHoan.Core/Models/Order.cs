namespace QuanLyHangHoan.Core.Models;

/// <summary>Trạng thái nhận hàng tại kho (cột "Nhận Tại Kho" của app cũ).</summary>
public static class ReceiveStatus
{
    public const string Received = "Hàng đã trả";
    public const string NotReturned = "Chưa trả";
    public const string NotNeeded = "Không cần nhận";
    public const string Lost = "Bị mất";
    public const string NotYetArrived = "Chưa về"; // giá trị có thật trong dữ liệu cũ
}

public sealed class OrderItem
{
    public string Product { get; set; } = "";
    public string Color { get; set; } = "";
    public string Size { get; set; } = "";
    public int Qty { get; set; }
    /// <summary>
    /// Số lượng của mặt hàng NÀY đã được khách hoàn (từ file/Sheet hoàn hàng). 0 = chưa biết hoặc không nằm trong yêu cầu hoàn.
    /// Dùng để hiển thị: đơn gửi 3 sản phẩm mà chỉ hoàn 1 thì vẫn giữ đủ 3 dòng, dòng được hoàn (ReturnedQty &gt; 0) khác màu.
    /// Nếu KHÔNG dòng nào có ReturnedQty &gt; 0 thì giao diện không phân biệt (dữ liệu cũ/không rõ).
    /// </summary>
    public int ReturnedQty { get; set; }
    public string? Sku { get; set; }
    public string? SkuId { get; set; }
    /// <summary>Giá theo từng ô nguồn khi nhập (unitReturn, discounted, originalUnit, subtotal, saleworkUnit...). null nếu không có.</summary>
    public Dictionary<string, long>? ImportPrices { get; set; }
}

/// <summary>Thông tin giá của lần nhập gần nhất (app cũ lưu rời trên đơn: priceModes, importModeSlots...).</summary>
public sealed class ImportMeta
{
    public Dictionary<string, long> PriceModes { get; set; } = new();
    public Dictionary<string, bool> PriceModeFlags { get; set; } = new();
    /// <summary>mode -> slot nguồn; null = giá cấp ĐƠN; thiếu khóa = không có giá.</summary>
    public Dictionary<string, string?> ImportModeSlots { get; set; } = new();
    public string? ImportSelectedPriceMode { get; set; }
    public bool? PriceHasValue { get; set; }
    public bool? PriceNeedsReview { get; set; }
    /// <summary>Nạp Shopee: cơ sở giá ("order"/"line"), hãng vận chuyển giao/trả, chi tiết thô của file (rawDetails; số lưu dạng chữ số).</summary>
    public string? ImportPriceBasis { get; set; }
    public string ForwardCarrier { get; set; } = "";
    public string ReturnCarrier { get; set; } = "";
    public Dictionary<string, string> RawDetails { get; set; } = new();
}

/// <summary>
/// Một KIỆN hoàn = một dòng. Một OrderId gốc có thể có nhiều kiện (A/B/C) với mã vận đơn riêng.
/// Không bao giờ gộp hai kiện chỉ vì cùng OrderId hoặc cùng mã yêu cầu hoàn.
/// </summary>
public sealed class Order
{
    /// <summary>Khóa nội bộ ổn định (GUID hoặc khóa suy ra khi nhập dữ liệu cũ).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string OrderId { get; set; } = "";          // mã đơn gốc (có thể dùng chung giữa các kiện)
    public string ReturnOrderId { get; set; } = "";    // mã yêu cầu hoàn/trả hàng

    // Mã vận đơn của CHÍNH kiện này
    public string Track { get; set; } = "";
    public string ReturnTrack { get; set; } = "";
    public string AltTrack { get; set; } = "";
    /// <summary>Mã giao đi - có thể dùng chung giữa nhiều kiện, KHÔNG đủ để nhận một kiện.</summary>
    public string ForwardTrack { get; set; } = "";
    /// <summary>Mã lịch sử/thay thế được liên kết tường minh (Tracking Codes, scanAliases).</summary>
    public List<string> TrackingCodes { get; set; } = new();
    public List<string> ScanAliases { get; set; } = new();

    public string Platform { get; set; } = "";         // Shopee | TikTok Shop | Lazada | Khác
    public string OrderType { get; set; } = "";
    public string PlatformStatus { get; set; } = "";
    public string PreOrder { get; set; } = "";         // preOrder (đơn chính) / preorder (đơn hủy)
    public string Status { get; set; } = "";           // ReceiveStatus.*
    public string Carrier { get; set; } = "";

    public string ShipDate { get; set; } = "";         // giữ chuỗi gốc (định dạng app cũ), parse khi cần
    public string ReturnDate { get; set; } = "";
    public string RecvDate { get; set; } = "";
    public string ReturnedAt { get; set; } = "";
    public string Deadline { get; set; } = "";

    public decimal Price { get; set; }
    public string Note { get; set; } = "";             // lý do hoàn/hủy
    public string BuyerNote { get; set; } = "";
    /// <summary>Ghi chú của chủ shop. App cũ lưu rời rạc ở myNote/userNote -> gộp một trường.</summary>
    public string UserNote { get; set; } = "";

    public string ImportWeek { get; set; } = "";
    public string ImportedAt { get; set; } = "";
    public bool IsWatchlist { get; set; }
    public string WatchlistReason { get; set; } = "";
    public bool IsPartialReturn { get; set; }

    public string PhotoId { get; set; } = "";          // trỏ tới file ảnh
    public List<OrderItem> Items { get; set; } = new();
    private ImportMeta? _import;
    private Func<ImportMeta>? _importFactory;

    /// <summary>Thông tin lần nhập. Với đơn đọc từ CSDL thì chỉ phân tích JSON khi cần dùng lần đầu (nạp hàng nghìn đơn nhanh hơn nhiều).</summary>
    public ImportMeta Import
    {
        get
        {
            var m = System.Threading.Volatile.Read(ref _import);
            if (m is not null) return m;
            var made = _importFactory?.Invoke() ?? new ImportMeta();
            return System.Threading.Interlocked.CompareExchange(ref _import, made, null) ?? made;
        }
        set => _import = value;
    }

    /// <summary>Đăng ký cách tạo <see cref="Import"/> khi cần (chỉ dùng lúc đọc từ CSDL).</summary>
    public void SetLazyImport(Func<ImportMeta> factory) { _importFactory = factory; _import = null; }

    /// <summary>Dữ liệu phụ chưa có cột riêng (rawDetails, priceModes...) giữ nguyên dạng JSON để không mất.</summary>
    public string ExtraJson { get; set; } = "";
}

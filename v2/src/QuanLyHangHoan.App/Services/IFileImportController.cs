using System.IO;
using QuanLyHangHoan.Core.Import;
using QuanLyHangHoan.Data;
using QuanLyHangHoan.Data.Import;

namespace QuanLyHangHoan.App.Services;

public interface IFileImportController
{
    bool IsDemoMode { get; }
    event Action<FileImportResult>? Completed;
    Task<FileImportResult> ImportAsync(
        IReadOnlyList<string> filePaths,
        TikTokMapOptions options,
        ImportProductMode mode,
        CancellationToken ct = default);
}

/// <summary>
/// Điều phối nạp tay file Excel/CSV thật: đọc byte với FileShare.ReadWrite (không sửa/xóa/di chuyển file gốc)
/// và chạy FileImportService ở luồng nền.
/// </summary>
public sealed class RealFileImportController : IFileImportController
{
    private readonly string _dbPath;
    private readonly Func<DateTime> _clock;

    public RealFileImportController(string dbPath, Func<DateTime>? clock = null)
    {
        _dbPath = dbPath;
        _clock = clock ?? (() => DateTime.Now);
    }

    public bool IsDemoMode => false;
    public event Action<FileImportResult>? Completed;

    public async Task<FileImportResult> ImportAsync(
        IReadOnlyList<string> filePaths,
        TikTokMapOptions options,
        ImportProductMode mode,
        CancellationToken ct = default)
    {
        var result = await Task.Run(() =>
        {
            var loaded = new List<(string Name, byte[] Bytes)>();
            var readErrors = new List<FileImportItem>();

            foreach (var path in filePaths)
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(path);
                try
                {
                    var bytes = ReadAllBytesShared(path);
                    loaded.Add((name, bytes));
                }
                catch (Exception ex)
                {
                    readErrors.Add(new FileImportItem(name, FileKind.Unknown, 0, ex.Message));
                }
            }

            FileImportResult importResult;
            if (loaded.Count > 0)
            {
                // An toàn dữ liệu thật: luôn sao lưu NGAY TRƯỚC khi nạp file. Sao lưu hỏng (mà đã có dữ liệu) thì KHÔNG nạp.
                if (File.Exists(_dbPath))
                {
                    var bk = BackupService.EnsureRecent(_dbPath, BackupService.DefaultDir(_dbPath), TimeSpan.FromMinutes(30), 7, _clock());
                    if (!bk.Ok)
                    {
                        var blocked = new FileImportResult { Ok = false, Message = "Không nạp vì chưa sao lưu được dữ liệu hiện tại: " + bk.Message };
                        blocked.Files.AddRange(readErrors);
                        return blocked;
                    }
                }
                importResult = FileImportService.Import(_dbPath, loaded, options, mode, _clock());
            }
            else
            {
                importResult = new FileImportResult { Ok = true };
            }

            if (readErrors.Count > 0)
            {
                importResult.Files.InsertRange(0, readErrors);
            }

            return importResult;
        }, ct);

        Completed?.Invoke(result);
        return result;
    }

    private static byte[] ReadAllBytesShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return ms.ToArray();
    }
}

/// <summary>
/// Bản giả cho chế độ --demo và test giao diện: KHÔNG đọc ổ đĩa thật.
/// </summary>
public sealed class FakeFileImportController : IFileImportController
{
    private readonly Func<IReadOnlyList<string>, TikTokMapOptions, ImportProductMode, FileImportResult>? _handler;

    public FakeFileImportController(
        Func<IReadOnlyList<string>, TikTokMapOptions, ImportProductMode, FileImportResult>? handler = null)
    {
        _handler = handler;
    }

    public bool IsDemoMode => true;
    public IReadOnlyList<string>? LastFilePaths { get; private set; }
    public TikTokMapOptions? LastOptions { get; private set; }
    public ImportProductMode? LastMode { get; private set; }

    public event Action<FileImportResult>? Completed;

    public Task<FileImportResult> ImportAsync(
        IReadOnlyList<string> filePaths,
        TikTokMapOptions options,
        ImportProductMode mode,
        CancellationToken ct = default)
    {
        LastFilePaths = filePaths.ToList();
        LastOptions = options;
        LastMode = mode;

        FileImportResult result;
        if (_handler is not null)
        {
            result = _handler(filePaths, options, mode);
        }
        else
        {
            result = BuildDemoResult(filePaths);
        }

        Completed?.Invoke(result);
        return Task.FromResult(result);
    }

    private static FileImportResult BuildDemoResult(IReadOnlyList<string> filePaths)
    {
        var result = new FileImportResult { Ok = true };
        var hasKnown = false;

        foreach (var path in filePaths)
        {
            var name = Path.GetFileName(path);
            var ext = Path.GetExtension(name).ToLowerInvariant();
            if (ext == ".xls")
            {
                result.Files.Add(new FileImportItem(name, FileKind.Unknown, 0, "File .xls cũ chưa hỗ trợ — hãy lưu lại thành .xlsx."));
                continue;
            }

            if (ext != ".xlsx" && ext != ".csv")
            {
                result.Files.Add(new FileImportItem(name, FileKind.Unknown, 0, "Chỉ hỗ trợ file .xlsx và .csv."));
                continue;
            }

            var isShopee = name.Contains("shopee", StringComparison.OrdinalIgnoreCase);
            var kind = isShopee ? FileKind.Shopee : FileKind.TikTok;
            hasKnown = true;
            result.Files.Add(new FileImportItem(name, kind, isShopee ? 8 : 6, null));
        }

        if (hasKnown)
        {
            result.TikTok = new TikTokImportResult { Ok = true, Added = 2, Updated = 1 };
        }

        return result;
    }
}

using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Dịch vụ chạy nền tự động dời đơn "Hàng đã trả" đủ điều kiện sang Kho lưu trữ.
/// </summary>
public interface IArchiveSweeper
{
    Task<int> SweepAsync(DateTime now, CancellationToken cancellationToken = default);
}

public sealed class SqliteArchiveSweeper : IArchiveSweeper
{
    private readonly string _dbPath;

    public SqliteArchiveSweeper(string dbPath)
    {
        _dbPath = dbPath;
    }

    public Task<int> SweepAsync(DateTime now, CancellationToken cancellationToken = default)
        => Task.Run(() => ArchiveSweep.Run(_dbPath, now), cancellationToken);
}

public sealed class NoOpArchiveSweeper : IArchiveSweeper
{
    public Task<int> SweepAsync(DateTime now, CancellationToken cancellationToken = default)
        => Task.FromResult(0);
}

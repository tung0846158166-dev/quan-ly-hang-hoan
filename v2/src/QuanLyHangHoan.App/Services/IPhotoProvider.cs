using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Dịch vụ đọc ảnh theo yêu cầu và thống kê ảnh bị mất từ kho lưu trữ ảnh.
/// </summary>
public interface IPhotoProvider
{
    Task<PhotoData?> GetPhotoAsync(string photoId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetMissingPhotoIdsAsync(CancellationToken cancellationToken = default);
}

public sealed class SqlitePhotoProvider : IPhotoProvider
{
    private readonly string _dbPath;

    public SqlitePhotoProvider(string dbPath)
    {
        _dbPath = dbPath;
    }

    public Task<PhotoData?> GetPhotoAsync(string photoId, CancellationToken cancellationToken = default)
        => Task.Run(() => PhotoRepository.Get(_dbPath, photoId), cancellationToken);

    public Task<IReadOnlyList<string>> GetMissingPhotoIdsAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => PhotoRepository.MissingIds(_dbPath), cancellationToken);
}

public sealed class NoOpPhotoProvider : IPhotoProvider
{
    public Task<PhotoData?> GetPhotoAsync(string photoId, CancellationToken cancellationToken = default)
        => Task.FromResult<PhotoData?>(null);

    public Task<IReadOnlyList<string>> GetMissingPhotoIdsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
}

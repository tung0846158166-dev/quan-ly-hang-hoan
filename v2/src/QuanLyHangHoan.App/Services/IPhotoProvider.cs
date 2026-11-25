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

/// <summary>
/// Chế độ cloud: ảnh trong máy trước (dữ liệu cũ), rồi bộ nhớ đệm ảnh cloud, rồi tải từ cloud (R2) và lưu đệm.
/// Mất mạng / chưa ghép / cloud không có ảnh: trả null (màn hình báo không có ảnh), không ném lỗi.
/// </summary>
public sealed class CloudPhotoProvider : IPhotoProvider
{
    private readonly string _dbPath;
    private readonly Func<QuanLyHangHoan.Data.Cloud.CloudApi?> _api;

    public CloudPhotoProvider(string dbPath, Func<QuanLyHangHoan.Data.Cloud.CloudApi?> api)
    {
        _dbPath = dbPath;
        _api = api;
    }

    public async Task<PhotoData?> GetPhotoAsync(string photoId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(photoId)) return null;
        var local = await Task.Run(() => LocalOrNull(photoId) ?? QuanLyHangHoan.Data.Cloud.CloudPhotoCache.Get(_dbPath, photoId), cancellationToken).ConfigureAwait(false);
        if (local is not null) return local;
        if (_api() is not { } api) return null;
        try
        {
            var bytes = await api.GetPhotoAsync(photoId, thumb: false, cancellationToken).ConfigureAwait(false);
            if (bytes.Length == 0) return null;
            var mime = QuanLyHangHoan.Data.Cloud.CloudPhotoCache.DetectMime(bytes);
            await Task.Run(() => QuanLyHangHoan.Data.Cloud.CloudPhotoCache.Put(_dbPath, photoId, mime, bytes), cancellationToken).ConfigureAwait(false);
            return new PhotoData(photoId, mime, bytes, "cloud", false);
        }
        catch (QuanLyHangHoan.Data.Cloud.CloudApiException) { return null; }
        catch (System.Net.Http.HttpRequestException) { return null; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }   // hết giờ chờ
    }

    /// <summary>Ảnh gốc trong máy; máy mới chưa có bảng ảnh gốc thì coi như không có.</summary>
    private PhotoData? LocalOrNull(string photoId)
    {
        try { return PhotoRepository.Get(_dbPath, photoId); }
        catch (Microsoft.Data.Sqlite.SqliteException) { return null; }
    }

    public Task<IReadOnlyList<string>> GetMissingPhotoIdsAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => { try { return PhotoRepository.MissingIds(_dbPath); } catch (Microsoft.Data.Sqlite.SqliteException) { return (IReadOnlyList<string>)Array.Empty<string>(); } }, cancellationToken);
}

public sealed class NoOpPhotoProvider : IPhotoProvider
{
    public Task<PhotoData?> GetPhotoAsync(string photoId, CancellationToken cancellationToken = default)
        => Task.FromResult<PhotoData?>(null);

    public Task<IReadOnlyList<string>> GetMissingPhotoIdsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
}

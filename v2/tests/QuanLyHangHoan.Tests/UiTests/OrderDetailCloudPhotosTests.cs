using QuanLyHangHoan.App.Services;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Models;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.Tests.UiTests;

/// <summary>Chủ dự án 26/11/2026: bấm vào chi tiết đơn trên máy tính phải thấy ảnh, kể cả ảnh điện thoại chụp (trên cloud). Dữ liệu GIẢ.</summary>
public sealed class OrderDetailCloudPhotosTests
{
    private static readonly DateTime Now = new(2026, 11, 26, 12, 0, 0);

    // ảnh PNG 1x1 hợp lệ
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private sealed class Photos(Dictionary<string, string[]> perParcel) : IPhotoProvider
    {
        public List<string> Fetched { get; } = new();
        public Task<PhotoData?> GetPhotoAsync(string photoId, CancellationToken cancellationToken = default)
        {
            lock (Fetched) Fetched.Add(photoId);
            return Task.FromResult<PhotoData?>(new PhotoData(photoId, "image/png", Png, "test", false));
        }
        public Task<IReadOnlyList<string>> GetMissingPhotoIdsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IReadOnlyList<string>> ListPhotoIdsAsync(string parcelId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(perParcel.TryGetValue(parcelId, out var v) ? v : Array.Empty<string>());
    }

    private static Order O(string id, string photoId = "") => new() { Id = id, OrderId = "OD" + id, Track = "T" + id, Platform = "Shopee", PhotoId = photoId };

    [Fact]
    public void Order_without_its_own_photo_shows_the_newest_cloud_photo_and_lists_the_rest()
    {
        var p = new Photos(new() { ["p1"] = new[] { "cloud_new", "cloud_old" } });
        var vm = new OrderDetailViewModel(O("p1"), Now, p);
        WpfTestHost.Run(() => WpfTestHost.RunTask(() => vm.LoadAsync()));
        Assert.True(vm.HasPhotoId);
        Assert.True(vm.HasPhotoLoaded);
        Assert.Equal("cloud_new", vm.MainPhotoId);
        Assert.Equal(new[] { "cloud_old" }, vm.ExtraPhotos.Select(x => x.PhotoId));
        Assert.Contains("2 ảnh", vm.PhotoCountText);
    }

    [Fact]
    public void Own_photo_stays_main_and_cloud_duplicates_are_not_repeated()
    {
        var p = new Photos(new() { ["p2"] = new[] { "mine", "phone_shot" } });
        var vm = new OrderDetailViewModel(O("p2", "mine"), Now, p);
        WpfTestHost.Run(() => WpfTestHost.RunTask(() => vm.LoadAsync()));
        Assert.Equal("mine", vm.MainPhotoId);
        Assert.Equal(new[] { "phone_shot" }, vm.ExtraPhotos.Select(x => x.PhotoId));
    }

    [Fact]
    public void No_photo_anywhere_says_so()
    {
        var vm = new OrderDetailViewModel(O("p3"), Now, new Photos(new()));
        WpfTestHost.Run(() => WpfTestHost.RunTask(() => vm.LoadAsync()));
        Assert.False(vm.HasPhotoId);
        Assert.Equal("Chưa có ảnh", vm.PhotoStatusText);
        Assert.Empty(vm.ExtraPhotos);
    }
}

using Microsoft.Win32;

namespace QuanLyHangHoan.App.Services;

public interface IImageFilePickerService
{
    string? PickImageFile(string title);
}

public sealed class WindowsImageFilePickerService : IImageFilePickerService
{
    public string? PickImageFile(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Ảnh (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
            Multiselect = false,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

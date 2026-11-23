using Microsoft.Win32;

namespace QuanLyHangHoan.App.Services;

/// <summary>
/// Hộp thoại chọn thư mục gốc Windows (.NET 8 WPF OpenFolderDialog).
/// </summary>
public sealed class WindowsFolderPickerService : IFolderPickerService
{
    public string? PickFolder(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}

using Microsoft.Win32;

namespace QuanLyHangHoan.App.Services;

public interface IImportFilePickerService
{
    IReadOnlyList<string> PickFiles();
}

public sealed class WindowsImportFilePickerService : IImportFilePickerService
{
    public IReadOnlyList<string> PickFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn file Excel / CSV để nạp đơn",
            Filter = "File Excel / CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Tất cả file (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : Array.Empty<string>();
    }
}

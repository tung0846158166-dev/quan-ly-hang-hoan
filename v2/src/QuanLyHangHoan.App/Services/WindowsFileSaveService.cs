using System.IO;
using System.Text;
using Microsoft.Win32;

namespace QuanLyHangHoan.App.Services;

public sealed class WindowsFileSaveService : IFileSaveService
{
    public string? PickSavePath(string title, string defaultFileName, string filter)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = defaultFileName,
            Filter = filter,
            AddExtension = true,
            OverwritePrompt = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public Task WriteTextAsync(string filePath, string content, CancellationToken cancellationToken = default)
    {
        // UTF-8 with BOM để Excel mở tiếng Việt có dấu không bị lỗi font
        return File.WriteAllTextAsync(filePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);
    }
}

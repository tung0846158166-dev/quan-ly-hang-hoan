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

    public async Task WriteBytesAsync(string filePath, byte[] content, CancellationToken cancellationToken = default)
    {
        // Ghi tạm rồi đổi tên: lỗi giữa chừng không để lại tệp dở; tệp đích do người dùng đã đồng ý ghi đè ở hộp thoại lưu.
        var tmp = filePath + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(tmp, content, cancellationToken);
            File.Move(tmp, filePath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* bỏ qua */ }
            throw;
        }
    }
}

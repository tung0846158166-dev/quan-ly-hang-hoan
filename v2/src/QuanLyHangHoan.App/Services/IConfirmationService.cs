using System.Windows;

namespace QuanLyHangHoan.App.Services;

public interface IConfirmationService
{
    bool Confirm(string title, string message);
}

public sealed class WindowsConfirmationService : IConfirmationService
{
    public bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}

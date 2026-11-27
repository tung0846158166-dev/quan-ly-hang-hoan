using System.Windows;
using QuanLyHangHoan.App.Views;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Services;

public sealed class WindowsNewOrderDialogService : INewOrderDialogService
{
    public NewOrderInput? Ask()
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
        var win = new NewOrderWindow { Owner = owner };
        return win.ShowDialog() == true ? win.Result : null;
    }

    public Task RunOffPlatformAsync(ViewModels.OffPlatformOrderFormViewModel form)
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
        new OffPlatformOrderWindow(form) { Owner = owner }.ShowDialog();
        return Task.CompletedTask;
    }
}

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using QuanLyHangHoan.App.ViewModels;

namespace QuanLyHangHoan.App.Views;

public partial class GoogleSheetView : UserControl
{
    private GoogleSheetViewModel? _subscribedVm;
    private bool _syncingPassword;

    public GoogleSheetView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => UnsubscribeVm();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UnsubscribeVm();
        if (e.NewValue is GoogleSheetViewModel vm)
        {
            _subscribedVm = vm;
            vm.PropertyChanged += OnVmPropertyChanged;
            if (string.IsNullOrEmpty(vm.UrlInput) && UrlPasswordBox.Password.Length > 0)
            {
                _syncingPassword = true;
                UrlPasswordBox.Password = string.Empty;
                _syncingPassword = false;
            }
        }
    }

    private void UnsubscribeVm()
    {
        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged -= OnVmPropertyChanged;
            _subscribedVm = null;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GoogleSheetViewModel.UrlInput)
            && _subscribedVm is not null
            && string.IsNullOrEmpty(_subscribedVm.UrlInput)
            && UrlPasswordBox.Password.Length > 0)
        {
            _syncingPassword = true;
            UrlPasswordBox.Password = string.Empty;
            _syncingPassword = false;
        }
    }

    private void UrlPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingPassword) return;
        if (DataContext is GoogleSheetViewModel vm)
        {
            vm.UrlInput = UrlPasswordBox.Password;
        }
    }
}

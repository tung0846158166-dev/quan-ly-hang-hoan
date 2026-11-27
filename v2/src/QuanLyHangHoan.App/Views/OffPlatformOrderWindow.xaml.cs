using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuanLyHangHoan.App.ViewModels;
using QuanLyHangHoan.Core.Stats;

namespace QuanLyHangHoan.App.Views;

/// <summary>Form tạo nhanh đơn ngoài sàn (logic ở <see cref="OffPlatformOrderFormViewModel"/>).</summary>
public partial class OffPlatformOrderWindow : Window
{
    private readonly OffPlatformOrderFormViewModel _form;

    public OffPlatformOrderWindow(OffPlatformOrderFormViewModel form)
    {
        InitializeComponent();
        _form = form;
        DataContext = form;
        Loaded += (_, _) => SearchBox.Focus();
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                e.Handled = true;
                await SubmitAsync(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            }
        };
    }

    private async Task SubmitAsync(bool another)
    {
        if (await _form.SubmitAsync(another))
        {
            if (another) SearchBox.Focus();
            else Close();
        }
    }

    private async void Create_Click(object sender, RoutedEventArgs e) => await SubmitAsync(false);
    private async void CreateAnother_Click(object sender, RoutedEventArgs e) => await SubmitAsync(true);
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            _form.AddFromSearch();
        }
        else if (e.Key == Key.Down && _form.HasSuggestions)
        {
            e.Handled = true;
            SuggestionList.SelectedIndex = 0;
            (SuggestionList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
        }
    }

    private void SuggestionList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; AddSelected(); }
        else if (e.Key == Key.Escape) { e.Handled = true; SearchBox.Focus(); }
    }

    private void SuggestionList_Activate(object sender, MouseButtonEventArgs e) => AddSelected();

    private void AddSelected()
    {
        if (SuggestionList.SelectedItem is CatalogProduct p)
        {
            _form.AddProduct(p);
            SearchBox.Focus();
        }
    }
}

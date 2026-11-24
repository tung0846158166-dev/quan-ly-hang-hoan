using System.Windows;
using System.Windows.Controls;
using QuanLyHangHoan.App.ViewModels;

namespace QuanLyHangHoan.App.Views;

/// <summary>Khối cấu hình trợ lý AI trong Cài đặt (khóa API, danh sách mô hình). Khóa chỉ đi từ ô mật khẩu vào kho mã hóa, không lưu trong ViewModel.</summary>
public partial class AiSettingsCard : UserControl
{
    public AiSettingsCard() => InitializeComponent();

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AiChatViewModel vm) return;
        var groq = KeyBox.Password;
        var gemini = GeminiKeyBox.Password;
        KeyBox.Clear();
        GeminiKeyBox.Clear();
        await vm.SaveSettingsAsync(groq, gemini);
    }

    private void Forget_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AiChatViewModel vm) vm.ForgetKeys();
    }
}

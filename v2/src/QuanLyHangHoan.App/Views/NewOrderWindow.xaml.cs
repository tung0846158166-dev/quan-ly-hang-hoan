using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuanLyHangHoan.Data;

namespace QuanLyHangHoan.App.Views;

public partial class NewOrderWindow : Window
{
    public NewOrderWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => TrackBox.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; TryCreate(); }
        };
    }

    /// <summary>Có giá trị khi người dùng bấm "Tạo đơn" với dữ liệu hợp lệ; null khi hủy.</summary>
    public NewOrderInput? Result { get; private set; }

    /// <summary>Kiểm tra dữ liệu nhập và dựng đầu vào (test gọi trực tiếp, không cần bấm nút).</summary>
    public static NewOrderInput? Build(string track, string orderId, string platformTag, string product, string color, string size,
        string qtyText, string carrier, string note, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(track)) { error = "Hãy nhập mã vận đơn."; return null; }
        var qty = 1;
        if (!string.IsNullOrWhiteSpace(qtyText) && (!int.TryParse(qtyText.Trim(), out qty) || qty < 1 || qty > 9999))
        { error = "Số lượng phải là số nguyên từ 1 đến 9999."; return null; }
        return new NewOrderInput(track.Trim(), orderId.Trim(), platformTag, carrier.Trim(), product.Trim(), color.Trim(), size.Trim(), qty, note.Trim(), "manual");
    }

    private void TryCreate()
    {
        var tag = (PlatformBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        var input = Build(TrackBox.Text, OrderIdBox.Text, tag, ProductBox.Text, ColorBox.Text, SizeBox.Text, QtyBox.Text, CarrierBox.Text, NoteBox.Text, out var error);
        if (input is null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        Result = input;
        DialogResult = true;
    }

    private void Create_Click(object sender, RoutedEventArgs e) => TryCreate();
    private void Cancel_Click(object sender, RoutedEventArgs e) { Result = null; DialogResult = false; }
}

using System.Windows;
using System.Windows.Input;

namespace QuanLyHangHoan.App.Views;

public partial class EditNoteWindow : Window
{
    public EditNoteWindow(string title, string subtitle, string fieldLabel, string initialValue)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        FieldLabelText.Text = fieldLabel;
        NoteInput.Text = initialValue ?? string.Empty;
        Loaded += (_, _) =>
        {
            NoteInput.Focus();
            NoteInput.CaretIndex = NoteInput.Text.Length;
        };
    }

    public string EditedText => NoteInput.Text ?? string.Empty;

    private void SaveButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }
}

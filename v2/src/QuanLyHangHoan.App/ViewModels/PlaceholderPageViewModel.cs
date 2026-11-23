using CommunityToolkit.Mvvm.ComponentModel;
using QuanLyHangHoan.App.Services;

namespace QuanLyHangHoan.App.ViewModels;

/// <summary>
/// ViewModel khung giữ chỗ cho các màn hình phụ chưa nối DB/nghiệp vụ ở T002.
/// </summary>
public sealed class PlaceholderPageViewModel : ObservableObject
{
    public PlaceholderPageViewModel(PlaceholderScreenData data)
    {
        Title = data.Title;
        Subtitle = data.Subtitle;
        BadgeText = data.BadgeText;
        Col1Header = data.ColumnHeaders.Count > 0 ? data.ColumnHeaders[0] : "Cột 1";
        Col2Header = data.ColumnHeaders.Count > 1 ? data.ColumnHeaders[1] : "Cột 2";
        Col3Header = data.ColumnHeaders.Count > 2 ? data.ColumnHeaders[2] : "Cột 3";
        Col4Header = data.ColumnHeaders.Count > 3 ? data.ColumnHeaders[3] : "Cột 4";
        Col5Header = data.ColumnHeaders.Count > 4 ? data.ColumnHeaders[4] : "Cột 5";
        SampleRows = data.SampleRows;
        FooterNote = data.FooterNote;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string BadgeText { get; }
    public string Col1Header { get; }
    public string Col2Header { get; }
    public string Col3Header { get; }
    public string Col4Header { get; }
    public string Col5Header { get; }
    public IReadOnlyList<PlaceholderPreviewRow> SampleRows { get; }
    public string FooterNote { get; }
}

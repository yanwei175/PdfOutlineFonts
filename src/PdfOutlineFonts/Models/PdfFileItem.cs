using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PdfOutlineFonts.Models;

public partial class PdfFileItem : ObservableObject
{
    [ObservableProperty]
    private string filePath = string.Empty;

    [ObservableProperty]
    private string status = "等待中";

    [ObservableProperty]
    private bool isIndeterminate;

    [ObservableProperty]
    private string? errorMessage;

    public string FileName => Path.GetFileName(FilePath);
}

namespace PdfOutlineFonts.Services;

using PdfOutlineFonts.ViewModels;

public interface IGhostscriptService
{
    string? VersionText { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task ConvertAsync(string inputPath, string outputPath, PdfConvertMode convertMode, int dpi, CancellationToken cancellationToken);
}

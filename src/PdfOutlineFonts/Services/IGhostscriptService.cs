namespace PdfOutlineFonts.Services;

public interface IGhostscriptService
{
    string? VersionText { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task ConvertToOutlinesAsync(string inputPath, string outputPath, CancellationToken cancellationToken);
}

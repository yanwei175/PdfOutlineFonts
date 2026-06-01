using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using Ghostscript.NET;
using Ghostscript.NET.Processor;
using PdfOutlineFonts.ViewModels;

namespace PdfOutlineFonts.Services;

public sealed class GhostscriptService : IGhostscriptService
{
    private readonly SemaphoreSlim initializeLock = new(1, 1);
    private bool initialized;
    private string? dllPath;

    public string? VersionText { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (initialized)
        {
            return;
        }

        await initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
            {
                return;
            }

            dllPath = await EnsureGhostscriptDllAsync(cancellationToken);
            var version = FileVersionInfo.GetVersionInfo(dllPath).FileVersion;
            VersionText = string.IsNullOrWhiteSpace(version) ? "未知版本" : version;
            initialized = true;
        }
        finally
        {
            initializeLock.Release();
        }
    }

    public async Task ConvertAsync(string inputPath, string outputPath, PdfConvertMode convertMode, int dpi, CancellationToken cancellationToken)
    {
            await InitializeAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var folder = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var safeDpi = Math.Clamp(dpi, 72, 1200);
            var args = BuildArgs(inputPath, outputPath, convertMode, safeDpi);

            var versionInfo = new GhostscriptVersionInfo(dllPath!);

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var processor = new GhostscriptProcessor(versionInfo, true);
                processor.Process(args, null);
            }, cancellationToken);

  
    }

    private static string[] BuildArgs(string inputPath, string outputPath, PdfConvertMode convertMode, int dpi)
    {
        return convertMode switch
        {
            PdfConvertMode.Vector =>
            [
                "-dBATCH",
                "-dNOPAUSE",
                "-dQUIET",
                "-sDEVICE=pdfwrite",
                "-dNoOutputFonts",
                "-dCompatibilityLevel=1.4",
                $"-sOutputFile={outputPath}",
                inputPath
            ],
            PdfConvertMode.Image =>
            [
                "-dBATCH",
                "-dNOPAUSE",
                "-dQUIET",
                "-sDEVICE=pdfimage24",
                "-dAutoRotatePages=/None",
                "-dTextAlphaBits=4",
                "-dGraphicsAlphaBits=4",
                "-dAlignToPixels=0",
                "-dGridFitTT=2",
                $"-r{dpi}",
                $"-sOutputFile={outputPath}",
                inputPath
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(convertMode), convertMode, null)
        };
    }

    private static async Task<string> EnsureGhostscriptDllAsync(CancellationToken cancellationToken)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "PdfOutlineFonts");
        Directory.CreateDirectory(tempDirectory);

        var targetPath = Path.Combine(tempDirectory, "gsdll64.dll");
        if (File.Exists(targetPath))
        {
            return targetPath;
        }

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("Assets.gsdll64.dll", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            throw new FileNotFoundException("未找到嵌入资源 gsdll64.dll。请将 gsdll64.dll 放入 Assets 目录并设置为 Embedded Resource。");
        }

        await using var resourceStream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException("读取嵌入资源 gsdll64.dll 失败。");

        await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await resourceStream.CopyToAsync(fileStream, cancellationToken);

        return targetPath;
    }
}

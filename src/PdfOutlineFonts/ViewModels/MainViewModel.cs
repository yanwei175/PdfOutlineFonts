using System.Collections;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PdfOutlineFonts.Models;
using PdfOutlineFonts.Services;

namespace PdfOutlineFonts.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IGhostscriptService ghostscriptService;
    private readonly SemaphoreSlim conversionSemaphore = new(2, 2);
    private CancellationTokenSource? conversionCts;

    [ObservableProperty]
    private string ghostscriptStatus = "Ghostscript: 正在初始化...";

    [ObservableProperty]
    private string outputDirectory = "与源文件同目录";

    [ObservableProperty]
    private string overallStatusText = "就绪";

    [ObservableProperty]
    private double overallProgress;

    [ObservableProperty]
    private bool isConverting;

    [ObservableProperty]
    private int selectedItemCount;

    public ObservableCollection<PdfFileItem> Files { get; } = [];

    public MainViewModel(IGhostscriptService ghostscriptService)
    {
        this.ghostscriptService = ghostscriptService;

        AddFilesCommand = new RelayCommand(AddFiles, () => !IsConverting);
        RemoveSelectedFilesCommand = new RelayCommand<IList?>(RemoveSelectedFiles, selected => !IsConverting && selected is { Count: > 0 });
        ClearFilesCommand = new RelayCommand(ClearFiles, () => !IsConverting);
        ChooseOutputDirectoryCommand = new RelayCommand(ChooseOutputDirectory, () => !IsConverting);
        StartConversionCommand = new AsyncRelayCommand(StartConversionAsync, CanStartConversion);
        CancelConversionCommand = new RelayCommand(CancelConversion, () => IsConverting);

        _ = InitializeGhostscriptAsync();
    }

    public IRelayCommand AddFilesCommand { get; }

    public IRelayCommand<IList?> RemoveSelectedFilesCommand { get; }

    public IRelayCommand ClearFilesCommand { get; }

    public IRelayCommand ChooseOutputDirectoryCommand { get; }

    public IAsyncRelayCommand StartConversionCommand { get; }

    public IRelayCommand CancelConversionCommand { get; }

    public void AddFilesFromDrop(IEnumerable<string> filePaths)
    {
        if (IsConverting)
        {
            return;
        }

        foreach (var path in filePaths.Where(IsPdfFile).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Files.Any(item => string.Equals(item.FilePath, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            Files.Add(new PdfFileItem { FilePath = path });
        }

        RefreshCommandState();
    }

    private async Task InitializeGhostscriptAsync()
    {
        try
        {
            await ghostscriptService.InitializeAsync(CancellationToken.None);
            GhostscriptStatus = $"Ghostscript: {ghostscriptService.VersionText}";
        }
        catch (Exception ex)
        {
            GhostscriptStatus = $"Ghostscript 初始化失败: {ex.Message}";
        }
    }

    private bool CanStartConversion() => !IsConverting && Files.Count > 0;

    private void AddFiles()
    {
        if (IsConverting)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "PDF 文件 (*.pdf)|*.pdf",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            AddFilesFromDrop(dialog.FileNames);
        }
    }

    private void RemoveSelectedFiles(IList? selectedItems)
    {
        if (IsConverting || selectedItems is not { Count: > 0 })
        {
            return;
        }

        var items = selectedItems.OfType<PdfFileItem>().ToList();
        foreach (var item in items)
        {
            Files.Remove(item);
        }

        RefreshCommandState();
    }

    private void ClearFiles()
    {
        if (IsConverting)
        {
            return;
        }

        Files.Clear();
        OverallProgress = 0;
        OverallStatusText = "就绪";
        RefreshCommandState();
    }

    private void ChooseOutputDirectory()
    {
        if (IsConverting)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "选择输出目录"
        };

        if (dialog.ShowDialog() == true)
        {
            OutputDirectory = dialog.FolderName;
        }
    }

    private async Task StartConversionAsync()
    {
        if (IsConverting || Files.Count == 0)
        {
            return;
        }

        conversionCts = new CancellationTokenSource();
        var token = conversionCts.Token;

        IsConverting = true;
        OverallProgress = 0;
        var total = Files.Count;
        var completed = 0;
        var success = 0;
        var failed = 0;
        OverallStatusText = $"正在转换 0/{total}...";

        foreach (var file in Files)
        {
            if (file.Status == "转换中")
            {
                file.Status = "等待中";
            }

            if (file.Status != "已完成")
            {
                file.Status = "等待中";
                file.ErrorMessage = null;
            }

            file.IsIndeterminate = false;
        }

        var tasks = Files.Select(async file =>
        {
            await conversionSemaphore.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();

                file.Status = "转换中";
                file.IsIndeterminate = true;

                var outputDirectory = ResolveOutputDirectory(file.FilePath);
                var outputFile = Path.Combine(outputDirectory, GetOutlinedFileName(file.FilePath));

                await ghostscriptService.ConvertToOutlinesAsync(file.FilePath, outputFile, token);
                file.Status = "已完成";
                Interlocked.Increment(ref success);
            }
            catch (OperationCanceledException)
            {
                if (file.Status == "转换中")
                {
                    file.Status = "等待中";
                }
            }
            catch (Exception ex)
            {
                file.Status = "失败";
                file.ErrorMessage = ex.Message;
                Interlocked.Increment(ref failed);
            }
            finally
            {
                file.IsIndeterminate = false;
                var done = Interlocked.Increment(ref completed);
                OverallProgress = total == 0 ? 0 : done * 100d / total;
                OverallStatusText = $"正在转换 {done}/{total}...";
                conversionSemaphore.Release();
            }
        }).ToList();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // 忽略取消异常，让结果按文件状态展示
        }
        finally
        {
            IsConverting = false;
            conversionCts.Dispose();
            conversionCts = null;
            OverallStatusText = $"已完成 {completed}/{total}";
            RefreshCommandState();
        }

        System.Windows.MessageBox.Show(
            $"共 {total} 个，成功 {success} 个，失败 {failed} 个",
            "转换完成",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void CancelConversion()
    {
        conversionCts?.Cancel();
        OverallStatusText = "正在取消...";
    }

    partial void OnIsConvertingChanged(bool value)
    {
        RefreshCommandState();
    }

    partial void OnSelectedItemCountChanged(int value)
    {
        RemoveSelectedFilesCommand.NotifyCanExecuteChanged();
    }

    private void RefreshCommandState()
    {
        AddFilesCommand.NotifyCanExecuteChanged();
        RemoveSelectedFilesCommand.NotifyCanExecuteChanged();
        ClearFilesCommand.NotifyCanExecuteChanged();
        ChooseOutputDirectoryCommand.NotifyCanExecuteChanged();
        StartConversionCommand.NotifyCanExecuteChanged();
        CancelConversionCommand.NotifyCanExecuteChanged();
    }

    private static bool IsPdfFile(string path)
    {
        return File.Exists(path) && string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveOutputDirectory(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(OutputDirectory) || OutputDirectory == "与源文件同目录")
        {
            return Path.GetDirectoryName(inputPath) ?? Directory.GetCurrentDirectory();
        }

        Directory.CreateDirectory(OutputDirectory);
        return OutputDirectory;
    }

    private static string GetOutlinedFileName(string inputPath)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(inputPath);
        return $"{nameWithoutExtension}_outlined.pdf";
    }
}

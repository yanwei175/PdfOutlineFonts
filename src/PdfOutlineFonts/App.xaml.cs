using System.Windows;
using PdfOutlineFonts.Services;
using PdfOutlineFonts.ViewModels;
using PdfOutlineFonts.Views;

namespace PdfOutlineFonts;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        IGhostscriptService ghostscriptService = new GhostscriptService();
        var viewModel = new MainViewModel(ghostscriptService);

        var window = new MainWindow
        {
            DataContext = viewModel
        };

        window.Show();
    }
}

using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PdfOutlineFonts.ViewModels;

namespace PdfOutlineFonts.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void FileList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void FileList_Drop(object sender, DragEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] droppedFiles)
        {
            ViewModel.AddFilesFromDrop(droppedFiles);
        }
    }

    private void FileDataGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            RemoveSelectedItems();
            e.Handled = true;
        }
    }

    private void FileDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.SelectedItemCount = FileDataGrid.SelectedItems.Count;
        }
    }

    private void RemoveSelectedItems()
    {
        if (ViewModel is null)
        {
            return;
        }

        ViewModel.RemoveSelectedFilesCommand.Execute((IList)FileDataGrid.SelectedItems);
    }
}

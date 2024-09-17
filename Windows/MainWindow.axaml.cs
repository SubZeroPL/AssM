using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AssM.Classes;
using AssM.Data;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DiscTools;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace AssM.Windows;

public partial class MainWindow : Window
{
    private ObservableCollection<Game> GameList { get; } = [];
    private Configuration Configuration { get; }

    public MainWindow()
    {
        InitializeComponent();
        DataGridGameList.ItemsSource = GameList;
        TextTitle.AddHandler(TextInputEvent, TextTitle_OnTextInput, RoutingStrategies.Tunnel);
        TextDescription.AddHandler(TextInputEvent, TextDescription_OnTextInput, RoutingStrategies.Tunnel);
        Configuration = Configuration.Load() ?? new Configuration();
        TextBoxOutputDirectory.Text = Configuration.OutputDirectory;
        CheckBoxOverwriteChd.IsChecked = Configuration.OverwriteExistingChds;
        CheckBoxOverwriteReadme.IsChecked = Configuration.OverwriteExistingReadmes;
        CheckBoxGetTitleFromCue.IsChecked = Configuration.GetTitleFromCue;
        Closing += (_, _) =>
        {
            Configuration.OutputDirectory = TextBoxOutputDirectory.Text;
            Configuration.OverwriteExistingChds = CheckBoxOverwriteChd.IsChecked.Value;
            Configuration.OverwriteExistingReadmes = CheckBoxOverwriteReadme.IsChecked.Value;
            Configuration.GetTitleFromCue = CheckBoxGetTitleFromCue.IsChecked.Value;
            Configuration.Save();
        };
    }

    private async void AddButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var fpo = new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = "Select images",
            FileTypeFilter = [new FilePickerFileType("CUE file") { Patterns = ["*.cue"] }]
        };
        var files = await GetTopLevel(this)?.StorageProvider.OpenFilePickerAsync(fpo)!;
        foreach (var file in files)
        {
            await Functions.AddGameToList(file.Path.LocalPath, GameList);
        }
    }

    private void ClearButton_OnClick(object? sender, RoutedEventArgs e)
    {
        GameList.Clear();
        TextTitle.Clear();
        TextDescription.Clear();
        LabelSha1.Content = string.Empty;
        LabelFileVersion.Content = string.Empty;
        LabelDataSha1.Content = string.Empty;
        LabelChdManVersion.Content = string.Empty;
    }

    private async void AddFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var fpo = new FolderPickerOpenOptions()
        {
            Title = "Select folder with images"
        };

        var dirs = (await GetTopLevel(this)?.StorageProvider.OpenFolderPickerAsync(fpo)!).Select(d => d.Path.LocalPath)
            .ToList();
        var progress = new AddFolderProgressWindow();
        _ = progress.ShowDialog(this);
        await progress.Process(dirs, GameList, TextBoxOutputDirectory.Text);
        progress.Close();
        DataGridGameList.CollectionView.Refresh();
    }

    private void DeleteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataGridGameList.SelectedIndex < 0) return;
        GameList.RemoveAt(DataGridGameList.SelectedIndex);
    }

    private void DataGridGameList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataGridGameList?.SelectedItem is not Game game) return;
        Functions.LoadExistingData(TextBoxOutputDirectory.Text, game);
        DataGridGameList.CollectionView.Refresh();

        TextTitle.Text = game.Title;
        TextDescription.Text = game.Description;
        LabelChdManVersion.Content = game.ChdData.ChdManVersion;
        LabelFileVersion.Content = game.ChdData.FileVersion;
        LabelSha1.Content = game.ChdData.Sha1Hash;
        LabelDataSha1.Content = game.ChdData.DataSha1Hash;
        TextBoxTrackInfo.Text = game.ChdData.GetTrackInfo();
    }

    private void TextDescription_OnTextInput(object? sender, TextInputEventArgs e)
    {
        var selected = DataGridGameList.SelectedIndex;
        if (selected < 0) return;
        var item = GameList[selected];
        item.Description = TextDescription.Text + e.Text;
        DataGridGameList.CollectionView.Refresh();
    }

    private void TextTitle_OnTextInput(object? sender, TextInputEventArgs e)
    {
        var selected = DataGridGameList.SelectedIndex;
        if (selected < 0) return;
        var item = GameList[selected];
        item.Title = TextTitle.Text + e.Text;
        DataGridGameList.CollectionView.Refresh();
    }

    private async void StartProcessingButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TextBoxOutputDirectory.Text))
        {
            await MessageBoxManager.GetMessageBoxStandard("Error", "Please provide a valid output directory",
                ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error).ShowAsync();
            return;
        }

        if (GameList.ToList().Count == 0)
        {
            await MessageBoxManager.GetMessageBoxStandard("Error", "Please provide at least one game", ButtonEnum.Ok,
                MsBox.Avalonia.Enums.Icon.Error).ShowAsync();
            return;
        }

        var progress = new ProgressWindow();
        _ = progress.ShowDialog(this);
        try
        {
            await progress.Process(GameList.ToList(), TextBoxOutputDirectory.Text,
                CheckBoxOverwriteChd.IsChecked ?? false, CheckBoxOverwriteReadme.IsChecked ?? false);
            await MessageBoxManager.GetMessageBoxStandard("Processing finished", "Processing finished").ShowAsync();
        }
        catch (Exception ex)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(progress.Title, ex.Message, ButtonEnum.Ok,
                MsBox.Avalonia.Enums.Icon.Error);
            await box.ShowAsync();
        }

        progress.Close();
        DataGridGameList.CollectionView.Refresh();
    }

    private async void SelectOutputFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var fpo = new FolderPickerOpenOptions
        {
            Title = "Select output folder",
            AllowMultiple = false
        };
        var dirs = await GetTopLevel(this)?.StorageProvider.OpenFolderPickerAsync(fpo)!;
        if (!dirs.Any()) return;
        var dir = dirs[0];
        TextBoxOutputDirectory.Text = dir.Path.LocalPath;
        foreach (var game in GameList)
        {
            Functions.LoadExistingData(TextBoxOutputDirectory.Text, game);    
        }
        DataGridGameList.CollectionView.Refresh();
    }
}
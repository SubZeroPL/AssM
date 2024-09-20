using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using AssM.Classes;
using AssM.Data;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
        var ver = Assembly.GetEntryAssembly()?.GetName().Version;
        if (ver != null)
        {
            Title = $"AssM {ver.Major}.{ver.Minor}.{ver.Build}";
        }
        DataGridGameList.ItemsSource = GameList;
        TextTitle.AddHandler(TextInputEvent, TextTitle_OnTextInput, RoutingStrategies.Tunnel);
        TextDescription.AddHandler(TextInputEvent, TextDescription_OnTextInput, RoutingStrategies.Tunnel);
        Configuration = Configuration.Load() ?? new Configuration();
        TextBoxOutputDirectory.Text = Configuration.OutputDirectory;
        RadioButtonDontGenerate.IsChecked = Configuration.ChdProcessing == ChdProcessing.DontGenerate;
        RadioButtonGenerateMissing.IsChecked = Configuration.ChdProcessing == ChdProcessing.GenerateMissing;
        RadioButtonGenerateAll.IsChecked = Configuration.ChdProcessing == ChdProcessing.GenerateAll;
        CheckBoxOverwriteReadme.IsChecked = Configuration.OverwriteExistingReadmes;
        CheckBoxGetTitleFromCue.IsChecked = Configuration.GetTitleFromCue;
        CheckBoxGameIdAsChdName.IsChecked = Configuration.GameIdAsChdName;
        Closing += (_, _) =>
        {
            Configuration.OutputDirectory = TextBoxOutputDirectory.Text;
            Configuration.OverwriteExistingReadmes = CheckBoxOverwriteReadme.IsChecked.Value;
            Configuration.GetTitleFromCue = CheckBoxGetTitleFromCue.IsChecked.Value;
            Configuration.GameIdAsChdName = CheckBoxGameIdAsChdName.IsChecked.Value;
            Configuration.Save();
        };
        if (!string.IsNullOrWhiteSpace(Configuration.OutputDirectory)) AddReadmes(Configuration.OutputDirectory);
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
            var game = Functions.AddGameToList(file.Path.LocalPath, Configuration, GameList);
            if (string.IsNullOrWhiteSpace(TextBoxOutputDirectory.Text)) continue;
            Functions.LoadExistingData(game, Configuration);
        }

        DataGridGameList.CollectionView.Refresh();
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
        TextBoxTrackInfo.Clear();
    }

    private async void AddFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var fpo = new FolderPickerOpenOptions
        {
            Title = "Select folder with images"
        };

        var dirs = (await GetTopLevel(this)?.StorageProvider.OpenFolderPickerAsync(fpo)!).Select(d => d.Path.LocalPath)
            .ToList();
        var progress = new AddFolderProgressWindow();
        _ = progress.ShowDialog(this);
        await progress.Process(dirs, GameList, Configuration);
        progress.Close();
        DataGridGameList.CollectionView.Refresh();
    }

    private void DeleteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataGridGameList.SelectedItem is not Game game) return;
        GameList.Remove(game);
    }

    private void DataGridGameList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataGridGameList?.SelectedItem is not Game game) return;
        Functions.LoadExistingData(game, Configuration);
        DataGridGameList.CollectionView.Refresh();

        TextTitle.Text = game.Title;
        TextDescription.Text = game.Description;
        LabelChdManVersion.Content = game.ChdData.ChdManVersion;
        LabelFileVersion.Content = game.ChdData.FileVersion;
        LabelSha1.Content = game.ChdData.Sha1Hash;
        LabelDataSha1.Content = game.ChdData.DataSha1Hash;
        TextBoxTrackInfo.Text = game.ChdData.GetTrackInfo();

        MenuItemShowReadme.IsEnabled = game.ReadmeCreated;
        MenuItemOpenFolder.IsEnabled = !string.IsNullOrWhiteSpace(TextBoxOutputDirectory.Text) &&
                                       Directory.Exists(Path.Combine(TextBoxOutputDirectory.Text,
                                           Functions.OutputPath(game)));

        DataGridGameList.CollectionView.Refresh();
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
            await progress.Process(GameList.ToList(), Configuration);
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

    private void AddReadmes(string directory)
    {
        if (string.IsNullOrWhiteSpace(TextBoxOutputDirectory.Text)) return;
        if (!Directory.Exists(TextBoxOutputDirectory.Text)) return;

        var readmeList = Functions.GetReadmeFilesInDirectory(directory);
        foreach (var readme in readmeList)
        {
            var game = new Game();
            Functions.LoadExistingData(game, Configuration, readme);
            if (!GameList.Any(g => g.Id.Equals(game.Id))) GameList.Add(game);
        }

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
        Configuration.OutputDirectory = TextBoxOutputDirectory.Text;
        
        AddReadmes(TextBoxOutputDirectory.Text);
        DataGridGameList.CollectionView.Refresh();
        
        foreach (var game in GameList)
        {
            Functions.LoadExistingData(game, Configuration);
        }
        DataGridGameList.CollectionView.Refresh();
    }

    private void TextBoxOutputDirectory_OnTextInput(object? sender, TextInputEventArgs e)
    {
        var dir = e.Text;
        if (!Directory.Exists(dir)) return;
        Configuration.OutputDirectory = dir;
        foreach (var game in GameList)
        {
            Functions.LoadExistingData(game, Configuration);
        }
        DataGridGameList.CollectionView.Refresh();
        
        AddReadmes(dir);

        DataGridGameList.CollectionView.Refresh();
    }

    private async void MenuItemShowReadme_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataGridGameList.SelectedIndex < 0) return;
        if (string.IsNullOrWhiteSpace(TextBoxOutputDirectory.Text)) return;
        if (DataGridGameList?.SelectedItem is not Game game) return;
        var readmePath = Path.Combine(TextBoxOutputDirectory.Text, Functions.OutputPath(game), Constants.ReadmeFile);
        if (!File.Exists(readmePath)) return;
        await new ReadmePreviewWindow(readmePath, game.Title).ShowDialog(this);
    }

    private void DataGridGameList_OnAutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        switch (e.PropertyName)
        {
            case "ChdData":
                e.Column.IsVisible = false;
                break;
            case "CuePath":
                e.Column.Header = "Cue Path";
                break;
            case "ReadmeCreated":
                e.Column.Header = "Readme Created";
                break;
            case "ChdCreated":
                e.Column.Header = "CHD Created";
                break;
        }
    }

    private void MenuItemOpenFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataGridGameList.SelectedIndex < 0) return;
        if (string.IsNullOrWhiteSpace(TextBoxOutputDirectory.Text)) return;
        if (DataGridGameList?.SelectedItem is not Game game) return;
        var outputPath = Path.Combine(TextBoxOutputDirectory.Text, Functions.OutputPath(game));
        var launcher = GetTopLevel(this)?.Launcher;
        launcher?.LaunchDirectoryInfoAsync(new DirectoryInfo(outputPath));
    }

    private void CheckBox_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: not null } radio)
        {
            if (Enum.TryParse<ChdProcessing>(radio.Tag as string, out var processing))
                Configuration.ChdProcessing = processing;
        }
        Configuration.OutputDirectory = TextBoxOutputDirectory.Text ?? string.Empty;
        Configuration.OverwriteExistingReadmes = CheckBoxOverwriteReadme.IsChecked ?? false;
        Configuration.GetTitleFromCue = CheckBoxGetTitleFromCue.IsChecked ?? false;
        Configuration.GameIdAsChdName = CheckBoxGameIdAsChdName.IsChecked ?? false;
        Configuration.Save();
    }
}
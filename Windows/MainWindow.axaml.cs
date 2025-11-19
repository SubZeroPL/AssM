using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using AssM.Classes;
using AssM.Data;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using NLog;
using Octokit;
using ProductHeaderValue = Octokit.ProductHeaderValue;

namespace AssM.Windows;

public partial class MainWindow : Window
{
    private readonly Logger _logger;
    private ObservableCollection<Game> GameList { get; } = [];
    private Configuration Configuration { get; }

    public MainWindow()
    {
        Configuration = Configuration.Load() ?? new Configuration();
        LogManager.Setup().LoadConfiguration(builder =>
        {
            if (Configuration.EnableLogging)
            {
                builder.ForLogger().WriteToFile(Path.Combine(AppContext.BaseDirectory, Constants.LogName));
            }
            else
            {
                builder.ForLogger().WriteToNil();
            }
        });
        _logger = LogManager.GetCurrentClassLogger();
        InitializeComponent();
        _logger.Debug("Getting version info");
        var ver = GetVersion();
        if (ver != null)
        {
            _logger.Debug($"Version: {ver}");
            Title = $"AssM {ver.Major}.{ver.Minor}.{ver.Build}";
        }

        DataGridGameList.ItemsSource = GameList;
        TextTitle.AddHandler(TextInputEvent, TextTitle_OnTextInput, RoutingStrategies.Tunnel);
        TextDescription.AddHandler(TextInputEvent, TextDescription_OnTextInput, RoutingStrategies.Tunnel);
        TextBoxOutputDirectory.Text = Configuration.OutputDirectory;
        RadioButtonDontGenerate.IsChecked = Configuration.ChdProcessing == ChdProcessing.DontGenerate;
        RadioButtonGenerateMissing.IsChecked = Configuration.ChdProcessing == ChdProcessing.GenerateMissing;
        RadioButtonGenerateAll.IsChecked = Configuration.ChdProcessing == ChdProcessing.GenerateAll;
        CheckBoxOverwriteReadme.IsChecked = Configuration.OverwriteExistingReadmes;
        CheckBoxGetTitleFromCue.IsChecked = Configuration.GetTitleFromCue;
        CheckBoxGameIdAsChdName.IsChecked = Configuration.GameIdAsChdName;
        CheckBoxProcessModified.IsChecked = Configuration.ProcessOnlyModified;
        Closing += (_, _) =>
        {
            Configuration.OutputDirectory = TextBoxOutputDirectory.Text;
            Configuration.OverwriteExistingReadmes = CheckBoxOverwriteReadme.IsChecked.Value;
            Configuration.GetTitleFromCue = CheckBoxGetTitleFromCue.IsChecked.Value;
            Configuration.GameIdAsChdName = CheckBoxGameIdAsChdName.IsChecked.Value;
            Configuration.ProcessOnlyModified = CheckBoxProcessModified.IsChecked.Value;
            Configuration.Save();
        };
        if (!string.IsNullOrWhiteSpace(Configuration.OutputDirectory)) AddReadmes(Configuration.OutputDirectory);
        _ = CheckForNewVersion();
    }

    private static Version? GetVersion()
    {
        return Assembly.GetEntryAssembly()?.GetName().Version;
    }

    private async Task CheckForNewVersion()
    {
        _logger.Debug("Checking for new version");
        var currentVer = GetVersion();
        var ghClient = new GitHubClient(new ProductHeaderValue(Assembly.GetEntryAssembly()?.GetName().Name));
        var latestRelease = await ghClient.Repository.Release.GetLatest("SubZeroPL", "AssM");
        var tag = latestRelease.TagName.Replace("v", string.Empty);
        var versionPresent = Version.TryParse(tag, out var version);
        if (!versionPresent || version <= currentVer) return;
        _logger.Debug($"New version detected: {tag}");
        TextBlockUpdate.Text = $"New version: {version!.Major}.{version.Minor}.{version.Build}";
        ButtonUpdate.IsVisible = true;
        ButtonUpdate.Tag = latestRelease.Url;
    }

    private void AddButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var fpo = new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = "Select images",
            FileTypeFilter = [ new FilePickerFileType("All supported formats (*.cue, *.iso)") { Patterns = ["*.cue", "*.iso"] },
                new FilePickerFileType("CUE file (*.cue)") { Patterns = ["*.cue"] },
                new FilePickerFileType("ISO file (*.iso)") { Patterns = ["*.iso"] }]
        };
        var files = GetTopLevel(this)?.StorageProvider.OpenFilePickerAsync(fpo).GetAwaiter().GetResult() ??
                    new List<IStorageFile>();
        foreach (var file in files)
        {
            AddGame(file.Path.LocalPath);
        }

        DataGridGameList.CollectionView.Refresh();
    }

    private void AddGame(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return;
        var game = Functions.AddGameToList(imagePath, Configuration, GameList);
        if (game == null)
        {
            MessageBoxManager.GetMessageBoxStandard("Error",
                    $"Failed to add game to list from {imagePath}{Environment.NewLine}Id not present in image",
                    ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error, WindowStartupLocation.CenterOwner)
                .ShowWindowDialogAsync(this);
            return;
        }

        if (string.IsNullOrWhiteSpace(TextBoxOutputDirectory.Text)) return;
        Functions.LoadExistingData(game, Configuration);
        game.Modified = true;
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

    private void AddFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var fpo = new FolderPickerOpenOptions
        {
            Title = "Select folder with images"
        };

        var dirs = GetTopLevel(this)?.StorageProvider.OpenFolderPickerAsync(fpo).GetAwaiter().GetResult()
            .Select(d => d.Path.LocalPath)
            .ToList() ?? [];
        var progress = new AddFolderProgressWindow();
        _ = progress.ShowDialog(this);
        progress.Process(dirs, GameList, Configuration, errors =>
        {
            progress.Close();
            DataGridGameList.CollectionView.Refresh();
            if (errors.Count > 0)
            {
                MessageBoxManager.GetMessageBoxStandard("Error",
                        string.Join(Environment.NewLine, errors),
                        ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error, WindowStartupLocation.CenterOwner)
                    .ShowWindowDialogAsync(this);
            }
        });
    }

    private void DeleteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataGridGameList.SelectedItem is not Game game) return;
        GameList.Remove(game);
    }

    private void DataGridGameList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataGridGameList?.SelectedItem is not Game game) return;
        Dispatcher.UIThread.InvokeAsync(() =>
        {
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
        });
    }

    private void TextDescription_OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (DataGridGameList.SelectedItem is not Game game) return;
        game.Description = TextDescription.Text + e.Text;
        game.Modified = true;
        DataGridGameList.CollectionView.Refresh();
    }

    private void TextTitle_OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (DataGridGameList.SelectedItem is not Game game) return;
        game.Title = TextTitle.Text + e.Text;
        game.Modified = true;
        DataGridGameList.CollectionView.Refresh();
    }

    private void StartProcessingButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TextBoxOutputDirectory.Text))
        {
            MessageBoxManager.GetMessageBoxStandard("Error", "Please provide a valid output directory",
                    ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error, WindowStartupLocation.CenterOwner)
                .ShowWindowDialogAsync(this).GetAwaiter().GetResult();
            return;
        }

        if (GameList.ToList().Count == 0)
        {
            MessageBoxManager.GetMessageBoxStandard("Error", "Please provide at least one game", ButtonEnum.Ok,
                    MsBox.Avalonia.Enums.Icon.Error, WindowStartupLocation.CenterOwner).ShowWindowDialogAsync(this)
                .GetAwaiter().GetResult();
            return;
        }

        var progress = new ProgressWindow();
        _ = progress.ShowDialog(this);
        try
        {
            progress.Process(GameList.ToList(), Configuration, () =>
            {
                progress.Close();
                MessageBoxManager.GetMessageBoxStandard("Processing finished", "Processing finished",
                        ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Success, WindowStartupLocation.CenterOwner)
                    .ShowWindowDialogAsync(this);
                DataGridGameList.CollectionView.Refresh();
            });
        }
        catch (Exception ex)
        {
            MessageBoxManager.GetMessageBoxStandard(progress.Title ?? "Error", ex.Message, ButtonEnum.Ok,
                MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this).GetAwaiter().GetResult();
        }
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

    private void SelectOutputFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var fpo = new FolderPickerOpenOptions
        {
            Title = "Select output folder",
            AllowMultiple = false
        };
        var dirs = GetTopLevel(this)?.StorageProvider.OpenFolderPickerAsync(fpo).GetAwaiter().GetResult() ??
                   new List<IStorageFolder>();
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

        AddReadmes(dir);

        DataGridGameList.CollectionView.Refresh();
    }

    private void MenuItemShowReadme_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataGridGameList.SelectedIndex < 0) return;
        if (string.IsNullOrWhiteSpace(TextBoxOutputDirectory.Text)) return;
        if (DataGridGameList?.SelectedItem is not Game game) return;
        var readmePath = Path.Combine(TextBoxOutputDirectory.Text, Functions.OutputPath(game), Constants.ReadmeFile);
        if (!File.Exists(readmePath)) return;
        new ReadmePreviewWindow(readmePath, game.Title).ShowDialog(this);
    }

    private void DataGridGameList_OnAutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        switch (e.PropertyName)
        {
            case "ChdData":
            case "Modified":
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
        Configuration.ProcessOnlyModified = CheckBoxProcessModified.IsChecked ?? false;
        Configuration.OverwriteExistingReadmes = CheckBoxOverwriteReadme.IsChecked ?? false;
        Configuration.GetTitleFromCue = CheckBoxGetTitleFromCue.IsChecked ?? false;
        Configuration.GameIdAsChdName = CheckBoxGameIdAsChdName.IsChecked ?? false;
        Configuration.Save();
        RefreshList();
    }

    private void RefreshList()
    {
        foreach (var game in GameList)
        {
            Functions.LoadExistingData(game, Configuration);
        }

        DataGridGameList.CollectionView.Refresh();
    }

    private void ButtonDiscord_OnClick(object? sender, RoutedEventArgs e)
    {
        var launcher = GetTopLevel(this)?.Launcher;
        launcher?.LaunchUriAsync(new Uri(Constants.DiscordLink));
    }

    private void ButtonHelp_OnClick(object? sender, RoutedEventArgs e)
    {
        var launcher = GetTopLevel(this)?.Launcher;
        launcher?.LaunchUriAsync(new Uri(Constants.GithubLink));
    }

    private void DataGridGameList_OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is not Game game) return;
        e.Row.FontStyle = game.Modified ? FontStyle.Italic : FontStyle.Normal;
        MenuItemShowReadme.IsEnabled = game.ReadmeCreated;
        MenuItemOpenFolder.IsEnabled = !string.IsNullOrWhiteSpace(TextBoxOutputDirectory.Text) &&
                                       Directory.Exists(Path.Combine(TextBoxOutputDirectory.Text,
                                           Functions.OutputPath(game)));
    }

    private void MainWindow_OnActivated(object? sender, EventArgs e)
    {
        DataGridGameList.CollectionView.Refresh();
    }

    private void ButtonUpdate_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ButtonUpdate.Tag is not string tag) return;
        var launcher = GetTopLevel(this)?.Launcher;
        launcher?.LaunchUriAsync(new Uri(tag));
    }

    private void DataGridGameList_OnKeyUp(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Delete when e.KeyModifiers == KeyModifiers.Control:
                ClearButton_OnClick(sender, e);
                break;
            case Key.Delete:
                DeleteButton_OnClick(sender, e);
                break;
            case Key.Insert when e.KeyModifiers == KeyModifiers.Control:
                AddFolderButton_OnClick(sender, e);
                break;
            case Key.Insert:
                AddButton_OnClick(sender, e);
                break;
        }

        e.Handled = true;
    }
}
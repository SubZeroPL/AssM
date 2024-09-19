using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using AssM.Classes;
using AssM.Data;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AssM.Windows;

public partial class AddFolderProgressWindow : Window
{
    private bool _cancelled;

    public AddFolderProgressWindow()
    {
        InitializeComponent();
    }

    public async Task Process(List<string> dirs,ObservableCollection<Game> gameList, Configuration configuration)
    {
        var cueFiles = new List<string>();
        foreach (var dir in dirs)
        {
            if (_cancelled) return;
            LabelFolderName.Content = dir;
            await Task.Run(() => cueFiles.AddRange(Functions.GetCueFilesInDirectory(dir)));
        }

        foreach (var cueFile in cueFiles)
        {
            if (_cancelled) return;
            await Task.Run(() => Functions.AddGameToList(cueFile, configuration, gameList));
        }

        if (string.IsNullOrWhiteSpace(configuration.OutputDirectory)) return;
        foreach (var game in gameList)
        {
            if (_cancelled) return;
            Functions.LoadExistingData(game, configuration);
        }
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _cancelled = true;
    }

    private void Window_OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _cancelled = true;
        if (!e.IsProgrammatic) e.Cancel = true;
    }
}
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using AssM.Classes;
using AssM.Data;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AssM.Windows;

internal class AddFolderProgressUpdateObject
{
    public string? Dir { get; init; }
    public int? Count { get; init; }
    public int? Index { get; init; }
    public int Step { get; init; }
}

public partial class AddFolderProgressWindow : Window
{
    private readonly BackgroundWorker _worker;

    public AddFolderProgressWindow()
    {
        InitializeComponent();
        _worker = new BackgroundWorker();
        _worker.WorkerReportsProgress = true;
        _worker.WorkerSupportsCancellation = true;
        _worker.ProgressChanged += WorkerOnProgressChanged;
    }

    private void WorkerOnProgressChanged(object? sender, ProgressChangedEventArgs e)
    {
        if (e.UserState is not AddFolderProgressUpdateObject updateObject) return;
        LabelStep.Content = Constants.AddFolderSteps[updateObject.Step];
        if (updateObject.Step == 1)
        {
            LabelStep.Content = string.Format(Constants.AddFolderSteps[updateObject.Step], updateObject.Index,
                updateObject.Count);
            LabelFolderName.Content = updateObject.Dir;
        }
    }

    public void Process(List<string> dirs, ObservableCollection<Game> gameList, Configuration configuration,
        Action<List<string>> finishedCallback)
    {
        var cueFiles = new List<string>();
        var errors = new List<string>();
        _worker.DoWork += (_, _) =>
        {
            var index = 0;
            foreach (var dir in dirs)
            {
                if (_worker.CancellationPending) return;
                var progress = index / dirs.Count * 100;
                _worker.ReportProgress(progress,
                    new AddFolderProgressUpdateObject { Dir = dir, Index = index++, Count = dirs.Count, Step = 1 });
                cueFiles.AddRange(Functions.GetCueIsoFilesInDirectory(dir));
            }

            index = 0;
            foreach (var cueFile in cueFiles)
            {
                if (_worker.CancellationPending) return;
                var progress = index / cueFiles.Count * 100;
                _worker.ReportProgress(progress,
                    new AddFolderProgressUpdateObject
                        { Dir = cueFile, Index = index++, Count = cueFiles.Count, Step = 2 });
                var game = Functions.AddGameToList(cueFile, configuration, gameList);
                if (game == null)
                {
                    errors.Add(
                        $"Failed to add game to list from {cueFile}{Environment.NewLine}Id not present in image");
                }
            }

            index = 0;
            if (string.IsNullOrWhiteSpace(configuration.OutputDirectory)) return;
            foreach (var game in gameList)
            {
                if (_worker.CancellationPending) return;
                var progress = index / gameList.Count * 100;
                _worker.ReportProgress(progress,
                    new AddFolderProgressUpdateObject
                        { Dir = game.Title, Index = index++, Count = gameList.Count, Step = 3 });
                Functions.LoadExistingData(game, configuration);
            }
        };

        _worker.RunWorkerCompleted += (_, _) => finishedCallback.Invoke(errors);

        _worker.RunWorkerAsync();
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _worker.CancelAsync();
    }

    private void Window_OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _worker.CancelAsync();
        if (!e.IsProgrammatic) e.Cancel = true;
    }
}
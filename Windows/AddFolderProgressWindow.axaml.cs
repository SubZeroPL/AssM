using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using AssM.Classes;
using AssM.Data;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AssM.Windows;

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
        if (e.UserState is string dir)
            LabelFolderName.Content = dir;
    }

    public void Process(List<string> dirs, ObservableCollection<Game> gameList, Configuration configuration, Action finishedCallback)
    {
        var cueFiles = new List<string>();
        _worker.DoWork += (_, _) =>
        {
            foreach (var dir in dirs)
            {
                if (_worker.CancellationPending) return;
                _worker.ReportProgress(0, dir);
                cueFiles.AddRange(Functions.GetCueFilesInDirectory(dir));
            }

            foreach (var cueFile in cueFiles)
            {
                if (_worker.CancellationPending) return;
                Functions.AddGameToList(cueFile, configuration, gameList);
            }

            if (string.IsNullOrWhiteSpace(configuration.OutputDirectory)) return;
            foreach (var game in gameList)
            {
                if (_worker.CancellationPending) return;
                Functions.LoadExistingData(game, configuration);
            }
        };
        
        _worker.RunWorkerCompleted += (_, _) => finishedCallback.Invoke();
        
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
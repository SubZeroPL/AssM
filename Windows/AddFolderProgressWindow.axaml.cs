using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using AssM.Classes;
using AssM.Data;
using Avalonia.Controls;

namespace AssM.Windows;

public partial class AddFolderProgressWindow : Window
{
    public AddFolderProgressWindow()
    {
        InitializeComponent();
    }

    public async Task Process(List<string> dirs, ObservableCollection<Game> gameList, string? ouputFolder)
    {
        var cueFiles = new List<string>();
        foreach (var dir in dirs)
        {
            LabelFolderName.Content = dir;
            await Task.Run(() => cueFiles.AddRange(Functions.GetCueFilesInDirectory(dir)));
        }

        foreach (var cueFile in cueFiles)
        {
            await Task.Run(() => Functions.AddGameToList(cueFile, gameList));
        }

        if (ouputFolder == null) return;
        foreach (var game in gameList)
        {
            Functions.LoadExistingData(ouputFolder, game);    
        }
    }
}
using System.IO;
using Avalonia.Controls;

namespace AssM.Windows;

public partial class ReadmePreviewWindow : Window
{
    public ReadmePreviewWindow()
    {
        InitializeComponent();
    }

    public ReadmePreviewWindow(string readmePath, string gameTitle)
    {
        InitializeComponent();
        Viewer.Markdown = File.ReadAllText(readmePath);
        LabelGame.Content = gameTitle;
    }
}
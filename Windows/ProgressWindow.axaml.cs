using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using AssM.Classes;
using AssM.Data;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AssM.Windows;

public partial class ProgressWindow : Window
{
    private bool _cancelled;
    private Configuration _configuration;

    public ProgressWindow()
    {
        InitializeComponent();
        _configuration = new Configuration();
    }

    public async Task Process(List<Game> gameList, Configuration configuration)
    {
        _configuration = configuration;

        for (var i = 0; i < gameList.Count; i++)
        {
            var game = gameList.ElementAt(i);
            if (_configuration.ProcessOnlyModified && !game.Modified) continue;
            LabelGameTitle.Content = game.Title;
            LabelAll.Content = gameList.Count;
            LabelIndex.Content = i + 1;
            var step = 1;
            LabelStep.Content = Constants.Steps[step++];
            await ConvertSingleChd(game, UpdateProgress);
            LabelStep.Content = Constants.Steps[step++];
            await CalculateTracksMd5(game, UpdateProgress);
            LabelStep.Content = Constants.Steps[step++];
            GetChdManInfo(game);
            LabelStep.Content = Constants.Steps[step];
            GenerateReadme(game);
            game.Modified = false;
        }
    }

    private void UpdateProgress(double progress)
    {
        Dispatcher.UIThread.InvokeAsync(() => { ProgressBarProgress.Value = progress; });
    }

    private async Task ConvertSingleChd(Game game, Action<double> reportProgress)
    {
        if (_cancelled) return;
        if (_configuration.ChdProcessing == ChdProcessing.DontGenerate) return;
        var chdFile = Functions.GetChdName(game, _configuration);
        var chdPath = Path.Combine(_configuration.OutputDirectory, Functions.OutputPath(game), chdFile);
        if (File.Exists(chdPath) && _configuration.ChdProcessing != ChdProcessing.GenerateAll) return;
        if (string.IsNullOrWhiteSpace(game.CuePath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(chdPath) ?? string.Empty);

        var chdmanConvert = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Constants.ChdMan,
                Arguments = string.Format(Constants.ChdManConvert, game.CuePath, chdPath),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        if (_cancelled) return;
        await Task.Run(async () =>
        {
            chdmanConvert.Start();

            while (!chdmanConvert.StandardError.EndOfStream)
            {
                if (_cancelled)
                {
                    if (!chdmanConvert.HasExited) chdmanConvert.Kill();
                    await chdmanConvert.WaitForExitAsync();
                    File.Delete(chdPath);
                    return;
                }

                var line = await chdmanConvert.StandardError.ReadLineAsync() ?? string.Empty;
                if (line.Contains(Constants.ChdManComplete))
                {
                    reportProgress(100.0);
                    break;
                }

                if (line.Contains("Error"))
                {
                    reportProgress(100.0);
                    throw new ProcessingException(line);
                }

                var matches = Constants.ChdManProgressRegex().Matches(line);
                var progress = double.Parse(matches[0].Groups[1].Value, CultureInfo.InvariantCulture);
                reportProgress(progress);
            }

            await chdmanConvert.WaitForExitAsync();

            if (chdmanConvert.ExitCode != 0)
            {
                if (_cancelled) return;
                var output = new List<string>();
                while (!chdmanConvert.StandardOutput.EndOfStream)
                {
                    if (_cancelled) return;
                    var line = await chdmanConvert.StandardOutput.ReadLineAsync() ?? string.Empty;
                    output.Add(line);
                }

                throw new ProcessingException(string.Join(Environment.NewLine, output));
            }

            game.ChdCreated = true;

            chdmanConvert.Dispose();
        });
    }

    private async Task CalculateTracksMd5(Game game, Action<double> reportProgress)
    {
        reportProgress(0.0);
        if (_cancelled) return;
        var readmePath = Path.Combine(_configuration.OutputDirectory, Functions.OutputPath(game), Constants.ReadmeFile);
        if (File.Exists(readmePath) && !_configuration.OverwriteExistingReadmes) return;
        var cuefile = game.CuePath;
        if (string.IsNullOrWhiteSpace(cuefile)) return;
        var lines = File.ReadLines(cuefile);
        List<string> bins = [];
        bins.AddRange(from line in lines
            where line.StartsWith("FILE")
            select Constants.BinFileRegex().Matches(line)
            into matches
            select matches[0].Groups[1].Value
            into bin
            select Path.Combine(Path.GetDirectoryName(cuefile)!, bin));

        if (_cancelled) return;
        var md5 = MD5.Create();
        if (game.ChdData.TrackInfo.Count != 0) game.ChdData.TrackInfo.Clear();
        for (var i = 0; i < bins.Count; i++)
        {
            if (_cancelled) return;
            var bin = bins[i];
            await using var fs = File.OpenRead(bin);
            var hashArray = await md5.ComputeHashAsync(fs);
            var hash = string.Join(string.Empty, hashArray.Select(b => b.ToHexString(2)));
            game.ChdData.TrackInfo.Add((i + 1, hash));
            reportProgress((double)i / bins.Count * 100.0);
        }

        reportProgress(100.0);
    }

    private void GetChdManInfo(Game game)
    {
        if (_cancelled) return;
        var chdFile = Functions.GetChdName(game, _configuration);
        var chdPath = Path.Combine(_configuration.OutputDirectory, Functions.OutputPath(game), chdFile);
        Functions.LoadChdManInfo(chdPath, game);
    }

    private void GenerateReadme(Game game)
    {
        if (_cancelled) return;
        var readmePath = Path.Combine(_configuration.OutputDirectory, Functions.OutputPath(game), Constants.ReadmeFile);
        if (File.Exists(readmePath) && !_configuration.OverwriteExistingReadmes) return;
        var dir = Path.GetDirectoryName(readmePath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        var template = File.ReadAllText(Constants.ReadmeTemplate);
        template = template.Replace(Constants.ReadmeGameTitle, game.Title);
        template = template.Replace(Constants.ReadmeGameId, game.Id);
        template = template.Replace(Constants.ReadmeChdHash, game.ChdData.DataSha1Hash);
        var binHashes = game.ChdData.GetTrackInfo();
        template = template.Replace(Constants.ReadmeBinHashes, binHashes);
        template = template.Replace(Constants.ReadmeGameDescription, game.Description);
        File.WriteAllText(readmePath, template);
        game.ReadmeCreated = true;
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
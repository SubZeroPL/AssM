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
using NLog;

namespace AssM.Windows;

public partial class ProgressWindow : Window
{
    private readonly Logger _logger;
    private bool _cancelled;
    private Configuration _configuration;

    public ProgressWindow()
    {
        _logger = LogManager.GetCurrentClassLogger();
        InitializeComponent();
        _configuration = new Configuration();
    }

    public async Task Process(List<Game> gameList, Configuration configuration)
    {
        _logger.Debug("Starting processing");
        _configuration = configuration;
        _logger.Debug($"Configuration: {_configuration}");

        for (var i = 0; i < gameList.Count; i++)
        {
            UpdateProgress(0.0);
            var game = gameList.ElementAt(i);
            _logger.Debug($"Processing game {i + 1}: {game.Title}");
            if (_configuration.ProcessOnlyModified && !game.Modified)
            {
                _logger.Debug($"Skipping game {i}: Not modified");
                continue;
            }
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
            LabelStep.Content = Constants.Steps[step++];
            GenerateReadme(game);
            LabelStep.Content = Constants.Steps[step];
            await Functions.ProcessJson(_configuration, game, UpdateProgress);
            game.Modified = false;
            _logger.Debug($"Finished processing game {i}: {game.Title}");
        }

        _logger.Debug("Finished processing");
    }

    private void UpdateProgress(double progress)
    {
        Dispatcher.UIThread.InvokeAsync(() => { ProgressBarProgress.Value = progress; });
    }

    private async Task ConvertSingleChd(Game game, Action<double> reportProgress)
    {
        _logger.Debug($"Converting game {game.Title} to CHD");
        if (_cancelled) return;
        if (_configuration.ChdProcessing == ChdProcessing.DontGenerate) return;
        var chdFile = Functions.GetChdName(game, _configuration);
        _logger.Debug($"CHD file: {chdFile}");
        var chdPath = Path.Combine(_configuration.OutputDirectory, Functions.OutputPath(game), chdFile);
        _logger.Debug($"CHD path: {chdPath}");
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
        _logger.Debug("Starting conversion task");
        await Task.Run(async () =>
        {
            _logger.Debug($"Executing chdman, cmd line: {chdmanConvert.StartInfo.Arguments}");
            chdmanConvert.Start();

            while (!chdmanConvert.StandardError.EndOfStream)
            {
                if (_cancelled)
                {
                    _logger.Debug("Process canceled, killing chdman");
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
                    _logger.Debug($"chdman error: {line}");
                    reportProgress(100.0);
                    throw new ProcessingException(line);
                }

                var matches = Constants.ChdManProgressRegex().Matches(line);
                var progress = double.Parse(matches[0].Groups[1].Value, CultureInfo.InvariantCulture);
                reportProgress(progress);
                _logger.Debug("chdman conversion finished");
            }

            await chdmanConvert.WaitForExitAsync();

            if (chdmanConvert.ExitCode != 0)
            {
                _logger.Debug($"chdman conversion finished with error: {chdmanConvert.ExitCode}");
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
        _logger.Debug("Finished conversion");
    }

    private async Task CalculateTracksMd5(Game game, Action<double> reportProgress)
    {
        _logger.Debug("Calculating tracks md5");
        reportProgress(0.0);
        if (_cancelled) return;
        var readmePath = Path.Combine(_configuration.OutputDirectory, Functions.OutputPath(game), Constants.ReadmeFile);
        _logger.Debug($"Readme path: {readmePath}");
        if (File.Exists(readmePath) && !_configuration.OverwriteExistingReadmes) return;
        var cuefile = game.CuePath;
        _logger.Debug($"Cue path: {cuefile}");
        if (string.IsNullOrWhiteSpace(cuefile)) return;
        _logger.Debug("Adding bin files from cue");
        var lines = File.ReadLines(cuefile);
        List<string> bins = [];
        bins.AddRange(from line in lines
            where line.StartsWith("FILE")
            select Constants.BinFileRegex().Matches(line)
            into matches
            select matches[0].Groups[1].Value
            into bin
            select Path.Combine(Path.GetDirectoryName(cuefile)!, bin));
        _logger.Debug($"Bin files from cue: {string.Join(',', bins)}");

        if (_cancelled) return;
        var md5 = MD5.Create();
        if (game.ChdData.TrackInfo.Count != 0) game.ChdData.TrackInfo.Clear();
        for (var i = 0; i < bins.Count; i++)
        {
            if (_cancelled) return;
            var bin = bins[i];
            _logger.Debug($"Calculating md5 for track {i+1}: {bin}");
            await using var fs = File.OpenRead(bin);
            var hashArray = await md5.ComputeHashAsync(fs);
            var hash = string.Join(string.Empty, hashArray.Select(b => b.ToHexString(2)));
            game.ChdData.TrackInfo.Add((i + 1, hash));
            reportProgress((double)i / bins.Count * 100.0);
            _logger.Debug("Done");
        }

        reportProgress(100.0);
        _logger.Debug("Calculating tracks md5 finished");
    }

    private void GetChdManInfo(Game game)
    {
        _logger.Debug($"Getting chdman info for {game.Title}");
        if (_cancelled) return;
        var chdFile = Functions.GetChdName(game, _configuration);
        var chdPath = Path.Combine(_configuration.OutputDirectory, Functions.OutputPath(game), chdFile);
        Functions.LoadChdManInfo(chdPath, game);
    }

    private void GenerateReadme(Game game)
    {
        _logger.Debug($"Generating readme for {game.Title}");
        if (_cancelled) return;
        var readmePath = Path.Combine(_configuration.OutputDirectory, Functions.OutputPath(game), Constants.ReadmeFile);
        _logger.Debug($"Readme path: {readmePath}");
        if (File.Exists(readmePath) && !_configuration.OverwriteExistingReadmes) return;
        var dir = Path.GetDirectoryName(readmePath);
        _logger.Debug($"Readme dir: {dir}");
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
        _logger.Debug("Readme generated");
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _logger.Debug("Cancel button clicked");
        _cancelled = true;
    }

    private void Window_OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _logger.Debug("Window is closing");
        _cancelled = true;
        if (!e.IsProgrammatic) e.Cancel = true;
    }
}
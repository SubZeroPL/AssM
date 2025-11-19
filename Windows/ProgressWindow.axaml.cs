using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AssM.Classes;
using AssM.Data;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NLog;
using Org.BouncyCastle.Crypto.Digests;

namespace AssM.Windows;

internal class ProgressUpdateObject
{
    public string? Title { get; init; }
    public int? Count { get; init; }
    public int? Index { get; init; }
    public int? Step { get; init; }
    public int? BinNo { get; init; }
    public int? BinCount { get; init; }
}

public partial class ProgressWindow : Window
{
    private readonly Logger _logger;
    private Configuration _configuration;
    private readonly BackgroundWorker _worker;

    public ProgressWindow()
    {
        _logger = LogManager.GetCurrentClassLogger();
        InitializeComponent();
        _configuration = new Configuration();
        _worker = new BackgroundWorker();
        _worker.WorkerReportsProgress = true;
        _worker.WorkerSupportsCancellation = true;
        _worker.ProgressChanged += UpdateProgress;
    }

    public void Process(List<Game> gameList, Configuration configuration, Action finishedCallback)
    {
        _logger.Debug("Starting processing");
        _configuration = configuration;
        _logger.Debug($"Configuration: {_configuration}");

        _worker.DoWork += (_, _) =>
        {
            for (var i = 0; i < gameList.Count; i++)
            {
                var progress = i / gameList.Count * 100;
                _worker.ReportProgress(progress);
                if (_worker.CancellationPending)
                    break;
                var game = gameList.ElementAt(i);
                _logger.Debug($"Processing game {i + 1}: {game.Title}");
                if (_configuration.ProcessOnlyModified && !game.Modified)
                {
                    _logger.Debug($"Skipping game {i + 1}: Not modified");
                    return;
                }

                var step = 1;
                _worker.ReportProgress(progress,
                    new ProgressUpdateObject
                        { Title = game.Title, Count = gameList.Count, Step = step++, Index = i + 1 });
                ConvertSingleChd(game);
                _worker.ReportProgress(progress,
                    new ProgressUpdateObject { Step = step++, BinNo = 1, BinCount = game.ChdData.TrackInfo.Count });
                CalculateTracksMd5(game);
                _worker.ReportProgress(progress, new ProgressUpdateObject { Step = step++ });
                GetChdManInfo(game);
                _worker.ReportProgress(progress, new ProgressUpdateObject { Step = step++ });
                GenerateReadme(game);
                _worker.ReportProgress(progress, new ProgressUpdateObject { Step = step });
                Functions.ProcessJson(_configuration, game, d => { _worker.ReportProgress((int)d); });
                game.Modified = false;
                _logger.Debug($"Finished processing game {i + 1}: {game.Title}");
            }
        };

        _worker.RunWorkerCompleted += (_, _) =>
        {
            _logger.Debug("Finished processing");
            finishedCallback.Invoke();
        };

        _worker.RunWorkerAsync();
    }

    private void UpdateProgress(object? sender, ProgressChangedEventArgs progressChangedEventArgs)
    {
        ProgressBarProgress.Value = progressChangedEventArgs.ProgressPercentage;
        if (progressChangedEventArgs.UserState is not ProgressUpdateObject progress) return;
        LabelGameTitle.Content = progress.Title ?? LabelGameTitle.Content;
        LabelAll.Content = progress.Count ?? LabelAll.Content;
        LabelIndex.Content = progress.Index ?? LabelIndex.Content;
        LabelStep.Content = progress.Step != null ? Constants.Steps[progress.Step ?? 0] : LabelStep.Content;
        if (progress is { Step: 2, BinNo: not null })
        {
            LabelStep.Content = string.Format(Constants.Steps[progress.Step ?? 2], progress.BinNo, progress.BinCount);
        }
    }

    private void ConvertSingleChd(Game game)
    {
        _logger.Debug($"Converting game {game.Title} to CHD");
        if (_worker.CancellationPending) return;
        if (_configuration.ChdProcessing == ChdProcessing.DontGenerate) return;
        var chdFile = Functions.GetChdName(game, _configuration);
        _logger.Debug($"CHD file: {chdFile}");
        var chdPath = Path.Combine(_configuration.OutputDirectory, Functions.OutputPath(game), chdFile);
        _logger.Debug($"CHD path: {chdPath}");
        if (File.Exists(chdPath) && _configuration.ChdProcessing != ChdProcessing.GenerateAll) return;
        if (string.IsNullOrWhiteSpace(game.ImagePath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(chdPath) ?? string.Empty);

        var chdmanConvert = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Constants.ChdMan,
                Arguments = string.Format(Constants.ChdManConvert, game.ImagePath, chdPath),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        if (_worker.CancellationPending) return;
        _logger.Debug("Starting conversion task");

        _logger.Debug($"Executing chdman, cmd line: {chdmanConvert.StartInfo.Arguments}");
        chdmanConvert.Start();

        while (!chdmanConvert.StandardError.EndOfStream)
        {
            if (_worker.CancellationPending)
            {
                _logger.Debug("Process canceled, killing chdman");
                if (!chdmanConvert.HasExited) chdmanConvert.Kill();
                chdmanConvert.WaitForExit();
                File.Delete(chdPath);
                return;
            }

            var line = chdmanConvert.StandardError.ReadLine() ?? string.Empty;
            if (line.Contains(Constants.ChdManComplete))
            {
                _worker.ReportProgress(100);
                break;
            }

            if (line.Contains("Error"))
            {
                _logger.Debug($"chdman error: {line}");
                _worker.ReportProgress(100);
                throw new ProcessingException(line);
            }

            var matches = Constants.ChdManProgressRegex().Matches(line);
            var progress = double.Parse(matches[0].Groups[1].Value, CultureInfo.InvariantCulture);
            _worker.ReportProgress((int)Math.Round(progress));
            _logger.Debug("chdman conversion finished");
        }

        chdmanConvert.WaitForExit();

        if (chdmanConvert.ExitCode != 0)
        {
            _logger.Debug($"chdman conversion finished with error: {chdmanConvert.ExitCode}");
            if (_worker.CancellationPending) return;
            var output = new List<string>();
            while (!chdmanConvert.StandardOutput.EndOfStream)
            {
                if (_worker.CancellationPending) return;
                var line = chdmanConvert.StandardOutput.ReadLine() ?? string.Empty;
                output.Add(line);
            }

            throw new ProcessingException(string.Join(Environment.NewLine, output));
        }

        game.ChdCreated = true;

        chdmanConvert.Dispose();

        _logger.Debug("Finished conversion");
    }

    private void CalculateTracksMd5(Game game)
    {
        _logger.Debug("Calculating tracks md5");
        _worker.ReportProgress(0);
        if (_worker.CancellationPending) return;
        var readmePath = Path.Combine(_configuration.OutputDirectory, Functions.OutputPath(game), Constants.ReadmeFile);
        _logger.Debug($"Readme path: {readmePath}");
        if (File.Exists(readmePath) && !_configuration.OverwriteExistingReadmes) return;
        var imagefile = game.ImagePath;
        List<string> bins = [];
        if (Functions.IsIso(game))
        {
            _logger.Debug($"Iso path: {imagefile}");
            if (string.IsNullOrWhiteSpace(imagefile)) return;
            bins.Add(imagefile);
            _logger.Debug($"Iso file: {string.Join(',', bins)}");
        }
        else
        {
            _logger.Debug($"Cue path: {imagefile}");
            if (string.IsNullOrWhiteSpace(imagefile)) return;
            _logger.Debug("Adding bin files from cue");
            var lines = File.ReadLines(imagefile);
            bins.AddRange(from line in lines
                where line.StartsWith("FILE")
                select Constants.BinFileRegex().Matches(line)
                into matches
                select matches[0].Groups[1].Value
                into bin
                select Path.Combine(Path.GetDirectoryName(imagefile)!, bin));
            _logger.Debug($"Bin files from cue: {string.Join(',', bins)}");
        }

        if (_worker.CancellationPending) return;
        if (game.ChdData.TrackInfo.Count != 0) game.ChdData.TrackInfo.Clear();
        for (var i = 0; i < bins.Count; i++)
        {
            if (_worker.CancellationPending) return;
            var bin = bins[i];
            _logger.Debug($"Calculating md5 for track {i + 1}: {bin}");
            using var fs = File.OpenRead(bin);
            var hash = CalculateMd5HashFromStream(fs);
            game.ChdData.TrackInfo.Add((i + 1, hash));
            _worker.ReportProgress(i / bins.Count * 100,
                new ProgressUpdateObject { Step = 2, BinNo = i + 1, BinCount = bins.Count });
            _logger.Debug("Done");
        }

        _worker.ReportProgress(100);
        _logger.Debug("Calculating tracks md5 finished");
    }

    private string CalculateMd5HashFromStream(Stream inputStream)
    {
        var md5 = new MD5Digest();
        var buffer = new byte[4096]; // Use a buffer for efficient reading

        int bytesRead;
        while ((bytesRead = inputStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            md5.BlockUpdate(buffer, 0, bytesRead);
            _worker.ReportProgress((int)Math.Round((double)inputStream.Position / inputStream.Length * 100.0));
        }

        var hashBytes = new byte[md5.GetDigestSize()];
        md5.DoFinal(hashBytes, 0);

        var sb = new StringBuilder();
        foreach (var t in hashBytes)
        {
            sb.Append(t.ToString("x2"));
        }

        return sb.ToString().ToUpperInvariant();
    }

    private void GetChdManInfo(Game game)
    {
        _logger.Debug($"Getting chdman info for {game.Title}");
        if (_worker.CancellationPending) return;
        var chdFile = Functions.GetChdName(game, _configuration);
        var chdPath = Path.Combine(_configuration.OutputDirectory, Functions.OutputPath(game), chdFile);
        Functions.LoadChdManInfo(chdPath, game);
    }

    private void GenerateReadme(Game game)
    {
        _logger.Debug($"Generating readme for {game.Title}");
        if (_worker.CancellationPending) return;
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
        _worker.CancelAsync();
    }

    private void Window_OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _logger.Debug("Window is closing");
        _worker.CancelAsync();
        if (!e.IsProgrammatic) e.Cancel = true;
    }
}
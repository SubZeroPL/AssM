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
using Avalonia.Threading;

namespace AssM.Windows;

public partial class ProgressWindow : Window
{
    public ProgressWindow()
    {
        InitializeComponent();
    }

    public async Task Process(List<Game> gameList, string outputPath, bool overwriteChds, bool overwriteReadmes)
    {
        for (var i = 0; i < gameList.Count; i++)
        {
            var game = gameList.ElementAt(i);
            LabelGameTitle.Content = game.Title;
            LabelAll.Content = gameList.Count;
            LabelIndex.Content = i + 1;
            var step = 1;
            LabelStep.Content = Constants.Steps[step++];
            await ConvertSingleChd(game, outputPath, overwriteChds, UpdateProgress);
            LabelStep.Content = Constants.Steps[step++];
            await CalculateTracksMd5(game, outputPath, overwriteReadmes, UpdateProgress);
            LabelStep.Content = Constants.Steps[step++];
            GetChdManInfo(game, outputPath);
            LabelStep.Content = Constants.Steps[step];
            GenerateReadme(game, outputPath, overwriteReadmes);
        }
    }

    private void UpdateProgress(double progress)
    {
        Dispatcher.UIThread.InvokeAsync(() => { ProgressBarProgress.Value = progress; });
    }

    private static async Task ConvertSingleChd(Game game, string outputPathRoot, bool overwriteChds,
        Action<double> reportProgress)
    {
        var chdFile = Path.GetFileName(Path.ChangeExtension(game.CuePath, "chd"));
        var chdPath = Path.Combine(outputPathRoot, Functions.OutputPath(game), chdFile);
        if (File.Exists(chdPath) && !overwriteChds) return;
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
        await Task.Run(async () =>
        {
            chdmanConvert.Start();

            while (!chdmanConvert.StandardError.EndOfStream)
            {
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

            // await chdmanConvert.WaitForExitAsync();

            if (chdmanConvert.ExitCode != 0)
            {
                var output = new List<string>();
                while (!chdmanConvert.StandardOutput.EndOfStream)
                {
                    var line = await chdmanConvert.StandardOutput.ReadLineAsync() ?? string.Empty;
                    output.Add(line);
                }

                throw new ProcessingException(string.Join(Environment.NewLine, output));
            }

            game.ChdCreated = true;

            chdmanConvert.Dispose();
        });
    }

    private static async Task CalculateTracksMd5(Game game, string outputPathRoot, bool overwriteReadmes,
        Action<double> reportProgress)
    {
        reportProgress(0.0);
        var readmePath = Path.Combine(outputPathRoot, Functions.OutputPath(game), Constants.ReadmeFile);
        if (File.Exists(readmePath) && !overwriteReadmes) return;
        var cuefile = game.CuePath;
        var lines = File.ReadLines(cuefile);
        List<string> bins = [];
        bins.AddRange(from line in lines
            where line.StartsWith("FILE")
            select Constants.BinFileRegex().Matches(line)
            into matches
            select matches[0].Groups[1].Value
            into bin
            select Path.Combine(Path.GetDirectoryName(cuefile)!, bin));

        var md5 = MD5.Create();
        if (game.ChdData.TrackInfo.Count != 0) game.ChdData.TrackInfo.Clear();
        for (var i = 0; i < bins.Count; i++)
        {
            var bin = bins[i];
            await using var fs = File.OpenRead(bin);
            var hashArray = await md5.ComputeHashAsync(fs);
            var hash = string.Join(string.Empty, hashArray.Select(b => b.ToHexString(2)));
            game.ChdData.TrackInfo.Add((i + 1, hash));
            reportProgress((double)i / bins.Count * 100.0);
        }

        reportProgress(100.0);
    }

    private static void GetChdManInfo(Game game, string outputPathRoot)
    {
        var chdFile = Path.GetFileName(Path.ChangeExtension(game.CuePath, "chd"));
        var chdPath = Path.Combine(outputPathRoot, Functions.OutputPath(game), chdFile);
        Functions.LoadChdManInfo(chdPath, game);
    }

    private static void GenerateReadme(Game game, string outputPathRoot, bool overwriteReadmes)
    {
        var readmePath = Path.Combine(outputPathRoot, Functions.OutputPath(game), Constants.ReadmeFile);
        if (File.Exists(readmePath) && !overwriteReadmes) return;
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
}
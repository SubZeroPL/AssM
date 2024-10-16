﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AssM.Data;
using Avalonia.Threading;
using DiscTools;

namespace AssM.Classes;

public static class Functions
{
    public static string OutputPath(Game game) => Path.Combine(game.Platform.ToString(), game.Id);

    public static string GetChdName(Game game, Configuration configuration) => configuration.GameIdAsChdName
        ? $"{game.Id.ToUpper()}.chd"
        : Path.GetFileName(Path.ChangeExtension(game.CuePath, "chd"));

    public static void LoadChdManInfo(string chdPath, Game game)
    {
        if (!File.Exists(chdPath)) return;
        var chdmanInfo = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Constants.ChdMan,
                Arguments = string.Format(Constants.ChdManInfo, chdPath),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        List<string> output = [];
        chdmanInfo.Start();
        while (!chdmanInfo.StandardOutput.EndOfStream)
        {
            output.Add(chdmanInfo.StandardOutput.ReadLine() ?? string.Empty);
        }

        List<string> error = [];
        while (!chdmanInfo.StandardError.EndOfStream)
        {
            error.Add(chdmanInfo.StandardError.ReadLine() ?? string.Empty);
        }

        if (error.Count != 0)
        {
            throw new ProcessingException(string.Join(Environment.NewLine, error));
        }

        /*
            [0]: "chdman - MAME Compressed Hunks of Data (CHD) manager 0.257 (mame0257)"
            [1]: "Input file:   G:\\test\\psx\\Tekken 3 (USA)\\Tekken 3 (USA).chd"
            [2]: "File Version: 5"
            [3]: "Logical size: 716,382,720 bytes"
            [4]: "Hunk Size:    19,584 bytes"
            [5]: "Total Hunks:  36,580"
            [6]: "Unit Size:    2,448 bytes"
            [7]: "Total Units:  292,640"
            [8]: "Compression:  cdlz (CD LZMA), cdzl (CD Deflate), cdfl (CD FLAC)"
            [9]: "CHD size:     459,654,890 bytes"
            [10]: "Ratio:        64.2%"
            [11]: "SHA1:         319ec7377eb0c3ef0b6049e440937d26ae743290"
            [12]: "Data SHA1:    2685e94ac98b12f8771db38051f1c2622f0bbc7a"
         */

        var matches = Constants.ChdManVersionRegex().Matches(output[0]);
        game.ChdData.ChdManVersion = matches[0].Groups[1].Value;
        var version = int.Parse(output[2].Split(':').Last().Trim());
        game.ChdData.FileVersion = version;
        var sha1 = output[11].Split(':').Last().Trim();
        var dataSha1 = output[12].Split(':').Last().Trim();
        game.ChdData.Sha1Hash = sha1.ToUpperInvariant();
        game.ChdData.DataSha1Hash = dataSha1.ToUpperInvariant();
        game.ChdCreated = true;
    }

    public static void LoadExistingData(Game game, Configuration configuration, string? readmePath = null)
    {
        if (string.IsNullOrWhiteSpace(configuration.OutputDirectory)) return;

        readmePath ??= Path.Combine(configuration.OutputDirectory, OutputPath(game), Constants.ReadmeFile);
        if (!File.Exists(readmePath)) game.ReadmeCreated = false;

        if (string.IsNullOrWhiteSpace(game.Description) && !game.Modified)
        {
            LoadDescriptionFromReadme(readmePath, game);
        }

        if (!game.Modified)
        {
            LoadTitleFromReadme(readmePath, game);
        }

        LoadTrackInfoFromReadme(readmePath, game);

        if (string.IsNullOrWhiteSpace(game.Id))
        {
            LoadGameIdFromReadme(readmePath, game);
        }

        if (game.Platform == DetectedDiscType.UnknownFormat)
        {
            var segments = readmePath.Split(Path.DirectorySeparatorChar).Reverse().ToArray();
            var platform = segments.Length >= 3 ? segments[2] : string.Empty;
            if (Enum.TryParse<DetectedDiscType>(platform, out var detectedDiscType))
            {
                game.Platform = detectedDiscType;
            }
        }

        var path = Path.Combine(configuration.OutputDirectory, OutputPath(game));
        var chdFile = GetChdName(game, configuration);
        var chdPath = Path.Combine(path, chdFile);
        if (File.Exists(chdPath)) LoadChdManInfo(chdPath, game);
        else game.ChdCreated = false;
    }

    private static void LoadTitleFromReadme(string readmePath, Game game)
    {
        if (!File.Exists(readmePath)) return;
        if (game.Modified) return;
        var lines = File.ReadAllLines(readmePath);
        var index = Array.FindIndex(lines, line => line.Contains("**Game name:**"));
        var endIndex = Array.FindIndex(lines, line => line.Contains("**Game ID:**"));
        var title = string.Join("", lines.Skip(index + 1).Take(new Range(1, endIndex - 1))).Trim();
        if (!string.IsNullOrEmpty(title)) game.Title = title;
        game.ReadmeCreated = true;
    }

    private static void LoadDescriptionFromReadme(string readmePath, Game game)
    {
        if (!File.Exists(readmePath)) return;
        if (game.Modified) return;
        var lines = File.ReadAllLines(readmePath);
        var index = Array.FindIndex(lines, line => line.Contains("**Description:**"));
        var description = string.Join(Environment.NewLine, lines.Skip(index + 1)).Trim();
        game.Description = description;
    }

    private static void LoadGameIdFromReadme(string readmePath, Game game)
    {
        if (!File.Exists(readmePath)) return;
        var lines = File.ReadAllLines(readmePath);
        var index = Array.FindIndex(lines, line => line.Contains("**Game ID:**"));
        var id = string.Join("", lines.Skip(index + 1).Take(3)).Trim();
        if (!string.IsNullOrEmpty(id)) game.Id = id;
        game.ReadmeCreated = true;
    }

    private static void LoadTrackInfoFromReadme(string readmePath, Game game)
    {
        if (!File.Exists(readmePath)) return;
        if (game.ChdData.TrackInfo.Count != 0) return;
        var lines = File.ReadAllLines(readmePath);
        var startIndex = Array.FindIndex(lines, line => line.Contains("**Hash:**")) + 1;
        var endIndex = Array.FindIndex(lines, line => line.Contains("**Description:**")) - 1;
        var hashes = lines.Skip(startIndex).Take(endIndex - startIndex).Where(item => item.StartsWith("BIN"));
        foreach (var hashLine in hashes)
        {
            var trackId = int.Parse(Constants.BinHashRegex().Matches(hashLine)[0].Groups[1].Value.Trim());
            var trackHash = Constants.BinHashRegex().Matches(hashLine)[0].Groups[2].Value.Trim();
            game.ChdData.TrackInfo.Add((trackId, trackHash));
        }
    }

    public static List<string> GetCueFilesInDirectory(string directory)
    {
        var result = Directory.GetFiles(directory).Where(d => Path.GetExtension(d) == ".cue").ToList();
        Directory.GetDirectories(directory).ToList().ForEach(d => result.AddRange(GetCueFilesInDirectory(d)));
        return result;
    }

    public static List<string> GetReadmeFilesInDirectory(string directory)
    {
        var result = Directory.GetFiles(directory).Where(file => Path.GetFileName(file) == Constants.ReadmeFile)
            .ToList();
        Directory.GetDirectories(directory).ToList().ForEach(d => result.AddRange(GetReadmeFilesInDirectory(d)));
        return result;
    }

    public static Game AddGameToList(string cuePath, Configuration configuration, ObservableCollection<Game> gameList)
    {
        var di = DiscInspector.ScanDisc(cuePath);
        var title = configuration.GetTitleFromCue ? Path.GetFileNameWithoutExtension(cuePath) : di.Data.GameTitle;
        var game = new Game
        {
            Title = title, Id = di.Data.SerialNumber, Platform = di.DetectedDiscType,
            CuePath = cuePath
        };
        var existingGame = gameList.FirstOrDefault(g => g.Id == game.Id);
        if (existingGame != null)
        {
            existingGame.Title = game.Title;
            existingGame.Platform = game.Platform;
            existingGame.CuePath = game.CuePath;
            existingGame.Id = game.Id;
        }
        else
        {
            Dispatcher.UIThread.Invoke(() => { gameList.Add(game); });
        }

        return game;
    }
}
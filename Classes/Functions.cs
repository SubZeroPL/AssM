using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using AssM.Data;
using Avalonia.Threading;
using DiscTools;
using NLog;

namespace AssM.Classes;

public static class Functions
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public static string OutputPath(Game game) => Path.Combine(game.Platform.ToString(), game.Id);

    public static string GetChdName(Game game, Configuration configuration) => configuration.GameIdAsChdName
        ? $"{game.Id.ToUpper()}.chd"
        : Path.GetFileName(Path.ChangeExtension(game.ImagePath, "chd"));

    public static void LoadChdManInfo(string chdPath, Game game)
    {
        Logger.Debug($"Loading chdMan info from {chdPath}");
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
            Logger.Error($"Failed to load chdMan info from {chdPath}\n{error}");
            throw new ProcessingException(string.Join(Environment.NewLine, error));
        }

        var matches = Constants.ChdManVersionRegex().Matches(output[0]);
        game.ChdData.ChdManVersion = matches[0].Groups[1].Value;
        var version = int.Parse(output[2].Split(':').Last().Trim());
        game.ChdData.FileVersion = version;
        var sha1 = output[11].Split(':').Last().Trim();
        var dataSha1 = output[12].Split(':').Last().Trim();
        game.ChdData.Sha1Hash = sha1.ToUpperInvariant();
        game.ChdData.DataSha1Hash = dataSha1.ToUpperInvariant();
        game.ChdCreated = true;
        Logger.Debug($"Loaded chdMan info from {chdPath}");
    }

    public static void LoadExistingData(Game game, Configuration configuration, string? readmePath = null)
    {
        Logger.Debug($"Loading existing data from {readmePath} for game {game.Title}");
        if (string.IsNullOrWhiteSpace(configuration.OutputDirectory)) return;

        readmePath ??= Path.Combine(configuration.OutputDirectory, OutputPath(game), Constants.ReadmeFile);
        if (!File.Exists(readmePath)) game.ReadmeCreated = false;

        if (!game.Modified)
        {
            if (!LoadTitleFromReadme(readmePath, game))
            {
                Logger.Error($"Failed to load title from {readmePath}");
            }
        }
        
        if (string.IsNullOrWhiteSpace(game.Description) && !game.Modified)
        {
            LoadDescriptionFromReadme(readmePath, game);
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
        Logger.Debug($"Loaded existing data from {readmePath} for game {game.Title}");
    }

    private static bool LoadTitleFromReadme(string readmePath, Game game)
    {
        Logger.Debug($"Loading title from {readmePath}");
        if (!File.Exists(readmePath)) return false;
        if (game.Modified) return false;
        var lines = File.ReadAllLines(readmePath);
        var index = Array.FindIndex(lines, line => line.Contains("**Game name:**"));
        var endIndex = Array.FindIndex(lines, line => line.Contains("**Game ID:**"));
        if (index < 0 || endIndex < 0)
        {
            Logger.Error($"Failed to load title from {readmePath}. Readme file is malformed");
            return false;
        }
        var title = string.Join("", lines.Skip(index + 1).Take(new Range(1, endIndex - 1))).Trim();
        if (!string.IsNullOrEmpty(title)) game.Title = title;
        game.ReadmeCreated = true;
        return true;
    }

    private static void LoadDescriptionFromReadme(string readmePath, Game game)
    {
        Logger.Debug($"Loading description from {readmePath}");
        if (!File.Exists(readmePath)) return;
        if (game.Modified) return;
        var lines = File.ReadAllLines(readmePath);
        var index = Array.FindIndex(lines, line => line.Contains("**Description:**"));
        if (index < 0)
        {
            Logger.Error($"Failed to load description from {readmePath}");
            return;
        }

        var description = string.Join(Environment.NewLine, lines.Skip(index + 1)).Trim();
        game.Description = description;
    }

    private static void LoadGameIdFromReadme(string readmePath, Game game)
    {
        Logger.Debug($"Loading game id from {readmePath}");
        if (!File.Exists(readmePath)) return;
        var lines = File.ReadAllLines(readmePath);
        var index = Array.FindIndex(lines, line => line.Contains("**Game ID:**"));
        if (index < 0)
        {
            Logger.Error($"Failed to load game id from {readmePath}");
            return;
        }

        var id = string.Join("", lines.Skip(index + 1).Take(3)).Trim();
        if (!string.IsNullOrEmpty(id)) game.Id = id;
        game.ReadmeCreated = true;
    }

    private static void LoadTrackInfoFromReadme(string readmePath, Game game)
    {
        Logger.Debug($"Loading track info from {readmePath}");
        if (!File.Exists(readmePath)) return;
        if (game.ChdData.TrackInfo.Count != 0) return;
        var lines = File.ReadAllLines(readmePath);
        var startIndex = Array.FindIndex(lines, line => line.Contains("**Hash:**")) + 1;
        if (startIndex < 0)
        {
            Logger.Error($"Failed to load track info from {readmePath}");
            return;
        }

        var endIndex = Array.FindIndex(lines, line => line.Contains("**Description:**")) - 1;
        var hashes = lines.Skip(startIndex).Take(endIndex - startIndex).Where(item => item.StartsWith("TRACK"));
        foreach (var hashLine in hashes)
        {
            var matches = Constants.BinHashRegex().Matches(hashLine);
            if (matches.Count == 0)
            {
                Logger.Error($"Failed to load track info from {readmePath}");
                return;
            }

            try
            {
                var trackId = int.Parse(matches[0].Groups[1].Value.Trim());
                var trackHash = matches[0].Groups[2].Value.Trim();
                game.ChdData.TrackInfo.Add((trackId, trackHash));
            }
            catch (ArgumentOutOfRangeException e)
            {
                Logger.Error($"Failed to load track info from {readmePath}");
                Logger.Error(e);
                return;
            }
        }
    }

    public static List<string> GetCueIsoFilesInDirectory(string directory)
    {
        Logger.Debug($"Getting cue/iso files from {directory}");
        var result = Directory.GetFiles(directory)
            .Where(d => Path.GetExtension(d) == ".cue" || Path.GetExtension(d) == ".iso").ToList();
        Directory.GetDirectories(directory).ToList().ForEach(d => result.AddRange(GetCueIsoFilesInDirectory(d)));
        return result;
    }

    public static List<string> GetReadmeFilesInDirectory(string directory)
    {
        Logger.Debug($"Getting readme files from {directory}");
        var result = Directory.GetFiles(directory).Where(file => Path.GetFileName(file) == Constants.ReadmeFile)
            .ToList();
        Directory.GetDirectories(directory).ToList().ForEach(d => result.AddRange(GetReadmeFilesInDirectory(d)));
        return result;
    }

    public static Game? AddGameToList(string imagePath, Configuration configuration, ObservableCollection<Game> gameList)
    {
        Logger.Debug($"Adding game to list from {imagePath}");
        var di = DiscInspector.ScanDisc(imagePath);
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract - apparently there are games that have no Id in image (like SLPS-00018)
        if (di.Data.SerialNumber == null)
        {
            Logger.Error($"Failed to add game to list from {imagePath}{Environment.NewLine}Id not present in image");
            return null;
        }

        var title = configuration.GetTitleFromCue ? Path.GetFileNameWithoutExtension(imagePath) : di.Data.GameTitle;
        var game = new Game
        {
            Title = title, Id = di.Data.SerialNumber, Platform = di.DetectedDiscType,
            ImagePath = imagePath
        };
        var existingGame = gameList.FirstOrDefault(g => g.Id == game.Id);
        if (existingGame != null)
        {
            Logger.Debug($"Game already exists: {existingGame.Title}, updating");
            existingGame.Title = game.Title;
            existingGame.Platform = game.Platform;
            existingGame.ImagePath = game.ImagePath;
            existingGame.Id = game.Id;
        }
        else
        {
            Dispatcher.UIThread.Invoke(() => { gameList.Add(game); });
        }

        return game;
    }

    public static void ProcessJson(Configuration configuration, Game game, Action<double> reportProgress)
    {
        Logger.Debug("Processing json");
        try
        {
            if (!File.Exists("JAssOn.dll"))
            {
                Logger.Debug("...or not");
                return;
            }

            var lib = Assembly.LoadFrom("JAssOn.dll");
            Logger.Debug($"version: {lib.GetName().Version?.ToString(3)}");
            var cls = lib.GetType("JAssOn.Ldrr");
            if (cls == null) return;
            dynamic? inst = Activator.CreateInstance(cls);
            if (inst == null) return;
            inst.ProcessJson(configuration.OutputDirectory, game.Id, game.Title, reportProgress);
        }
        catch (Exception e)
        {
            Logger.Debug($"Json processing cancelled: {e}");
        }
    }

    public static bool IsIso(Game game) => game.ImagePath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase);
}
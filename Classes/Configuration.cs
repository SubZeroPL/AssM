using System.IO;
using System.Text.Json;

namespace AssM.Classes;

public class Configuration
{
    public string OutputDirectory { get; set; } = string.Empty;
    public ChdProcessing ChdProcessing { get; set; }
    public bool OverwriteExistingReadmes { get; set; }
    public bool GetTitleFromCue { get; set; }
    public bool GameIdAsChdName { get; set; }

    private const string FileName = "Settings.json"; 
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static Configuration? Load()
    {
        if (!File.Exists(FileName)) return null;
        var jsonString = File.ReadAllText(FileName);
        return JsonSerializer.Deserialize<Configuration>(jsonString);
    }

    public void Save()
    {
        var jsonString = JsonSerializer.Serialize(this, Options);
        File.WriteAllText(FileName, jsonString);
    }
}
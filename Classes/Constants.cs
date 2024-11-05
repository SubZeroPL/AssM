using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace AssM.Classes;

public static partial class Constants
{
    private static bool IsWindows() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static readonly string ChdMan = string.Join("", Path.Combine("Tools", "chdman"), IsWindows() ? ".exe" : "");
    public const string ChdManInfo = """
                                     info -i "{0}"
                                     """;
    public const string ChdManConvert = """
                                        createcd -i "{0}" -o "{1}" -f
                                        """;
    public static readonly string ReadmeTemplate = Path.Combine("Templates", "README-template.md");
    public const string ReadmeFile = "README.md";
    private const string ChdManVersion = @"chdman - MAME Compressed Hunks of Data \(CHD\) manager (\d\.\d+) \(.+\)";
    [GeneratedRegex(ChdManVersion)]
    public static partial Regex ChdManVersionRegex();
    private const string ChdManProgress = @"Compressing, (.+)\% complete\.\.\. \(ratio\=.+\..+\%\)";
    [GeneratedRegex(ChdManProgress)]
    public static partial Regex ChdManProgressRegex();
    public const string ChdManComplete = "Compression complete";
    private const string BinFile = "FILE \"(.+)\" BINARY";
    [GeneratedRegex(BinFile)]
    public static partial Regex BinFileRegex();

    private const string BinHash = @"BIN \(TRACK (\d{2})\) MD5\: (.+)";
    [GeneratedRegex(BinHash)]
    public static partial Regex BinHashRegex();
    public const string ReadmeGameTitle = "#gameTitle#";
    public const string ReadmeGameId = "#gameId#";
    public const string ReadmeChdHash = "#chdHash#";
    public const string ReadmeBinHashes = "#binHashes#";
    public const string ReadmeGameDescription = "#description#";
    
    public const string GithubLink = "https://github.com/SubZeroPL/AssM";
    public const string DiscordLink = "https://discord.com/invite/USu77Cktqd";
    public const string LatestReleaseLink = "https://api.github.com/repos/SubZeroPL/AssM/releases/latest";

    public const string LogName = "Debug.log";

    public static readonly string[] Steps =
    [
        "",
        "Conversion to CHD",
        "Calculating tracks MD5",
        "Getting CHD info",
        "Generating README",
        "Process JSON"
    ];
}
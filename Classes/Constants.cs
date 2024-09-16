using System.Text.RegularExpressions;

namespace AssM.Classes;

public static partial class Constants
{
    public const string ChdMan = @"Tools\ChdMan.exe";
    public const string ChdManInfo = """
                                     info -i "{0}"
                                     """;
    public const string ChdManConvert = """
                                        createcd -i "{0}" -o "{1}" -f
                                        """;
    public const string ReadmeTemplate = @"Templates\README-template.md";
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

    public static readonly string[] Steps =
    [
        "",
        "Conversion to CHD",
        "Calculating tracks MD5",
        "Getting CHD info",
        "Generating README"
    ];
}
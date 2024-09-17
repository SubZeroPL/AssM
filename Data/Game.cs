using System;
using System.Collections.Generic;
using System.Linq;
using DiscTools;

namespace AssM.Data;

public class Game
{
    public string Title { get; set; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string CuePath { get; init; } = string.Empty;
    public DetectedDiscType Platform { get; init; }
    public bool ReadmeCreated { get; set; }
    public bool ChdCreated { get; set; }
    public string Description { get; set; } = string.Empty;
    public ChdData ChdData { get; } = new();
}

public class ChdData
{
    public string ChdManVersion { get; set; } = string.Empty;
    public int FileVersion { get; set; }
    public string Sha1Hash { get; set; } = string.Empty;
    public string DataSha1Hash { get; set; } = string.Empty;
    public List<(int TrackNo, string TrackMD5)> TrackInfo { get; } = [];

    public string GetTrackInfo()
    {
        return string.Join(Environment.NewLine,
            TrackInfo.Select(ti => $"BIN (TRACK {ti.TrackNo,2:D2}) MD5: {ti.TrackMD5}{Environment.NewLine}")).Trim();
    }
}
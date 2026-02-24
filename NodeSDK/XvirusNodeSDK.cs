using System;
using System.Linq;
using System.Text.Json;
using BaseLibrary.Serializers;
using Microsoft.JavaScript.NodeApi;
using Xvirus.NodeSDK.Serializers;

namespace Xvirus;

/// <summary>
/// Represents the result of a file scan, exported as a JavaScript class.
/// </summary>
[JSExport]
public class ScanResultNode
{
    public bool IsMalware { get; set; }
    public string Name { get; set; } = string.Empty;
    public double MalwareScore { get; set; }
    public string Path { get; set; } = string.Empty;
}

/// <summary>
/// Xvirus SDK exported as a Node.js native AOT module.
/// All public static members are exported as module-level functions.
/// </summary>
public static class XvirusNodeSDK
{
    private static Scanner? Scanner;

    /// <summary>
    /// Loads the Xvirus engine and its databases.
    /// </summary>
    [JSExport]
    public static void Load(bool force = false)
    {
        if (force)
            Unload();

        if (force || Scanner == null)
        {
            var settings = Settings.Load();
            var database = new DB(settings);
            var ai = new AI(settings);
            var rules = new Rules();
            Scanner = new Scanner(settings, database, ai, rules);
        }
    }

    /// <summary>
    /// Unloads the Xvirus engine and frees resources.
    /// </summary>
    [JSExport]
    public static void Unload()
    {
        Scanner = null;
        GC.Collect();
    }

    /// <summary>
    /// Scans a single file and returns a structured result object.
    /// </summary>
    [JSExport]
    public static ScanResultNode Scan(string filePath)
    {
        if (Scanner == null)
            Load();

        var result = Scanner!.ScanFile(filePath);

        if (result.MalwareScore == -1)
            throw new Exception(result.Name);

        return new ScanResultNode
        {
            IsMalware = result.IsMalware,
            Name = result.Name,
            MalwareScore = result.MalwareScore,
            Path = result.Path
        };
    }

    /// <summary>
    /// Scans a single file and returns the result as a JSON string.
    /// </summary>
    [JSExport]
    public static string ScanAsString(string filePath)
    {
        var result = Scan(filePath);
        return JsonSerializer.Serialize(result, NodeSourceGenerationContext.Default.ScanResultNode);
    }

    /// <summary>
    /// Scans all files in a folder and returns an array of result objects.
    /// </summary>
    [JSExport]
    public static ScanResultNode[] ScanFolder(string folderPath)
    {
        if (Scanner == null)
            Load();

        return Scanner!.ScanFolder(folderPath)
            .Select(r => new ScanResultNode
            {
                IsMalware = r.IsMalware,
                Name = r.Name,
                MalwareScore = r.MalwareScore,
                Path = r.Path
            })
            .ToArray();
    }

    /// <summary>
    /// Scans all files in a folder and returns the results as a JSON string.
    /// </summary>
    [JSExport]
    public static string ScanFolderAsString(string folderPath)
    {
        var results = ScanFolder(folderPath);
        return JsonSerializer.Serialize(results, NodeSourceGenerationContext.Default.ScanResultNodeArray);
    }

    /// <summary>
    /// Checks for database and SDK updates. Returns a JSON string with update info.
    /// </summary>
    [JSExport]
    public static string CheckUpdates(bool loadDBAfterUpdate = false)
    {
        var settings = Settings.Load();
        var result = Updater.CheckUpdates(settings);

        if (loadDBAfterUpdate)
            Load(true);

        return result;
    }

    /// <summary>
    /// Returns the current engine settings as a JSON string.
    /// </summary>
    [JSExport]
    public static string GetSettings()
    {
        var settings = Scanner != null ? Scanner.settings : Settings.Load();
        return JsonSerializer.Serialize(settings, SourceGenerationContext.Default.SettingsDTO);
    }

    /// <summary>
    /// Gets or sets logging. Pass true/false to set; omit to query the current state.
    /// </summary>
    [JSExport]
    public static bool Logging(bool? enableLogging = null)
    {
        Logger.EnableLogging = enableLogging ?? Logger.EnableLogging;
        return Logger.EnableLogging;
    }

    /// <summary>
    /// Gets or sets the base folder used to locate databases and resources.
    /// Omit the argument to query the current base folder.
    /// </summary>
    [JSExport]
    public static string BaseFolder(string? baseFolder = null)
    {
        Utils.CurrentDir = baseFolder ?? AppContext.BaseDirectory;
        return Utils.CurrentDir;
    }

    /// <summary>
    /// Returns the SDK version string.
    /// </summary>
    [JSExport]
    public static string Version()
    {
        return Utils.GetVersion();
    }
}

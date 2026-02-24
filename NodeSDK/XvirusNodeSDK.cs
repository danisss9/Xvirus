using System;
using System.Linq;
using System.Text.Json;
using BaseLibrary.Serializers;
using Microsoft.JavaScript.NodeApi;
using Xvirus.NodeSDK.Serializers;

namespace Xvirus;

/// <summary>
/// Represents the result of a single file scan.
/// </summary>
[JSExport]
public class ScanResultNode
{
    /// <summary>Whether the file was classified as malware.</summary>
    public bool IsMalware { get; set; }

    /// <summary>Detection name, or an empty string when the file is clean.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Malware probability score in the range [0, 1].</summary>
    public double MalwareScore { get; set; }

    /// <summary>Absolute path of the scanned file.</summary>
    public string Path { get; set; } = string.Empty;
}

/// <summary>
/// Xvirus SDK — Node.js native AOT module.
/// Build with <c>dotnet publish -r &lt;rid&gt; -c Release</c>.
/// Produces <c>XvirusNodeSDK.node</c> and an ESM wrapper <c>XvirusNodeSDK.js</c>.
/// </summary>
[JSExport]
public static class XvirusNodeSDK
{
    private static Scanner? Scanner;

    /// <summary>
    /// Loads the Xvirus engine and all detection databases.
    /// Call this once before scanning. Pass <c>force=true</c> to reload.
    /// </summary>
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
    /// Unloads the engine and releases all held resources.
    /// </summary>
    public static void Unload()
    {
        Scanner = null;
        GC.Collect();
    }

    /// <summary>
    /// Scans a single file and returns a typed result object.
    /// </summary>
    /// <param name="filePath">Absolute path to the file to scan.</param>
    /// <returns>A <see cref="ScanResultNode"/> with detection details.</returns>
    /// <exception cref="Exception">Thrown when the file cannot be scanned.</exception>
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
    /// <param name="filePath">Absolute path to the file to scan.</param>
    public static string ScanAsString(string filePath)
    {
        var result = Scan(filePath);
        return JsonSerializer.Serialize(result, NodeSourceGenerationContext.Default.ScanResultNode);
    }

    /// <summary>
    /// Scans all files in a folder and returns an array of result objects.
    /// </summary>
    /// <param name="folderPath">Absolute path to the folder to scan.</param>
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
    /// <param name="folderPath">Absolute path to the folder to scan.</param>
    public static string ScanFolderAsString(string folderPath)
    {
        var results = ScanFolder(folderPath);
        return JsonSerializer.Serialize(results, NodeSourceGenerationContext.Default.ScanResultNodeArray);
    }

    /// <summary>
    /// Checks for database and SDK updates.
    /// </summary>
    /// <param name="loadDBAfterUpdate">
    /// When <c>true</c>, reloads the engine databases after a successful update.
    /// </param>
    /// <returns>A JSON string describing available updates.</returns>
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
    public static string GetSettings()
    {
        var settings = Scanner != null ? Scanner.settings : Settings.Load();
        return JsonSerializer.Serialize(settings, SourceGenerationContext.Default.SettingsDTO);
    }

    /// <summary>
    /// Gets or sets whether logging is enabled.
    /// Omit the argument to query the current state without changing it.
    /// </summary>
    /// <param name="enableLogging">Pass <c>true</c>/<c>false</c> to enable/disable logging.</param>
    /// <returns>The current logging state after the call.</returns>
    public static bool Logging(bool? enableLogging = null)
    {
        Logger.EnableLogging = enableLogging ?? Logger.EnableLogging;
        return Logger.EnableLogging;
    }

    /// <summary>
    /// Gets or sets the base folder used to locate databases and resources.
    /// Omit the argument to query the current value without changing it.
    /// </summary>
    /// <param name="baseFolder">Absolute path to use as the base folder.</param>
    /// <returns>The current base folder path after the call.</returns>
    public static string BaseFolder(string? baseFolder = null)
    {
        Utils.CurrentDir = baseFolder ?? AppContext.BaseDirectory;
        return Utils.CurrentDir;
    }

    /// <summary>
    /// Returns the SDK version string.
    /// </summary>
    public static string Version()
    {
        return Utils.GetVersion();
    }
}

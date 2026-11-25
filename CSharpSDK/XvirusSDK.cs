using BaseLibrary.Serializers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Xvirus.Model;

namespace Xvirus
{
    public class XvirusSDK
    {
        private static Scanner? Scanner;

        public static bool CancelAllScans()
        {
            return (Scanner?.CancelAllScans() ?? 0) > 0;
        }

        public static bool CancelScan(string path)
        {
            return Scanner?.CancelScan(path) ?? false;
        }

        public static void Load(bool force = false)
        {
            if (force)
                Unload();

            if (force || Scanner == null)
            {
                var settings = Settings.Load();
                var database = new DB(settings);
                var ai = new AI(settings);
                var aiScript = new AIScript(settings);
                var rules = new Rules("rules.json", seedRunningProcesses: false);
                Scanner = new Scanner(settings, database, ai, aiScript, rules);
            }
        }

        public static void Unload()
        {
            Scanner = null;
            GC.Collect();
        }

        public static ScanResult Scan(string filePath)
        {
            if (Scanner == null)
                Load();

            return Scanner!.ScanFile(filePath);
        }

        public static ScanResult Scan(string filePath, CancellationToken ct)
        {
            if (Scanner == null)
                Load();

            return Scanner!.ScanFile(filePath, ct);
        }

        public static string ScanString(string filePath)
        {
            return JsonSerializer.Serialize(Scan(filePath), SourceGenerationContext.Default.ScanResult);
        }

        public static IEnumerable<ScanResult> ScanFolder(string folderPath)
        {
            if (Scanner == null)
                Load();

            return Scanner!.ScanFolder(folderPath);
        }

        public static IEnumerable<ScanResult> ScanFolder(string folderPath, CancellationToken ct)
        {
            if (Scanner == null)
                Load();

            return Scanner!.ScanFolder(folderPath, ct);
        }

        public static string ScanFolderString(string folderPath)
        {
            // One result per line. Each item is serialized on its own so that brackets
            // inside paths or names are never touched by the formatting.
            var items = ScanFolder(folderPath).Select(r => JsonSerializer.Serialize(r, SourceGenerationContext.Default.ScanResult));
            var body = string.Join(",\n", items);
            return body.Length == 0 ? "[]" : "[\n" + body + "\n]";
        }

        public static string CheckUpdates(bool loadDBAfterUpdate = false)
        {
            var settings = Settings.Load();
            var result = Updater.CheckUpdates(settings);

            if (loadDBAfterUpdate)
                Load(true);

            return result;
        }

        public static SettingsDTO GetSettings()
        {
            return Scanner != null ? Scanner.settings : Settings.Load();
        }

        public static string GetSettingsString()
        {
            return JsonSerializer.Serialize(GetSettings(), SourceGenerationContext.Default.SettingsDTO);
        }

        public static bool Logging(bool? enableLogging = null)
        {
            Logger.EnableLogging = enableLogging ?? Logger.EnableLogging;
            return Logger.EnableLogging;
        }

        public static string BaseFolder(string? baseFolder = null)
        {
            Utils.CurrentDir = baseFolder ?? AppContext.BaseDirectory;
            return Utils.CurrentDir;
        }

        public static string Version()
        {
            return AppInfo.GetVersion();
        }
    }
}

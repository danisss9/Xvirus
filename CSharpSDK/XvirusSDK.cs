using BaseLibrary.Serializers;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Xvirus.Model;

namespace Xvirus
{
    public class XvirusSDK
    {
        private static Scanner? Scanner;

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

        public static string ScanFolderString(string folderPath)
        {
            var scanResultsString = JsonSerializer.Serialize(ScanFolder(folderPath), SourceGenerationContext.Default.ScanResult);
            return scanResultsString.Replace("[", "[\n", StringComparison.Ordinal).Replace("]", "\n]", StringComparison.Ordinal);
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
            return Utils.GetVersion();
        }
    }
}

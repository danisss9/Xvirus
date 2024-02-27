using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using XescSDK.Model;

namespace XescSDK
{
    public class XvirusSDK
    {
        private static Scanner? Scanner;

        public static void Load(bool force = false)
        {
            if(force) 
                Unload();

            if (force || Scanner == null)
            {
                var settings = Settings.Load();
                var database = new DB(settings);
                var ai = new AI(settings);
                Scanner = new Scanner(settings, database, ai);
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

            return Scanner!.ScanFile(filePath, out var _);
        }

        public static ScanResult Scan(string filePath, out string md5)
        {
            if (Scanner == null)
                Load();

            return Scanner!.ScanFile(filePath, out md5);
        }

        public static string ScanString(string filePath)
        {
            return JsonSerializer.Serialize(Scan(filePath));
        }

        public static string ScanFolderString(string folderPath)
        {
            var filePaths = Directory.GetFiles(folderPath, "*", new EnumerationOptions() { RecurseSubdirectories = true,AttributesToSkip = 0 } );
            var scanResults = new List<string>();

            foreach (var filePath in filePaths)
            {
                var sc = Scan(filePath, out var md5);
                var x = filePath + ": " + JsonSerializer.Serialize(sc.Name + "-" + md5);
                scanResults.Add(x);
                if(sc.IsMalware)
                    Console.WriteLine(x);
            }

            return string.Join("\n", scanResults);
          }
        

        public static string CheckUpdates(bool checkSDKUpdates = false, bool loadDBAfterUpdate = false)
        {
            var settings = Settings.Load();
            var result = Updater.CheckUpdates(settings, checkSDKUpdates);

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
            return JsonSerializer.Serialize(GetSettings());
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

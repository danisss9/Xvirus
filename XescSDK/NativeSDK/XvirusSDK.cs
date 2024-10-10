using System;
using System.Runtime.InteropServices;
using System.Text.Json;
using BaseLibrary.Serializers;

namespace XescSDK
{
    public class XvirusSDK
    {
        private static Scanner? Scanner;

        [UnmanagedCallersOnly(EntryPoint = "load")]
        public static ActionResult Load(bool force = false)
        {
            try
            {
                LoadAux(force);
                return new ActionResult() { Sucess = true, Result = Marshal.StringToHGlobalUni("Loaded Successfully") };
            }
            catch (Exception e)
            {
                return new ActionResult() { Sucess = false, Error = Marshal.StringToHGlobalUni(e.Message) };
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "unload")]
        public static ActionResult Unload()
        {
            try
            {
                UnloadAux();
                return new ActionResult() { Sucess = true, Result = Marshal.StringToHGlobalUni("Unloaded Successfully") };
            }
            catch (Exception e)
            {
                return new ActionResult() { Sucess = false, Error = Marshal.StringToHGlobalUni(e.Message) };
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "scan")]
        public static ScanResult Scan(IntPtr filePath)
        {
            try
            {
                var filePathAux = Marshal.PtrToStringUni(filePath);
                var result = ScanAux(filePathAux!);
                return new ScanResult()
                {
                    Sucess = true,
                    IsMalware = result.IsMalware,
                    MalwareScore = result.MalwareScore,
                    Name = Marshal.StringToHGlobalUni(result.Name)
                };
            }
            catch (Exception e)
            {
                return new ScanResult() { Sucess = false, Error = Marshal.StringToHGlobalUni(e.Message) };
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "scanAsString")]
        public static ActionResult ScanAsString(IntPtr filePath)
        {
            try
            {
                var filePathAux = Marshal.PtrToStringUni(filePath);
                var result = JsonSerializer.Serialize(ScanAux(filePathAux), options: new JsonSerializerOptions() { TypeInfoResolver = ScanResultGenerationContext.Default });
                return new ActionResult() { Sucess = true, Result = Marshal.StringToHGlobalUni(result) };
            }
            catch (Exception e)
            {
                return new ActionResult() { Sucess = false, Error = Marshal.StringToHGlobalUni(e.Message) };
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "checkUpdates")]
        public static ActionResult CheckUpdates(bool checkSDKUpdates = false, bool loadDBAfterUpdate = false)
        {
            try
            {
                var settings = Settings.Load();
                var result = Updater.CheckUpdates(settings, checkSDKUpdates);

                if (loadDBAfterUpdate)
                    LoadAux(true);

                return new ActionResult() { Sucess = true, Result = Marshal.StringToHGlobalUni(result) };
            }
            catch (Exception e)
            {
                return new ActionResult() { Sucess = false, Error = Marshal.StringToHGlobalUni(e.Message) };
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "getSettings")]
        public static ActionResult GetSettings()
        {
            try
            {
                var result = JsonSerializer.Serialize(GetSettingsAux(), options: new JsonSerializerOptions() { TypeInfoResolver = SettingsGenerationContext.Default });
                return new ActionResult() { Sucess = true, Result = Marshal.StringToHGlobalUni(result) };
            }
            catch (Exception e)
            {
                return new ActionResult() { Sucess = false, Error = Marshal.StringToHGlobalUni(e.Message) };
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "logging")]
        public static bool Logging(bool? enableLogging = null)
        {
            Logger.EnableLogging = enableLogging ?? Logger.EnableLogging;
            return Logger.EnableLogging;
        }

        [UnmanagedCallersOnly(EntryPoint = "baseFolder")]
        public static IntPtr BaseFolder(IntPtr baseFolder)
        {
            var baseFolderAux = Marshal.PtrToStringUni(baseFolder);
            Utils.CurrentDir = baseFolderAux ?? AppContext.BaseDirectory;
            return Marshal.StringToHGlobalUni(Utils.CurrentDir);
        }

        [UnmanagedCallersOnly(EntryPoint = "version")]
        public static IntPtr Version()
        {
            return Marshal.StringToHGlobalUni(Utils.GetVersion());
        }

        private static Model.ScanResult ScanAux(string filePath)
        {
            if (Scanner == null)
                LoadAux();

            var result = Scanner!.ScanFile(filePath, out var _);

            if (result.MalwareScore == -1)
                throw new Exception(result.Name);

            return result;
        }

        private static void LoadAux(bool force = false)
        {
            if (force)
                UnloadAux();

            if (force || Scanner == null)
            {
                var settings = Settings.Load();
                var database = new DB(settings);
                //var ai = new AI();
                Scanner = new Scanner(settings, database, null);
            }
        }

        private static void UnloadAux()
        {
            Scanner = null;
            GC.Collect();
        }

        private static Model.SettingsDTO GetSettingsAux()
        {
            return Scanner != null ? Scanner.settings : Settings.Load();
        }
    }

    public struct ActionResult
    {
        public bool Sucess { get; set; }
        public IntPtr Result { get; set; }
        public IntPtr Error { get; set; }
    }

    public struct ScanResult
    {
        public bool Sucess { get; set; }
        public IntPtr Error { get; set; }
        public bool IsMalware { get; set; }
        public IntPtr Name { get; set; }
        public double MalwareScore { get; set; }
    }
}

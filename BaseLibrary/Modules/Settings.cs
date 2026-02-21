using BaseLibrary.Serializers;
using System;
using System.IO;
using System.Text.Json;
using Xvirus.Model;

namespace Xvirus
{
    public class Settings
    {
        public static SettingsDTO Load(string path = "settings.json")
        {
            var fullPath = Utils.RelativeToFullPath(path);
            if (!File.Exists(fullPath))
                return new SettingsDTO();
            try
            {
                string jsonString = File.ReadAllText(fullPath);
                return JsonSerializer.Deserialize(jsonString, SourceGenerationContextIndent.Default.SettingsDTO)!;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                throw;
            }
        }

        public static void Save(SettingsDTO settings, string path = "settings.json")
        {
            var fullPath = Utils.RelativeToFullPath(path);
            try
            {
                string jsonString = JsonSerializer.Serialize(settings, SourceGenerationContextIndent.Default.SettingsDTO);
                File.WriteAllText(fullPath, jsonString);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                throw;
            }
        }

        public static AppSettingsDTO LoadAppSettings(string path = "appsettings.json")
        {
            var fullPath = Utils.RelativeToFullPath(path);
            if (!File.Exists(fullPath))
                return new AppSettingsDTO();
            try
            {
                string jsonString = File.ReadAllText(fullPath);
                return JsonSerializer.Deserialize(jsonString, SourceGenerationContextIndent.Default.AppSettingsDTO)!;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                throw;
            }
        }

        public static void SaveAppSettings(AppSettingsDTO settings, string path = "appsettings.json")
        {
            var fullPath = Utils.RelativeToFullPath(path);
            try
            {
                string jsonString = JsonSerializer.Serialize(settings, SourceGenerationContextIndent.Default.AppSettingsDTO);
                File.WriteAllText(fullPath, jsonString);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                throw;
            }
        }
    }
}

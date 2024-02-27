using BaseLibrary.Serializers;
using System;
using System.IO;
using System.Text.Json;
using XescSDK.Model;

namespace XescSDK
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
                return JsonSerializer.Deserialize(jsonString, SettingsGenerationContext.Default.SettingsDTO)!;
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
                string jsonString = JsonSerializer.Serialize(settings, options: new JsonSerializerOptions() { WriteIndented = true,TypeInfoResolver = SettingsGenerationContext.Default }) ;
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

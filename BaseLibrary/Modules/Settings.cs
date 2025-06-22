using BaseLibrary.Serializers;
using System;
using System.IO;
using System.Text.Json;
using Xvirus.Model;

namespace Xvirus
{
    public class Settings
    {
        private readonly static JsonSerializerOptions JsonSerializerOptions = new()
        {
            WriteIndented = true,
            TypeInfoResolver = SourceGenerationContext.Default
        };

        public static SettingsDTO Load(string path = "settings.json")
        {
            var fullPath = Utils.RelativeToFullPath(path);
            if (!File.Exists(fullPath))
                return new SettingsDTO();
            try
            {
                string jsonString = File.ReadAllText(fullPath);
                return JsonSerializer.Deserialize(jsonString, SourceGenerationContext.Default.SettingsDTO)!;
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
                string jsonString = JsonSerializer.Serialize(settings, JsonSerializerOptions) ;
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

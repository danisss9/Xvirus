using BaseLibrary.Serializers;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Xvirus.Model;

namespace Xvirus
{
    public class Updater
    {
        private static readonly UpdateMethod[] UpdateList = new UpdateMethod[]
        {
            new UpdateMethod {
                FileName = "viruslist.db",
                GetUpdateInfoVersion = (UpdateInfo info) => info.Maindb,
                GetDatabaseInfoVersion = (DatabaseDTO info) => info.MainDB,
                SetDatabaseInfoVersion = (DatabaseDTO info, long value) => info.MainDB = value,
            },
            new UpdateMethod {
                FileName = "dailylist.db",
                GetUpdateInfoVersion = (UpdateInfo info) => info.Dailydb,
                GetDatabaseInfoVersion = (DatabaseDTO info) => info.DailyDB,
                SetDatabaseInfoVersion = (DatabaseDTO info, long value) => info.DailyDB = value,
            },
            new UpdateMethod {
                FileName = "whitelist.db",
                GetUpdateInfoVersion = (UpdateInfo info) => info.Whitedb,
                GetDatabaseInfoVersion = (DatabaseDTO info) => info.WhiteDB,
                SetDatabaseInfoVersion = (DatabaseDTO info, long value) => info.WhiteDB = value,
            },
            new UpdateMethod {
                FileName = "dailywl.db",
                GetUpdateInfoVersion = (UpdateInfo info) => info.Dailywldb,
                GetDatabaseInfoVersion = (DatabaseDTO info) => info.DailywlDB,
                SetDatabaseInfoVersion = (DatabaseDTO info, long value) => info.DailywlDB = value,
            },
            new UpdateMethod {
                FileName = "heurlist.db",
                GetUpdateInfoVersion = (UpdateInfo info) => info.Heurdb,
                GetDatabaseInfoVersion = (DatabaseDTO info) => info.HeurDB,
                SetDatabaseInfoVersion = (DatabaseDTO info, long value) => info.HeurDB = value,
            },
            new UpdateMethod {
                FileName = "heurlist2.db",
                GetUpdateInfoVersion = (UpdateInfo info) => info.Heurdb2,
                GetDatabaseInfoVersion = (DatabaseDTO info) => info.HeurDB2,
                SetDatabaseInfoVersion = (DatabaseDTO info, long value) => info.HeurDB2 = value,
            },
            new UpdateMethod {
                FileName = "malvendor.db",
                GetUpdateInfoVersion = (UpdateInfo info) => info.Malvendordb,
                GetDatabaseInfoVersion = (DatabaseDTO info) => info.MalvendorDB,
                SetDatabaseInfoVersion = (DatabaseDTO info, long value) => info.MalvendorDB = value,
            },
            new UpdateMethod {
                FileName = "model.ai",
                GetUpdateInfoVersion = (UpdateInfo info) => info.Aimodel,
                GetDatabaseInfoVersion = (DatabaseDTO info) => info.AIModel,
                SetDatabaseInfoVersion = (DatabaseDTO info, long value) => info.AIModel = value,
            },
            new UpdateMethod {
                FileName = "scriptmodel.gguf",
                GetUpdateInfoVersion = (UpdateInfo info) => info.Scriptmodel,
                GetDatabaseInfoVersion = (DatabaseDTO info) => info.ScriptAIModel,
                SetDatabaseInfoVersion = (DatabaseDTO info, long value) => info.ScriptAIModel = value,
            },
        };

        public static string CheckUpdates(SettingsDTO settings)
        {
            using var wc = new HttpClient();
            try
            {
                var updateUrl = $"https://cloud.xvirus.net/api/updateinfo?app={AppInfo.AppCode}";
                var updateInfoStr = wc.GetStringAsync(updateUrl).GetAwaiter().GetResult();
                var updateInfo = JsonSerializer.Deserialize(updateInfoStr, SourceGenerationContextCamelCase.Default.UpdateInfo);
                var newUpdates = false;

                if (updateInfo == null)
                    throw new Exception("Could not get update information from the server!");

                var dirPath = Utils.RelativeToFullPath(settings.DatabaseFolder);
                if (!Directory.Exists(dirPath))
                    Directory.CreateDirectory(dirPath);

                foreach (var updateMethod in UpdateList)
                {
                    var currVersion = updateMethod.GetDatabaseInfoVersion(settings.DatabaseVersion);
                    var versionInfo = updateMethod.GetUpdateInfoVersion(updateInfo);
                    if (versionInfo != null && currVersion != versionInfo.Version)
                    {
                        // Stream to disk instead of buffering in memory: the script AI model is a
                        // large GGUF file (~1GB) that does not fit the byte-array pattern.
                        var path = Utils.RelativeToFullPath(settings.DatabaseFolder, updateMethod.FileName);
                        try
                        {
                            using (var response = wc.GetAsync(versionInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
                            {
                                response.EnsureSuccessStatusCode();
                                using var contentStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                                using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920);
                                contentStream.CopyTo(fileStream);
                            }
                        }
                        catch
                        {
                            // A partially written model would fail to load later; remove it so the
                            // next update attempt starts clean.
                            try { File.Delete(path); } catch { }
                            throw;
                        }
                        updateMethod.SetDatabaseInfoVersion(settings.DatabaseVersion, versionInfo.Version);
                        newUpdates = true;
                    }
                }

                settings.LastUpdateCheck = DateTime.UtcNow;
                Settings.Save(settings);

                if (settings.CheckSDKUpdates && updateInfo.App.Version != AppInfo.GetVersion())
                {
                    return "There is a new version available!";
                }

                return newUpdates ? "Database was updated!" : "Database is up-to-date!";
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                throw;
            }
        }
    }
}

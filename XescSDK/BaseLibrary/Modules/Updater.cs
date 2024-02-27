using BaseLibrary.Serializers;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using XescSDK.Model;

namespace XescSDK
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
        };
        private static readonly string updateUrl = "https://cloud.xvirus.net/api/updateinfo?app=sdk";

        public static string CheckUpdates(SettingsDTO settings, bool checkSDKUpdates)
        {
            using var wc = new HttpClient();
            try
            {
                var updateInfoStr = wc.GetStringAsync(updateUrl).GetAwaiter().GetResult();
                var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(updateInfoStr, new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, TypeInfoResolver = UpdateInfoGenerationContext.Default });
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
                        var database = wc.GetByteArrayAsync(versionInfo.DownloadUrl).GetAwaiter().GetResult();

                        if(database != null)
                        {
                            var path = Utils.RelativeToFullPath(settings.DatabaseFolder, updateMethod.FileName);
                            File.WriteAllBytes(path, database);
                            updateMethod.SetDatabaseInfoVersion(settings.DatabaseVersion, versionInfo.Version);
                            newUpdates = true;
                        }
                    }
                }

                if (newUpdates)
                    Settings.Save(settings);

                if (checkSDKUpdates && updateInfo.App.Version != Utils.GetVersion())
                {
                    return "There is a new SDK version available!";
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

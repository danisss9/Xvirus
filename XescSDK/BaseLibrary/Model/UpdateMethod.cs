using System;

namespace XescSDK.Model
{
    public class UpdateMethod
    {
        public string FileName { get; set; }
        public Func<UpdateInfo, VersionInfo<long>> GetUpdateInfoVersion { get; set; }
        public Func<DatabaseDTO, long> GetDatabaseInfoVersion { get; set; }
        public Action<DatabaseDTO, long> SetDatabaseInfoVersion { get; set; }
    }
}
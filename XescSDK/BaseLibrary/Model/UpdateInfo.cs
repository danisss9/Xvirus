namespace XescSDK.Model
{
    public class UpdateInfo
    {
        public VersionInfo<long> Maindb { get; set; }
        public VersionInfo<long> Dailydb { get; set; }
        public VersionInfo<long> Whitedb { get; set; }
        public VersionInfo<long> Dailywldb { get; set; }
        public VersionInfo<long> Heurdb { get; set; }
        public VersionInfo<long> Heurdb2 { get; set; }
        public VersionInfo<long> Malvendordb { get; set; }
        public VersionInfo<long> Aimodel { get; set; }
        public VersionInfo<string> App { get; set; }
    }

    public class VersionInfo<T>
    {
        public T Version { get; set; }
        public string DownloadUrl { get; set; }
        public string Description { get; set; }
    }
}

namespace XescSDK.Model
{
    public class ScanResult
    {
        public bool IsMalware { get; set; }
        public string Name { get; set; }
        public double MalwareScore { get; set; }
        public string Path { get; set; }

        public ScanResult(double malwareScore, string name, string path)
        {
            IsMalware = malwareScore > 0.8;
            Name = name;
            MalwareScore = malwareScore;
            Path = path;
        }
    }
}

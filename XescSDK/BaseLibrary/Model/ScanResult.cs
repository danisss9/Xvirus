namespace XescSDK.Model
{
    public class ScanResult
    {
        public bool IsMalware { get; set; }
        public string Name { get; set; }
        public double MalwareScore { get; set; }

        public ScanResult(double malwareScore, string name)
        {
            IsMalware = malwareScore > 0.8;
            Name = name;
            MalwareScore = malwareScore;
        }
    }
}

namespace Xvirus.Model
{
    public class ScanResult
    {
        public bool IsMalware { get; set; }
        public string Name { get; set; }
        public double MalwareScore { get; set; }
        public string Path { get; set; }

        public ScanResult(double malwareScore, string name, string path, double aggressivity = 0.8)
        {
            IsMalware = malwareScore > aggressivity;
            Name = name;
            MalwareScore = malwareScore;
            Path = path;
        }
    }
}

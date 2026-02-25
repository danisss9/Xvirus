namespace XvirusService.Model;

public class NetworkConnectionDTO
{
    public string Protocol { get; set; } = string.Empty;
    public string LocalAddress { get; set; } = string.Empty;
    public string RemoteAddress { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int Pid { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public double Score { get; set; }
}

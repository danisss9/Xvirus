using Xvirus.Model;

namespace XvirusService.Model;

public class BlockIpRequestDTO
{
    public string Ip { get; set; } = string.Empty;
    public string Direction { get; set; } = "both";
    public string Protocol { get; set; } = "any";
    public string Profile { get; set; } = "any";
    public bool KillConnections { get; set; } = true;
}

public class KillConnectionRequestDTO
{
    public string LocalAddress { get; set; } = string.Empty;
    public int? LocalPort { get; set; }
    public string RemoteAddress { get; set; } = string.Empty;
    public int? RemotePort { get; set; }
}

public class CreateRuleRequestDTO
{
    public string Kind { get; set; } = "program"; // program | ip | port | domain
    public string Path { get; set; } = string.Empty;
    public string Type { get; set; } = "block";   // allow | block
    public string Protocol { get; set; } = "any";
    public string Direction { get; set; } = "both";
    public string Profile { get; set; } = "any";
}

public class NetworkActionResultDTO
{
    public bool Success { get; set; }
    public int KilledConnections { get; set; }
    public Rule? Rule { get; set; }
    public string Message { get; set; } = string.Empty;
}

using Xvirus;
using Xvirus.Model;

namespace XvirusService.Services;

public class ScannerService
{
    private readonly Scanner _scanner;

    public ScannerService(Scanner scanner)
    {
        _scanner = scanner;
    }

    public ScanResult ScanFile(string filePath) => _scanner.ScanFile(filePath);
}

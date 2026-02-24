using System;

namespace Xvirus.Model;

public class QuarantineEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OriginalFileName { get; set; } = string.Empty;
    public string OriginalFilePath { get; set; } = string.Empty;
    public string QuarantinedFileName { get; set; } = string.Empty;
    public string OriginalAttributes { get; set; } = string.Empty;
    public DateTime OriginalCreationTime { get; set; }
    public DateTime OriginalLastWriteTime { get; set; }
    public DateTime OriginalLastAccessTime { get; set; }
}
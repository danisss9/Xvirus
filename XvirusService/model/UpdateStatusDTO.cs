using System;

namespace XvirusService.Model
{
    public class UpdateStatusDTO
    {
        public string Message { get; set; } = string.Empty;
        public DateTime? LastUpdateCheck { get; set; }
    }
}

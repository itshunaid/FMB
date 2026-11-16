namespace FMB.Domain.Entities
{
    public class TiffinLog
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string HouseholdId { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending"; // Collected, Not Collected, On Hold, Pending
        public string ScanMode { get; set; } = "auto"; // auto, manual, proxy
        public DateTime? ScanTime { get; set; }
        public string? Remarks { get; set; }
    }
}

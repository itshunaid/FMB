namespace FMB.Domain.Entities
{
    public class AuditLog
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string PerformedBy { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string TargetItem { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string? Reason { get; set; }
    }
}

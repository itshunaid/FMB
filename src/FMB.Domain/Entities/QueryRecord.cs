namespace FMB.Domain.Entities
{
    public class QueryRecord
    {
        public int Id { get; set; }
        public string ItsId { get; set; } = string.Empty;
        public string MemberName { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Status { get; set; } = "Open";
        public string? Reply { get; set; }
    }
}

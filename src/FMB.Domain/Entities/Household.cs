namespace FMB.Domain.Entities
{
    public class Household
    {
        public string Id { get; set; } = string.Empty; // ITS ID
        public int ThaliNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Zone { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public int Size { get; set; }
        public string? Status { get; set; } = "Active"; // Active, On Hold, Discontinued
        public string? PasswordHash { get; set; }
        public string? DistributorId { get; set; }
        public Distributor? Distributor { get; set; }
        public string TagId { get; set; } = string.Empty;
        public DateTime? HoldFrom { get; set; }
        public DateTime? HoldTo { get; set; }
    }
}

namespace FMB.Domain.Entities
{
    public class Distributor
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Status { get; set; } = "Active";
        public ICollection<Household> Households { get; set; } = new List<Household>();
    }
}

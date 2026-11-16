namespace FMB.Domain.Entities
{
    public class ExpenseRecord
    {
        public DateTime Date { get; set; }
        public decimal Groceries { get; set; }
        public decimal Meat { get; set; }
        public decimal Gas { get; set; }
        public decimal Labour { get; set; }
        public decimal Packaging { get; set; }
        public decimal Other { get; set; }
        public decimal TotalExpense { get; set; }
        public int ThaliCount { get; set; }
        public string Notes { get; set; } = string.Empty;
    }
}

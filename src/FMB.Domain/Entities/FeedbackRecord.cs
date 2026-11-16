namespace FMB.Domain.Entities;

public class FeedbackRecord
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string MenuName { get; set; } = string.Empty;
    public string ItsId { get; set; } = string.Empty;
    public string MemberName { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string Quantity { get; set; } = "Right";
    public string Quality { get; set; } = "Good";
    public string Comment { get; set; } = string.Empty;
}

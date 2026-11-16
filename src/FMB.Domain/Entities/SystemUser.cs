namespace FMB.Domain.Entities;

public class SystemUser
{
    public string ItsId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = "Volunteer";
    public string Phone { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public string PinHash { get; set; } = string.Empty;
    public DateTime? LastLogin { get; set; }
}

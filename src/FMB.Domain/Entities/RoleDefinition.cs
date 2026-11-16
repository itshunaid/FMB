namespace FMB.Domain.Entities;

public sealed class RoleDefinition
{
    public string Name { get; set; } = string.Empty;
    public string PermissionsJson { get; set; } = "{}";
    public string DashboardWidgetsJson { get; set; } = "[]";
}

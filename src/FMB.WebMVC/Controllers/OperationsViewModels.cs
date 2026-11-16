using System.ComponentModel.DataAnnotations;
using FMB.Domain.Entities;
using FMB.WebMVC.Services;

namespace FMB.WebMVC.Controllers;

public sealed class MenuFormViewModel
{
    [DataType(DataType.Date)]
    public DateTime Date { get; set; } = DateTime.Today;

    [Required, StringLength(200)]
    public string MenuName { get; set; } = string.Empty;
}

public sealed class BulkMenuScheduleViewModel
{
    [DataType(DataType.Date)]
    public DateTime StartDate { get; set; } = DateTime.Today;

    [DataType(DataType.Date)]
    public DateTime EndDate { get; set; } = DateTime.Today.AddDays(6);

    [Required, StringLength(200)]
    public string MenuName { get; set; } = string.Empty;
}

public sealed record MenuRow(DateTime Date, string MenuName);

public sealed class MenusViewModel
{
    public string? Search { get; init; }
    public string? MenuFilter { get; init; }
    public DateTime? SelectedDate { get; init; }
    public IReadOnlyList<string> MenuNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<MenuRow> Rows { get; init; } = Array.Empty<MenuRow>();
    public string? Message { get; init; }
}

public sealed class ExpenseFormViewModel
{
    [DataType(DataType.Date)]
    public DateTime Date { get; set; } = DateTime.Today;

    [Range(typeof(decimal), "0", "999999999")]
    public decimal Groceries { get; set; }

    [Range(typeof(decimal), "0", "999999999")]
    public decimal Meat { get; set; }

    [Range(typeof(decimal), "0", "999999999")]
    public decimal Gas { get; set; }

    [Range(typeof(decimal), "0", "999999999")]
    public decimal Labour { get; set; }

    [Range(typeof(decimal), "0", "999999999")]
    public decimal Packaging { get; set; }

    [Range(typeof(decimal), "0", "999999999")]
    public decimal Other { get; set; }

    [Range(1, 100000)]
    public int ThaliCount { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }
}

public sealed record ExpenseRow(
    DateTime Date,
    string MenuName,
    int ThaliCount,
    decimal Total,
    decimal PerThali,
    int CategoryCount,
    decimal Groceries,
    decimal Meat,
    decimal Gas,
    decimal Labour,
    decimal Packaging,
    decimal Other,
    string Notes);

public sealed class ExpensesViewModel
{
    public string? Search { get; init; }
    public string? MenuFilter { get; init; }
    public DateTime? SelectedDate { get; init; }
    public IReadOnlyList<string> MenuNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ExpenseRow> Rows { get; init; } = Array.Empty<ExpenseRow>();
    public string? Message { get; init; }
}

public sealed class FeedbackFormViewModel
{
    [DataType(DataType.Date)]
    public DateTime Date { get; set; } = DateTime.Today;

    [Required, StringLength(200)]
    public string MenuName { get; set; } = string.Empty;

    [Required, RegularExpression(@"^\d{6,20}$")]
    public string ItsId { get; set; } = string.Empty;

    [Range(1, 5)]
    public int Rating { get; set; } = 5;

    [Required]
    public string Quantity { get; set; } = "Right";

    [Required]
    public string Quality { get; set; } = "Excellent";

    [StringLength(1000)]
    public string? Comment { get; set; }
}

public sealed record FeedbackRow(
    int Id,
    DateTime Date,
    string MenuName,
    string ItsId,
    string MemberName,
    string Zone,
    int Rating,
    string Quantity,
    string Quality,
    string Comment);

public sealed class FeedbackViewModel
{
    public DateTime FormDate { get; init; }
    public string FormMenuName { get; init; } = string.Empty;
    public string FeedbackUrl { get; init; } = string.Empty;
    public string? Search { get; init; }
    public string? MenuFilter { get; init; }
    public string? ZoneFilter { get; init; }
    public int? RatingFilter { get; init; }
    public string? QuantityFilter { get; init; }
    public string? QualityFilter { get; init; }
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
    public IReadOnlyList<string> MenuNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Zones { get; init; } = Array.Empty<string>();
    public IReadOnlyList<FeedbackRow> Rows { get; init; } = Array.Empty<FeedbackRow>();
    public string? Message { get; init; }
}

public sealed class QueryFormViewModel
{
    [Required, RegularExpression(@"^\d{6,20}$")]
    public string ItsId { get; set; } = string.Empty;

    [Required, StringLength(150)]
    public string MemberName { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Subject { get; set; } = string.Empty;

    [Required, StringLength(2000, MinimumLength = 3)]
    public string Message { get; set; } = string.Empty;
}

public sealed class QueryReplyViewModel
{
    public int Id { get; set; }

    [Required, StringLength(2000, MinimumLength = 2)]
    public string Reply { get; set; } = string.Empty;

    [Required]
    public string Status { get; set; } = "Resolved";
}

public sealed record QueryRow(
    int Id,
    string ItsId,
    string MemberName,
    string Subject,
    string Message,
    string Status,
    string? Reply);

public sealed class QueriesViewModel
{
    public string? Search { get; init; }
    public string? StatusFilter { get; init; }
    public IReadOnlyList<QueryRow> Rows { get; init; } = Array.Empty<QueryRow>();
    public string? Message { get; init; }
}

public sealed record NotificationRow(DateTime Timestamp, string Type, string Message);

public sealed class NotificationsViewModel
{
    public string? Search { get; init; }
    public string? TypeFilter { get; init; }
    public DateTime? SelectedDate { get; init; }
    public IReadOnlyList<string> Types { get; init; } = Array.Empty<string>();
    public IReadOnlyList<NotificationRow> Rows { get; init; } = Array.Empty<NotificationRow>();
}

public sealed class UserFormViewModel
{
    [Required, RegularExpression(@"^\d{6,20}$")]
    public string ItsId { get; set; } = string.Empty;

    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = "Volunteer";

    [Phone, StringLength(30)]
    public string? Phone { get; set; }

    [Required]
    public string Status { get; set; } = "Active";

    [StringLength(128, MinimumLength = 4)]
    public string? Pin { get; set; }

    public IReadOnlyList<string> AvailableRoles { get; set; } = RoleAccess.BuiltInRoles;
}

public sealed record UserRow(
    string ItsId,
    string Name,
    string Role,
    string Phone,
    string Status,
    DateTime? LastLogin);

public sealed class UsersViewModel
{
    public string? Search { get; init; }
    public string? RoleFilter { get; init; }
    public string? StatusFilter { get; init; }
    public IReadOnlyList<UserRow> Rows { get; init; } = Array.Empty<UserRow>();
    public IReadOnlyList<string> AvailableRoles { get; init; } = RoleAccess.BuiltInRoles;
    public string? Message { get; init; }
}

public sealed class UserActivityViewModel
{
    public SystemUser User { get; init; } = new();
    public IReadOnlyList<AuditLog> Logs { get; init; } = Array.Empty<AuditLog>();
}

public sealed class AuditLogsViewModel
{
    public string? Search { get; init; }
    public string? PerformedBy { get; init; }
    public string? ActionFilter { get; init; }
    public string? TableFilter { get; init; }
    public IReadOnlyList<string> Performers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Tables { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AuditLog> Rows { get; init; } = Array.Empty<AuditLog>();
    public IReadOnlyList<AuditLog> DeletedRows { get; init; } = Array.Empty<AuditLog>();
}

public sealed class RolesViewModel
{
    public string? Search { get; init; }
    public int TotalRoles { get; init; }
    public IReadOnlyList<RolePermissionRow> Rows { get; init; } = Array.Empty<RolePermissionRow>();
    public bool CanManageRoles { get; init; }
    public string? Message { get; init; }
}

public sealed record RolePermissionRow(
    string Role,
    IReadOnlyDictionary<string, string> Permissions,
    bool IsBuiltIn = true,
    int AssignedUsers = 0,
    int DashboardWidgetCount = 0);

public sealed record RoleDetailsViewModel(
    string Role,
    bool IsBuiltIn,
    int AssignedUsers,
    IReadOnlyDictionary<string, string> Permissions,
    IReadOnlyList<string> DashboardWidgets,
    bool CanManageRoles);

public sealed record RoleActivityViewModel(string Role, IReadOnlyList<AuditLog> Rows);

public sealed class RoleFormViewModel
{
    [Required, StringLength(50, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    public List<string> Permissions { get; set; } = [];
    public List<string> DashboardWidgets { get; set; } = [];

    public bool IsEdit { get; set; }
}

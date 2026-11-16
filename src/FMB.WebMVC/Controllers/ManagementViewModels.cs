using System.ComponentModel.DataAnnotations;
using FMB.Domain.Entities;

namespace FMB.WebMVC.Controllers;

public sealed class HouseholdFormViewModel
{
    [Required, StringLength(20)]
    public string Id { get; set; } = string.Empty;

    public int ThaliNumber { get; set; }

    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(250)]
    public string Address { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Zone { get; set; } = string.Empty;

    [Required, Phone, StringLength(30)]
    public string Phone { get; set; } = string.Empty;

    [Range(1, 100)]
    public int Size { get; set; } = 1;

    [Required]
    public string Status { get; set; } = "Active";

    [Required, StringLength(50)]
    public string TagId { get; set; } = string.Empty;

    public string? DistributorId { get; set; }

    public string? Password { get; set; }
}

public sealed class DistributorFormViewModel
{
    [Required, StringLength(20)]
    public string Id { get; set; } = string.Empty;

    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required, Phone, StringLength(30)]
    public string Phone { get; set; } = string.Empty;

    [Required]
    public string Status { get; set; } = "Active";
}

public sealed record ReconciliationRow(
    string HouseholdId,
    string Name,
    string Zone,
    string TagId,
    string Status,
    string? EntryType,
    DateTime? ScanTime);

public sealed class ReconciliationViewModel
{
    public DateTime Date { get; init; }
    public bool HasMenu { get; init; }
    public bool WindowClosed { get; init; }
    public bool DayFinalized { get; init; }
    public string? Search { get; init; }
    public string? Zone { get; init; }
    public string? StatusFilter { get; init; }
    public IReadOnlyList<string> Zones { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ReconciliationRow> Rows { get; init; } = Array.Empty<ReconciliationRow>();
    public string? Message { get; init; }
}

public sealed class ManualCollectionViewModel
{
    [Required]
    public string HouseholdId { get; set; } = string.Empty;

    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; set; } = string.Empty;
}

public sealed record DailyTrendRow(DateTime Date, int Expected, int Collected, int Rate);
public sealed record MonthlyTrendRow(string Period, int Expected, int Collected, int Rate);
public sealed record ZoneTrendRow(string Zone, int Expected, int Collected, int Rate);
public sealed record DistributorTrendRow(string? DistributorId, string Name, int Expected, int Collected);
public sealed record AnalyticsEntry(
    DateTime Date,
    string HouseholdId,
    string Name,
    string Zone,
    string Status,
    string? EntryType,
    string? DistributorId,
    string? DistributorName,
    bool IsActive);

public sealed class AnalyticsViewModel
{
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public string? Zone { get; init; }
    public string? Status { get; init; }
    public string? EntryType { get; init; }
    public int Expected { get; init; }
    public int Collected { get; init; }
    public int CollectionRate { get; init; }
    public IReadOnlyList<string> Zones { get; init; } = Array.Empty<string>();
    public IReadOnlyList<DailyTrendRow> DailyTrend { get; init; } = Array.Empty<DailyTrendRow>();
    public IReadOnlyList<MonthlyTrendRow> MonthlyTrend { get; init; } = Array.Empty<MonthlyTrendRow>();
    public IReadOnlyList<ZoneTrendRow> ZoneTrends { get; init; } = Array.Empty<ZoneTrendRow>();
    public IReadOnlyList<DistributorTrendRow> DistributorTrends { get; init; } = Array.Empty<DistributorTrendRow>();
    public IReadOnlyList<AnalyticsEntry> Entries { get; init; } = Array.Empty<AnalyticsEntry>();
    public string? Message { get; init; }
}

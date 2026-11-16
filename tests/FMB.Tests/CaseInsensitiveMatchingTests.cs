using FMB.Application.Commands;
using FMB.Application.Queries;
using FMB.Domain.Entities;
using FMB.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMB.Tests;

public class CaseInsensitiveMatchingTests
{
    [Fact]
    public async Task ScanTiffinCommand_AllowsCaseInsensitiveTagIds()
    {
        await using var context = CreateContext();
        context.Households.Add(new Household
        {
            Id = "1",
            Name = "Test Household",
            Status = "Active",
            TagId = "RFID-1001"
        });
        await context.SaveChangesAsync();

        var handler = new ScanTiffinCommandHandler(context);
        var result = await handler.Handle(new ScanTiffinCommand(" rfid-1001 ", "Auto"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Test Household", result.HouseholdName);
    }

    [Fact]
    public async Task DashboardStatsQuery_UsesCaseInsensitiveStatusChecks()
    {
        await using var context = CreateContext();
        context.Households.AddRange(
            new Household { Id = "1", Name = "A", Status = "active", TagId = "RFID-1" },
            new Household { Id = "2", Name = "B", Status = "on hold", TagId = "RFID-2" },
            new Household { Id = "3", Name = "C", Status = "discontinued", TagId = "RFID-3" }
        );
        await context.SaveChangesAsync();

        var handler = new GetDashboardStatsQueryHandler(context);
        var result = await handler.Handle(new GetDashboardStatsQuery(DateTime.Today), CancellationToken.None);

        Assert.Equal(1, result.TotalActive);
        Assert.Equal(1, result.OnHold);
        Assert.Equal(1, result.Pending);
    }

    [Fact]
    public async Task ScanTiffinCommand_RejectsHouseholdsWithWhitespaceAroundStatus()
    {
        await using var context = CreateContext();
        context.Households.Add(new Household
        {
            Id = "1",
            Name = "Held Household",
            Status = "  On Hold  ",
            TagId = "RFID-2001"
        });
        await context.SaveChangesAsync();

        var handler = new ScanTiffinCommandHandler(context);
        var result = await handler.Handle(new ScanTiffinCommand("rfid-2001", "Auto"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("On Hold", result.Message);
    }

    [Fact]
    public async Task ScanTiffinCommand_UsesReaderTimestampAndAuditsDuplicateScans()
    {
        await using var context = CreateContext();
        context.Households.Add(new Household
        {
            Id = "1",
            Name = "Test Household",
            Status = "Active",
            TagId = "RFID-1001"
        });
        await context.SaveChangesAsync();

        var scannedAt = new DateTimeOffset(2026, 11, 15, 17, 7, 42, TimeSpan.FromHours(5.5));
        var handler = new ScanTiffinCommandHandler(context);

        var first = await handler.Handle(
            new ScanTiffinCommand("RFID-1001", "Auto", scannedAt, "GATE-A"),
            CancellationToken.None);
        var duplicate = await handler.Handle(
            new ScanTiffinCommand("rfid-1001", "Auto", scannedAt.AddMinutes(2), "GATE-A"),
            CancellationToken.None);

        Assert.True(first.Success);
        Assert.Equal(scannedAt, first.Timestamp);
        Assert.True(duplicate.Success);
        Assert.Equal(scannedAt.ToUniversalTime(), duplicate.Timestamp);
        Assert.Single(context.TiffinLogs);
        Assert.Equal(2, context.AuditLogs.Count());
        Assert.Contains("Duplicate", context.AuditLogs.OrderBy(log => log.Id).Last().Action);
    }

    [Fact]
    public async Task ScanTiffinCommand_AuditsUnknownTags()
    {
        await using var context = CreateContext();
        var handler = new ScanTiffinCommandHandler(context);

        var result = await handler.Handle(
            new ScanTiffinCommand("UNKNOWN-1", "Auto", DateTimeOffset.UtcNow, "GATE-A"),
            CancellationToken.None);

        Assert.False(result.Success);
        var audit = Assert.Single(context.AuditLogs);
        Assert.Equal("Unknown RFID Tag Scan", audit.Action);
        Assert.Equal("UNKNOWN-1", audit.TargetItem);
        Assert.Contains("GATE-A", audit.Reason);
    }

    [Fact]
    public async Task ScanTiffinCommand_RejectsScansForFinalizedDays()
    {
        await using var context = CreateContext();
        context.Households.Add(new Household
        {
            Id = "1",
            Name = "Test Household",
            Status = "Active",
            TagId = "RFID-1001"
        });
        context.AuditLogs.Add(new AuditLog
        {
            TableName = "DailyOperations",
            Action = "Distribution day finalized",
            TargetItem = "2026-11-15"
        });
        await context.SaveChangesAsync();

        var handler = new ScanTiffinCommandHandler(context);
        var result = await handler.Handle(
            new ScanTiffinCommand(
                "RFID-1001",
                "Auto",
                new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero),
                "GATE-A"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("finalized", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.TiffinLogs);
    }

    [Fact]
    public async Task ScanTiffinCommand_RejectsDiscontinuedHouseholds()
    {
        await using var context = CreateContext();
        context.Households.Add(new Household
        {
            Id = "1",
            Name = "Discontinued Household",
            Status = "Discontinued",
            TagId = "RFID-3001"
        });
        await context.SaveChangesAsync();

        var handler = new ScanTiffinCommandHandler(context);
        var result = await handler.Handle(
            new ScanTiffinCommand("RFID-3001", "Auto"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(context.TiffinLogs);
        Assert.Equal("Scan Rejected - Household Inactive", Assert.Single(context.AuditLogs).Action);
    }

    [Fact]
    public async Task DashboardStatsQuery_IgnoresNullAndWhitespaceStatusValues()
    {
        await using var context = CreateContext();
        context.Households.AddRange(
            new Household { Id = "1", Name = "A", Status = "active", TagId = "RFID-1" },
            new Household { Id = "2", Name = "B", Status = "  ", TagId = "RFID-2" },
            new Household { Id = "3", Name = "C", Status = null, TagId = "RFID-3" }
        );
        await context.SaveChangesAsync();

        var handler = new GetDashboardStatsQueryHandler(context);
        var result = await handler.Handle(new GetDashboardStatsQuery(DateTime.Today), CancellationToken.None);

        Assert.Equal(1, result.TotalActive);
        Assert.Equal(0, result.OnHold);
        Assert.Equal(1, result.Pending);
    }

    [Fact]
    public async Task DashboardStatsQuery_ExcludesInactiveCollectionsFromActiveRate()
    {
        await using var context = CreateContext();
        context.Households.AddRange(
            new Household { Id = "1", Name = "Active", Status = "Active", TagId = "RFID-1" },
            new Household { Id = "2", Name = "Discontinued", Status = "Discontinued", TagId = "RFID-2" });
        context.TiffinLogs.AddRange(
            new TiffinLog { Date = DateTime.Today, HouseholdId = "1", Status = "Collected" },
            new TiffinLog { Date = DateTime.Today, HouseholdId = "2", Status = "Collected" });
        await context.SaveChangesAsync();

        var handler = new GetDashboardStatsQueryHandler(context);
        var result = await handler.Handle(new GetDashboardStatsQuery(DateTime.Today), CancellationToken.None);

        Assert.Equal(1, result.TotalActive);
        Assert.Equal(1, result.Collected);
        Assert.Equal(0, result.Pending);
        Assert.Equal(100, result.CollectionRatePercent);
    }

    [Fact]
    public async Task DashboardStatsQuery_CountsEachCollectedHouseholdOnceForTheWholeDay()
    {
        await using var context = CreateContext();
        context.Households.Add(new Household
        {
            Id = "1",
            Name = "Active",
            Status = "Active",
            TagId = "RFID-1"
        });
        context.TiffinLogs.AddRange(
            new TiffinLog
            {
                Date = new DateTime(2026, 11, 15),
                HouseholdId = "1",
                Status = "Collected"
            },
            new TiffinLog
            {
                Date = new DateTime(2026, 11, 15, 13, 30, 0),
                HouseholdId = "1",
                Status = "Collected"
            });
        await context.SaveChangesAsync();

        var handler = new GetDashboardStatsQueryHandler(context);
        var result = await handler.Handle(
            new GetDashboardStatsQuery(new DateTime(2026, 11, 15)),
            CancellationToken.None);

        Assert.Equal(1, result.Collected);
        Assert.Equal(0, result.Pending);
        Assert.Equal(100, result.CollectionRatePercent);
    }

    private static TiffinTrackerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TiffinTrackerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TiffinTrackerDbContext(options);
    }
}

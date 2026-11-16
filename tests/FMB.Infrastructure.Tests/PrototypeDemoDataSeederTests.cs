using FMB.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FMB.Infrastructure.Tests;

public sealed class PrototypeDemoDataSeederTests
{
    [Fact]
    public async Task SeedAsync_AddsPrototypeOperationsAndIsIdempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TiffinTrackerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new TiffinTrackerDbContext(options);
        await DbInitializer.SeedAsync(context);

        var today = DateTime.Today;
        var initialHouseholdCount = await context.Households.CountAsync();
        var initialLogCount = await context.TiffinLogs.CountAsync();
        var initialMenuCount = await context.DailyMenus.CountAsync();
        var initialFeedbackCount = await context.FeedbackRecords.CountAsync();
        var initialAuditCount = await context.AuditLogs.CountAsync();

        Assert.Equal(370, initialHouseholdCount);
        Assert.Equal(5, await context.Distributors.CountAsync());
        Assert.Equal(67, initialMenuCount);
        Assert.Equal("Chicken Biryani", await context.DailyMenus
            .Where(menu => menu.Date == today)
            .Select(menu => menu.MenuName)
            .SingleAsync());
        Assert.True(initialLogCount >= 45 * 370);
        Assert.Equal(10, await context.ExpenseRecords.CountAsync());
        Assert.Equal(5, await context.QueryRecords.CountAsync());
        Assert.True(initialFeedbackCount > 0);
        Assert.Equal(5, initialAuditCount);
        Assert.Equal(1, await context.Households.CountAsync(household => household.Status == "On Hold"));

        await DbInitializer.SeedAsync(context);

        Assert.Equal(initialHouseholdCount, await context.Households.CountAsync());
        Assert.Equal(initialLogCount, await context.TiffinLogs.CountAsync());
        Assert.Equal(initialMenuCount, await context.DailyMenus.CountAsync());
        Assert.Equal(initialFeedbackCount, await context.FeedbackRecords.CountAsync());
        Assert.Equal(initialAuditCount, await context.AuditLogs.CountAsync());
        Assert.Equal(10, await context.ExpenseRecords.CountAsync());
        Assert.Equal(5, await context.QueryRecords.CountAsync());
    }

    [Fact]
    public async Task SeedAsync_PreservesExistingRecordsWhileFillingMissingPrototypeData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TiffinTrackerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new TiffinTrackerDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var today = DateTime.Today;
        context.Distributors.Add(new FMB.Domain.Entities.Distributor
        {
            Id = "D1",
            Name = "Existing distributor",
            Phone = "1234567890"
        });
        context.Households.Add(new FMB.Domain.Entities.Household
        {
            Id = "30400000",
            ThaliNumber = 1,
            Name = "Existing household",
            Address = "Existing address",
            Zone = "Existing zone",
            Phone = "1234567890",
            Size = 2,
            Status = "Active",
            TagId = "existing-tag"
        });
        context.DailyMenus.Add(new FMB.Domain.Entities.DailyMenu
        {
            Date = today,
            MenuName = "Existing menu"
        });
        context.TiffinLogs.Add(new FMB.Domain.Entities.TiffinLog
        {
            Date = today,
            HouseholdId = "30400000",
            Status = "Not Collected"
        });
        context.ExpenseRecords.Add(new FMB.Domain.Entities.ExpenseRecord
        {
            Date = today.AddDays(-1),
            Groceries = 12,
            TotalExpense = 12
        });
        await context.SaveChangesAsync();

        await DbInitializer.SeedAsync(context);

        Assert.Equal("Existing distributor", await context.Distributors
            .Where(distributor => distributor.Id == "D1")
            .Select(distributor => distributor.Name)
            .SingleAsync());
        Assert.Equal("Existing household", await context.Households
            .Where(household => household.Id == "30400000")
            .Select(household => household.Name)
            .SingleAsync());
        Assert.Equal("Existing menu", await context.DailyMenus
            .Where(menu => menu.Date == today)
            .Select(menu => menu.MenuName)
            .SingleAsync());
        Assert.Equal("Not Collected", await context.TiffinLogs
            .Where(log => log.Date == today && log.HouseholdId == "30400000")
            .Select(log => log.Status)
            .SingleAsync());
        Assert.Equal(12, await context.ExpenseRecords
            .Where(expense => expense.Date == today.AddDays(-1))
            .Select(expense => expense.Groceries)
            .SingleAsync());
    }
}

using FMB.Domain.Entities;
using FMB.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FMB.Infrastructure.Tests;

public sealed class DailyCollectionIndexTests
{
    [Fact]
    public async Task SeedAsync_PreventsMultipleCollectionRowsForOneHouseholdPerDay()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TiffinTrackerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new TiffinTrackerDbContext(options);
        await DbInitializer.SeedAsync(context);
        var householdId = await context.Households.Select(household => household.Id).FirstAsync();
        var today = DateTime.Today;
        context.TiffinLogs.AddRange(
            new TiffinLog { Date = today, HouseholdId = householdId, Status = "Collected" },
            new TiffinLog { Date = today, HouseholdId = householdId, Status = "Collected" });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}

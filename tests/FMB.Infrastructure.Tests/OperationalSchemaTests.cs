using FMB.Domain.Entities;
using FMB.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FMB.Infrastructure.Tests;

public sealed class OperationalSchemaTests
{
    [Fact]
    public async Task SeedAsync_AddsHouseholdPasswordColumnToAnExistingDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TiffinTrackerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new TiffinTrackerDbContext(options);
        await context.Database.EnsureCreatedAsync();
        context.Households.AddRange(
            new Household { Id = "30400001", ThaliNumber = 1, Name = "First", Address = "Test", Zone = "Test", Phone = "1234567890", Size = 1, Status = "Active", TagId = "test-tag-1" },
            new Household { Id = "30400002", ThaliNumber = 2, Name = "Second", Address = "Test", Zone = "Test", Phone = "1234567890", Size = 1, Status = "Active", TagId = "test-tag-2" });
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync(
            "DROP INDEX \"IX_Households_ThaliNumber\"; ALTER TABLE \"Households\" DROP COLUMN \"ThaliNumber\"; ALTER TABLE \"Households\" DROP COLUMN \"PasswordHash\";");

        await DbInitializer.SeedAsync(context, seedDevelopmentData: false);
        var thirdHousehold = new Household
        {
            Id = "30400003",
            Name = "Third",
            Address = "Test address",
            Zone = "Test zone",
            Phone = "1234567890",
            Size = 1,
            Status = "Active",
            TagId = "test-tag-3",
            ThaliNumber = 3,
            PasswordHash = "hashed-password"
        };
        context.Households.Add(thirdHousehold);

        await context.SaveChangesAsync();

        Assert.Equal("hashed-password", await context.Households
            .Where(item => item.Id == thirdHousehold.Id)
            .Select(item => item.PasswordHash)
            .SingleAsync());
        Assert.Equal(new[] { 1, 2, 3 }, await context.Households
            .OrderBy(item => item.Id)
            .Select(item => item.ThaliNumber)
            .ToArrayAsync());
    }

    [Fact]
    public async Task SeedAsync_AddsOperationalTablesToAnExistingDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TiffinTrackerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new TiffinTrackerDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync(
            "DROP TABLE \"FeedbackRecords\"; DROP TABLE \"SystemUsers\"; DROP TABLE \"RoleDefinitions\";");

        await DbInitializer.SeedAsync(context, seedDevelopmentData: false);
        context.FeedbackRecords.Add(new FeedbackRecord
        {
            Date = DateTime.Today,
            MenuName = "Test menu",
            ItsId = "4000001",
            MemberName = "Test member",
            Zone = "Test zone",
            Rating = 5,
            Quantity = "Right",
            Quality = "Good"
        });
        context.SystemUsers.Add(new SystemUser
        {
            ItsId = "4000001",
            Name = "Test user",
            Role = "Admin",
            Status = "Active",
            PinHash = "test-hash"
        });
        context.RoleDefinitions.Add(new RoleDefinition
        {
            Name = "Finance",
            PermissionsJson = """{"dashboard":"v","expenses":"vce"}""",
            DashboardWidgetsJson = """["kpi_fin"]"""
        });

        await context.SaveChangesAsync();

        Assert.Equal(1, await context.FeedbackRecords.CountAsync());
        Assert.Equal(1, await context.SystemUsers.CountAsync());
        Assert.Equal("Finance", await context.RoleDefinitions.Select(item => item.Name).SingleAsync());
    }
}

using FMB.Domain.Entities;
using FMB.Infrastructure.Persistence;
using FMB.WebMVC.Services;
using Microsoft.EntityFrameworkCore;

namespace FMB.Tests;

public sealed class DevelopmentUserSeederTests
{
    [Fact]
    public async Task SeedAsync_AddsScreenshotStaffAccountsWithoutReplacingExistingUsers()
    {
        var options = new DbContextOptionsBuilder<TiffinTrackerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new TiffinTrackerDbContext(options);
        context.SystemUsers.Add(new SystemUser
        {
            ItsId = "40000002",
            Name = "Existing account",
            Role = "Volunteer",
            Status = "Active",
            PinHash = PinCredential.Hash("5678")
        });
        await context.SaveChangesAsync();

        await DevelopmentUserSeeder.SeedAsync(context);

        var accounts = await context.SystemUsers.ToDictionaryAsync(user => user.ItsId);
        Assert.Equal("Murtaza Sagarwala", accounts["30455616"].Name);
        Assert.Equal("Admin", accounts["30455616"].Role);
        Assert.True(PinCredential.Verify("1234", accounts["30455616"].PinHash));
        Assert.Equal("Existing account", accounts["40000002"].Name);
        Assert.True(PinCredential.Verify("5678", accounts["40000002"].PinHash));
        Assert.Equal("Taher Lakdawala", accounts["40000003"].Name);
        Assert.True(PinCredential.Verify("1234", accounts["40000003"].PinHash));
        Assert.Equal("Hatim Contractor", accounts["40000004"].Name);
        Assert.Equal("Juzer Vora", accounts["40000005"].Name);
    }

    [Fact]
    public async Task SeedAsync_AddsPasswordForSecondHouseholdWithoutReplacingExistingPasswords()
    {
        var options = new DbContextOptionsBuilder<TiffinTrackerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new TiffinTrackerDbContext(options);
        context.Households.AddRange(
            new Household { Id = "30400001", Name = "First", ThaliNumber = 1 },
            new Household { Id = "30400002", Name = "Second", ThaliNumber = 2 });
        await context.SaveChangesAsync();

        await DevelopmentUserSeeder.SeedAsync(context);

        var demoHousehold = await context.Households.SingleAsync(item => item.ThaliNumber == 2);
        Assert.True(PinCredential.Verify("1234", demoHousehold.PasswordHash!));
        demoHousehold.PasswordHash = PinCredential.Hash("5678");
        await context.SaveChangesAsync();

        await DevelopmentUserSeeder.SeedAsync(context);

        Assert.True(PinCredential.Verify(
            "5678",
            (await context.Households.SingleAsync(item => item.ThaliNumber == 2)).PasswordHash!));
    }

    [Fact]
    public async Task SeedAsync_AddsDemoPasswordToPrototypeHouseholds()
    {
        var options = new DbContextOptionsBuilder<TiffinTrackerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new TiffinTrackerDbContext(options);
        context.Households.Add(new Household
        {
            Id = "30400000",
            Name = "Prototype household",
            ThaliNumber = 1,
            TagId = "E280-1000"
        });
        await context.SaveChangesAsync();

        await DevelopmentUserSeeder.SeedAsync(context);

        var household = await context.Households.SingleAsync();
        Assert.True(PinCredential.Verify("1234", household.PasswordHash!));
    }
}

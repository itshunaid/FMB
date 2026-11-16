using FMB.Domain.Entities;
using FMB.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMB.WebMVC.Services;

public static class DevelopmentUserSeeder
{
    public static async Task SeedAsync(TiffinTrackerDbContext context, CancellationToken cancellationToken = default)
    {
        var demoUsers = new[]
        {
            new SystemUser
            {
                ItsId = "30455616",
                Name = "Murtaza Sagarwala",
                Role = "Admin",
                Phone = string.Empty,
                Status = "Active",
                PinHash = PinCredential.Hash("1234")
            },
            new SystemUser
            {
                ItsId = "40000002",
                Name = "Zoeb Merchant",
                Role = "Volunteer",
                Phone = string.Empty,
                Status = "Active",
                PinHash = PinCredential.Hash("1234")
            },
            new SystemUser
            {
                ItsId = "40000003",
                Name = "Taher Lakdawala",
                Role = "Back Office",
                Phone = string.Empty,
                Status = "Active",
                PinHash = PinCredential.Hash("1234")
            },
            new SystemUser
            {
                ItsId = "40000004",
                Name = "Hatim Contractor",
                Role = "Volunteer",
                Phone = string.Empty,
                Status = "Active",
                PinHash = PinCredential.Hash("1234")
            },
            new SystemUser
            {
                ItsId = "40000005",
                Name = "Juzer Vora",
                Role = "Back Office",
                Phone = string.Empty,
                Status = "Active",
                PinHash = PinCredential.Hash("1234")
            }
        };

        foreach (var demoUser in demoUsers)
        {
            if (!await context.SystemUsers.AnyAsync(user => user.ItsId == demoUser.ItsId, cancellationToken))
            {
                context.SystemUsers.Add(demoUser);
            }
        }

        var prototypeHouseholds = await context.Households
            .Where(household => household.TagId.StartsWith("E280-"))
            .ToListAsync(cancellationToken);
        foreach (var household in prototypeHouseholds)
        {
            if (string.IsNullOrWhiteSpace(household.PasswordHash))
            {
                household.PasswordHash = PinCredential.Hash("1234");
            }
        }

        var demoHousehold = await context.Households.OrderBy(household => household.ThaliNumber)
            .Skip(1).FirstOrDefaultAsync(cancellationToken)
            ?? await context.Households.OrderBy(household => household.ThaliNumber)
                .FirstOrDefaultAsync(cancellationToken);
        if (demoHousehold is not null && string.IsNullOrWhiteSpace(demoHousehold.PasswordHash))
        {
            demoHousehold.PasswordHash = PinCredential.Hash("1234");
        }

        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}

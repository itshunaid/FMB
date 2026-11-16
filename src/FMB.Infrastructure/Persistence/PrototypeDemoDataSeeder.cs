using FMB.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FMB.Infrastructure.Persistence;

internal static class PrototypeDemoDataSeeder
{
    private static readonly string[] FirstNames =
    [
        "Abbas", "Ali", "Hatim", "Huzefa", "Husain", "Idris", "Juzer", "Mustafa",
        "Qutbuddin", "Taher", "Yusuf", "Zoeb", "Fakhri", "Shabbir", "Kaizar",
        "Burhan", "Saifee", "Aziz", "Moiz", "Taha"
    ];

    private static readonly string[] LastNames =
    [
        "Sagarwala", "Kapadia", "Rangwala", "Nalwala", "Ezzy", "Master",
        "Contractor", "Merchant", "Bhopalwala", "Lakdawala", "Vora", "Tinwala",
        "Dhinojwala", "Jiwaji"
    ];

    private static readonly string[] Zones =
    [
        "Hussaini Alam", "Charminar", "Yakutpura", "Moghalpura", "Dabeerpura", "Falaknuma"
    ];

    private static readonly string[] Menus =
    [
        "Dal Chawal Palidu", "Chicken Biryani", "Mutton Khichda", "Kheema Roti",
        "Veg Pulao Raita", "Sev Usal Roti", "Haleem Naan"
    ];

    private static readonly (string Id, string Name, string Phone)[] DistributorData =
    [
        ("D1", "Hunaid Kothari", "9848010100"),
        ("D2", "Nasir Bhai Shakir", "9848010137"),
        ("D3", "Ammar Dahodwala", "9848010174"),
        ("D4", "Zaid Poonawala", "9848010211"),
        ("D5", "Munir Bhai Ladiwala", "9848010248")
    ];

    public static async Task SeedAsync(TiffinTrackerDbContext context)
    {
        await SeedDistributorsAsync(context);

        var existingHouseholds = await context.Households
            .Select(household => new { household.Id, household.ThaliNumber, household.TagId })
            .ToListAsync();
        var householdIds = existingHouseholds.Select(household => household.Id).ToHashSet();
        var thaliNumbers = existingHouseholds
            .Where(household => household.ThaliNumber > 0)
            .Select(household => household.ThaliNumber)
            .ToHashSet();
        var tags = existingHouseholds.Select(household => household.TagId).ToHashSet(StringComparer.Ordinal);
        var nextThaliNumber = thaliNumbers.DefaultIfEmpty(0).Max() + 1;
        var random = new PrototypeRandom();
        var sourceHouseholds = new List<SourceHousehold>(370);
        var householdsToAdd = new List<Household>(370);

        for (var index = 0; index < 370; index++)
        {
            var source = new SourceHousehold(
                Id: (30400000 + index * 7).ToString(),
                Name: $"{FirstNames[index % FirstNames.Length]} {LastNames[(index * 3 + index / 20) % LastNames.Length]}",
                Address: $"{10 + index % 90}-{2 + index % 7}-{100 + index}, Hyderabad",
                Zone: Zones[index % Zones.Length],
                Phone: $"9{100000000L + index * 2713L}",
                Size: 2 + index % 6,
                DistributorId: index < 140 ? DistributorData[index % DistributorData.Length].Id : null,
                TagId: $"E280-{1000 + index}",
                CollectionProbability: 0.68 + random.NextDouble() * 0.31);
            sourceHouseholds.Add(source);

            if (householdIds.Contains(source.Id))
            {
                continue;
            }

            var thaliNumber = index + 1;
            if (thaliNumbers.Contains(thaliNumber))
            {
                while (thaliNumbers.Contains(nextThaliNumber))
                {
                    nextThaliNumber++;
                }
                thaliNumber = nextThaliNumber++;
            }
            thaliNumbers.Add(thaliNumber);

            var tagId = source.TagId;
            while (!tags.Add(tagId))
            {
                tagId += "-DEMO";
            }

            var onHold = index == 9;
            householdsToAdd.Add(new Household
            {
                Id = source.Id,
                ThaliNumber = thaliNumber,
                Name = source.Name,
                Address = source.Address,
                Zone = source.Zone,
                Phone = source.Phone,
                Size = source.Size,
                Status = onHold ? "On Hold" : "Active",
                DistributorId = source.DistributorId,
                TagId = tagId,
                HoldFrom = onHold ? DateTime.Today : null,
                HoldTo = onHold ? DateTime.Today.AddDays(6) : null
            });
        }

        if (householdsToAdd.Count > 0)
        {
            context.Households.AddRange(householdsToAdd);
            await context.SaveChangesAsync();
        }

        var menusByDate = await context.DailyMenus
            .Select(menu => menu.Date)
            .ToListAsync();
        var menuDates = menusByDate.Select(date => DateOnly.FromDateTime(date)).ToHashSet();
        var menusToAdd = new List<DailyMenu>();
        var today = DateOnly.FromDateTime(DateTime.Today);

        for (var dayIndex = 0; dayIndex < 45; dayIndex++)
        {
            var date = today.AddDays(dayIndex - 45);
            AddMenuIfMissing(date, Menus[dayIndex % Menus.Length], menuDates, menusToAdd);
        }

        AddMenuIfMissing(today, "Chicken Biryani", menuDates, menusToAdd);
        for (var dayIndex = 1; dayIndex <= 21; dayIndex++)
        {
            AddMenuIfMissing(today.AddDays(dayIndex), Menus[(dayIndex - 1) % Menus.Length], menuDates, menusToAdd);
        }

        if (menusToAdd.Count > 0)
        {
            context.DailyMenus.AddRange(menusToAdd);
            await context.SaveChangesAsync();
        }

        var existingLogKeys = (await context.TiffinLogs
                .Select(log => new { log.Date, log.HouseholdId })
                .ToListAsync())
            .Select(log => (DateOnly.FromDateTime(log.Date), log.HouseholdId))
            .ToHashSet();
        var currentDayStatuses = (await context.TiffinLogs
                .Where(log => log.Date == today.ToDateTime(TimeOnly.MinValue))
                .Select(log => new { log.HouseholdId, log.Status })
                .ToListAsync())
            .ToDictionary(log => log.HouseholdId, log => log.Status);
        var logsToAdd = new List<TiffinLog>(45 * sourceHouseholds.Count);
        var historicalLogs = new Dictionary<DateOnly, List<TiffinLog>>();

        for (var dayIndex = 0; dayIndex < 45; dayIndex++)
        {
            var date = today.AddDays(dayIndex - 45);
            var dayLogs = new List<TiffinLog>(sourceHouseholds.Count);
            foreach (var household in sourceHouseholds)
            {
                TiffinLog log;
                if (random.NextDouble() < 0.015)
                {
                    log = new TiffinLog
                    {
                        Date = date.ToDateTime(TimeOnly.MinValue),
                        HouseholdId = household.Id,
                        Status = "On Hold"
                    };
                }
                else if (random.NextDouble() < household.CollectionProbability)
                {
                    var automatic = random.NextDouble() < 0.94;
                    log = new TiffinLog
                    {
                        Date = date.ToDateTime(TimeOnly.MinValue),
                        HouseholdId = household.Id,
                        Status = "Collected",
                        ScanMode = automatic ? "auto" : "manual",
                        ScanTime = date.ToDateTime(random.NextTime()),
                        Remarks = automatic ? null : "Gate miss — confirmed manually"
                    };
                }
                else
                {
                    log = new TiffinLog
                    {
                        Date = date.ToDateTime(TimeOnly.MinValue),
                        HouseholdId = household.Id,
                        Status = "Not Collected"
                    };
                }

                dayLogs.Add(log);
                AddLogIfMissing(log, existingLogKeys, logsToAdd);
            }
            historicalLogs.Add(date, dayLogs);
        }

        foreach (var household in sourceHouseholds)
        {
            if (household.Id == (30400000 + 9 * 7).ToString())
            {
                continue;
            }
            if (random.NextDouble() >= household.CollectionProbability * 0.55)
            {
                continue;
            }

            var log = new TiffinLog
            {
                Date = today.ToDateTime(TimeOnly.MinValue),
                HouseholdId = household.Id,
                Status = "Collected",
                ScanMode = "auto",
                ScanTime = today.ToDateTime(random.NextTime())
            };
            if (!currentDayStatuses.ContainsKey(household.Id))
            {
                AddLogIfMissing(log, existingLogKeys, logsToAdd);
                currentDayStatuses[household.Id] = log.Status;
            }
        }

        if (logsToAdd.Count > 0)
        {
            context.TiffinLogs.AddRange(logsToAdd);
            await context.SaveChangesAsync();
        }

        await SeedExpensesAsync(context, today, historicalLogs);
        await SeedFeedbackAsync(context, today, sourceHouseholds, historicalLogs, currentDayStatuses);
        await SeedQueriesAsync(context, sourceHouseholds);
        await SeedAuditLogsAsync(context, today);
    }

    private static async Task SeedDistributorsAsync(TiffinTrackerDbContext context)
    {
        var existingIds = (await context.Distributors.Select(distributor => distributor.Id).ToListAsync())
            .ToHashSet(StringComparer.Ordinal);
        var distributorsToAdd = DistributorData
            .Where(distributor => !existingIds.Contains(distributor.Id))
            .Select(distributor => new Distributor
            {
                Id = distributor.Id,
                Name = distributor.Name,
                Phone = distributor.Phone,
                Status = "Active"
            })
            .ToList();

        if (distributorsToAdd.Count > 0)
        {
            context.Distributors.AddRange(distributorsToAdd);
            await context.SaveChangesAsync();
        }
    }

    private static void AddMenuIfMissing(
        DateOnly date,
        string menuName,
        HashSet<DateOnly> existingDates,
        List<DailyMenu> menusToAdd)
    {
        if (existingDates.Add(date))
        {
            menusToAdd.Add(new DailyMenu { Date = date.ToDateTime(TimeOnly.MinValue), MenuName = menuName });
        }
    }

    private static void AddLogIfMissing(
        TiffinLog log,
        HashSet<(DateOnly Date, string HouseholdId)> existingKeys,
        List<TiffinLog> logsToAdd)
    {
        var key = (DateOnly.FromDateTime(log.Date), log.HouseholdId);
        if (existingKeys.Add(key))
        {
            logsToAdd.Add(log);
        }
    }

    private static async Task SeedExpensesAsync(
        TiffinTrackerDbContext context,
        DateOnly today,
        IReadOnlyDictionary<DateOnly, List<TiffinLog>> historicalLogs)
    {
        var existingDates = (await context.ExpenseRecords.Select(expense => expense.Date).ToListAsync())
            .Select(date => DateOnly.FromDateTime(date))
            .ToHashSet();
        var expenses = new List<ExpenseRecord>();

        for (var daysAgo = 10; daysAgo >= 1; daysAgo--)
        {
            var date = today.AddDays(-daysAgo);
            if (existingDates.Contains(date))
            {
                continue;
            }

            var random = new Random(date.DayNumber);
            var groceries = random.Next(1500, 4001);
            var meat = random.Next(1800, 4501);
            var gas = random.Next(450, 1101);
            var labour = random.Next(900, 2201);
            var packaging = random.Next(350, 901);
            var other = random.Next(150, 701);
            expenses.Add(new ExpenseRecord
            {
                Date = date.ToDateTime(TimeOnly.MinValue),
                Groceries = groceries,
                Meat = meat,
                Gas = gas,
                Labour = labour,
                Packaging = packaging,
                Other = other,
                TotalExpense = groceries + meat + gas + labour + packaging + other,
                ThaliCount = historicalLogs[date].Count(log => log.Status == "Collected"),
                Notes = "Prototype demo kitchen expenses"
            });
        }

        if (expenses.Count > 0)
        {
            context.ExpenseRecords.AddRange(expenses);
            await context.SaveChangesAsync();
        }
    }

    private static async Task SeedFeedbackAsync(
        TiffinTrackerDbContext context,
        DateOnly today,
        IReadOnlyList<SourceHousehold> households,
        IReadOnlyDictionary<DateOnly, List<TiffinLog>> historicalLogs,
        IReadOnlyDictionary<string, string> currentDayStatuses)
    {
        var existingRecords = await context.FeedbackRecords
            .Select(record => new { record.Date, record.ItsId })
            .ToListAsync();
        var existingKeys = existingRecords
            .Select(record => (DateOnly.FromDateTime(record.Date), record.ItsId))
            .ToHashSet();
        var householdById = households.ToDictionary(household => household.Id);
        var feedbackToAdd = new List<FeedbackRecord>();
        var comments = new[]
        {
            "Good taste and quality.",
            "Please keep the portion size consistent.",
            "The meal was fresh and well packed.",
            "Please add a little more variety."
        };

        for (var daysAgo = 10; daysAgo >= 1; daysAgo--)
        {
            var date = today.AddDays(-daysAgo);
            var random = new Random(date.DayNumber ^ 0x5F3759DF);
            foreach (var log in historicalLogs[date])
            {
                if (log.Status != "Collected" || random.NextDouble() >= 0.12)
                {
                    continue;
                }
                var household = householdById[log.HouseholdId];
                AddFeedbackIfMissing(
                    new FeedbackRecord
                    {
                        Date = date.ToDateTime(TimeOnly.MinValue),
                        MenuName = Menus[Math.Abs(date.DayNumber) % Menus.Length],
                        ItsId = household.Id,
                        MemberName = household.Name,
                        Zone = household.Zone,
                        Rating = random.Next(3, 6),
                        Quantity = "Right",
                        Quality = "Good",
                        Comment = comments[random.Next(comments.Length)]
                    },
                    existingKeys,
                    feedbackToAdd);
            }
        }

        var currentMenu = "Chicken Biryani";
        var currentCollectors = households
            .Where(household => currentDayStatuses.TryGetValue(household.Id, out var status) && status == "Collected")
            .Take(4)
            .Where(household => !existingKeys.Contains((today, household.Id)))
            .ToList();
        var ratings = new[] { 5, 4, 3, 5 };
        for (var index = 0; index < currentCollectors.Count; index++)
        {
            var household = currentCollectors[index];
            feedbackToAdd.Add(new FeedbackRecord
            {
                Date = today.ToDateTime(TimeOnly.MinValue),
                MenuName = currentMenu,
                ItsId = household.Id,
                MemberName = household.Name,
                Zone = household.Zone,
                Rating = ratings[index],
                Quantity = "Right",
                Quality = "Good",
                Comment = comments[index]
            });
            existingKeys.Add((today, household.Id));
        }

        if (feedbackToAdd.Count > 0)
        {
            context.FeedbackRecords.AddRange(feedbackToAdd);
            await context.SaveChangesAsync();
        }
    }

    private static void AddFeedbackIfMissing(
        FeedbackRecord record,
        HashSet<(DateOnly Date, string ItsId)> existingKeys,
        List<FeedbackRecord> recordsToAdd)
    {
        var key = (DateOnly.FromDateTime(record.Date), record.ItsId);
        if (existingKeys.Add(key))
        {
            recordsToAdd.Add(record);
        }
    }

    private static async Task SeedQueriesAsync(
        TiffinTrackerDbContext context,
        IReadOnlyList<SourceHousehold> households)
    {
        var sampleQueries = new[]
        {
            ("Please update my delivery address", "I have moved within Hussaini Alam. Please confirm the address change.", "Open", (string?)null),
            ("Tiffin not received", "My household did not receive yesterday's tiffin.", "In Progress", (string?)null),
            ("Pause service", "Please pause the service for the coming week.", "Resolved", "Your service pause has been recorded."),
            ("Update phone number", "Please update my registered contact number.", "Open", (string?)null),
            ("Food feedback", "Please include less spice in the next menu.", "Resolved", "Thank you. The kitchen team has been informed.")
        };
        var existingKeys = (await context.QueryRecords
                .Select(query => new { query.ItsId, query.Subject })
                .ToListAsync())
            .Select(query => (query.ItsId, query.Subject))
            .ToHashSet();
        var queriesToAdd = new List<QueryRecord>();

        for (var index = 0; index < sampleQueries.Length; index++)
        {
            var household = households[index * 37];
            var (subject, message, status, reply) = sampleQueries[index];
            if (existingKeys.Add((household.Id, subject)))
            {
                queriesToAdd.Add(new QueryRecord
                {
                    ItsId = household.Id,
                    MemberName = household.Name,
                    Subject = subject,
                    Message = message,
                    Status = status,
                    Reply = reply
                });
            }
        }

        if (queriesToAdd.Count > 0)
        {
            context.QueryRecords.AddRange(queriesToAdd);
            await context.SaveChangesAsync();
        }
    }

    private static async Task SeedAuditLogsAsync(TiffinTrackerDbContext context, DateOnly today)
    {
        var sampleEntries = new[]
        {
            ("Households", "Household added", "30400000", "Demo household imported"),
            ("Distributors", "Distributor added", "D1", "Prototype distributor imported"),
            ("Menus", "Menu published", today.ToString("yyyy-MM-dd"), "Chicken Biryani"),
            ("Expenses", "Expense recorded", today.AddDays(-1).ToString("yyyy-MM-dd"), "Prototype demo data"),
            ("Queries", "Query replied", "Demo query", "Back office reply seeded")
        };
        var existingEntries = await context.AuditLogs
            .Select(log => new { log.PerformedBy, log.Action, log.TargetItem, log.TableName })
            .ToListAsync();
        var existingKeys = existingEntries
            .Select(log => (log.PerformedBy, log.Action, log.TargetItem, log.TableName))
            .ToHashSet();
        var logsToAdd = new List<AuditLog>();

        for (var index = 0; index < sampleEntries.Length; index++)
        {
            var (tableName, action, targetItem, reason) = sampleEntries[index];
            var key = ("Demo Seed", action, targetItem, tableName);
            if (existingKeys.Add(key))
            {
                logsToAdd.Add(new AuditLog
                {
                    Timestamp = today.ToDateTime(new TimeOnly(11, 45 + index)),
                    PerformedBy = "Demo Seed",
                    Action = action,
                    TargetItem = targetItem,
                    TableName = tableName,
                    Reason = reason
                });
            }
        }

        if (logsToAdd.Count > 0)
        {
            context.AuditLogs.AddRange(logsToAdd);
            await context.SaveChangesAsync();
        }
    }

    private sealed record SourceHousehold(
        string Id,
        string Name,
        string Address,
        string Zone,
        string Phone,
        int Size,
        string? DistributorId,
        string TagId,
        double CollectionProbability);

    private sealed class PrototypeRandom
    {
        private uint _state = 7;

        public double NextDouble()
        {
            _state = unchecked(_state + 0x6D2B79F5u);
            var value = unchecked((_state ^ (_state >> 15)) * (1u | _state));
            value = unchecked((value + unchecked((value ^ (value >> 7)) * (61u | value))) ^ value);
            return (value ^ (value >> 14)) / 4294967296d;
        }

        public TimeOnly NextTime()
        {
            var hour = 12 + (int)(NextDouble() * 2);
            var minute = (int)(NextDouble() * 60);
            return new TimeOnly(hour, minute);
        }
    }
}

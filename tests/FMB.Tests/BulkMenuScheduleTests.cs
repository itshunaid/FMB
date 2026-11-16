using System.Diagnostics.CodeAnalysis;
using FMB.Domain.Entities;
using FMB.Infrastructure.Persistence;
using FMB.WebMVC.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

namespace FMB.Tests;

public sealed class BulkMenuScheduleTests
{
    [Fact]
    public async Task ScheduleSkipsDatesWithExistingMenusOrFinalizedOperations()
    {
        await using var db = CreateContext();
        var start = DateTime.Today.AddDays(10);
        var finalizedDate = start.AddDays(1);
        db.DailyMenus.Add(new DailyMenu { Date = start, MenuName = "Existing menu" });
        db.AuditLogs.Add(new AuditLog
        {
            TableName = "DailyOperations",
            Action = "Distribution day finalized",
            TargetItem = finalizedDate.ToString("yyyy-MM-dd")
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.BulkScheduleMenus(
            new BulkMenuScheduleViewModel
            {
                StartDate = start,
                EndDate = start.AddDays(2),
                MenuName = "Chicken Biryani"
            },
            CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(2, await db.DailyMenus.CountAsync());
        Assert.Equal("Existing menu", await db.DailyMenus
            .Where(item => item.Date == start)
            .Select(item => item.MenuName)
            .SingleAsync());
        Assert.Equal("Chicken Biryani", await db.DailyMenus
            .Where(item => item.Date == start.AddDays(2))
            .Select(item => item.MenuName)
            .SingleAsync());
        var audit = await db.AuditLogs.SingleAsync(item => item.Action == "Menus bulk scheduled");
        Assert.Contains("1 date(s) scheduled", audit.Reason);
    }

    [Fact]
    public async Task ScheduleRejectsRangesLongerThan366Days()
    {
        await using var db = CreateContext();
        var controller = CreateController(db);

        var result = await controller.BulkScheduleMenus(
            new BulkMenuScheduleViewModel
            {
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(366),
                MenuName = "Chicken Biryani"
            },
            CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.Empty(await db.DailyMenus.ToListAsync());
        Assert.False(controller.ModelState.IsValid);
    }

    private static TiffinTrackerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TiffinTrackerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TiffinTrackerDbContext(options);
    }

    private static OperationsController CreateController(TiffinTrackerDbContext db)
    {
        var httpContext = new DefaultHttpContext { Session = new TestSession() };
        var controller = new OperationsController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };
        return controller;
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> values = new(StringComparer.Ordinal);

        public string Id => "test-session";
        public bool IsAvailable => true;
        public IEnumerable<string> Keys => values.Keys;

        public void Clear() => values.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => values.Remove(key);
        public void Set(string key, byte[] value) => values[key] = value;
        public bool TryGetValue(string key, [NotNullWhen(true)] out byte[]? value) =>
            values.TryGetValue(key, out value);
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}

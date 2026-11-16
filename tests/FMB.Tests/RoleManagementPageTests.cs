using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using FMB.Domain.Entities;
using FMB.Infrastructure.Persistence;
using FMB.WebMVC.Controllers;
using FMB.WebMVC.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

namespace FMB.Tests;

[Collection("Role configuration")]
public sealed class RoleManagementPageTests
{
    [Fact]
    public async Task RolesPageSearchShowsUserModuleAndWidgetCounts()
    {
        await using var db = CreateContext();
        db.SystemUsers.AddRange(
            CreateUser("400001", "Volunteer A", "Volunteer"),
            CreateUser("400002", "Volunteer B", "Volunteer"),
            CreateUser("400003", "Back Office", "Back Office"));
        await db.SaveChangesAsync();

        var result = await CreateController(db).Roles("vol", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<RolesViewModel>(view.Model);
        var row = Assert.Single(model.Rows);
        Assert.Equal("Volunteer", row.Role);
        Assert.Equal(2, row.AssignedUsers);
        Assert.Equal(10, row.Permissions.Count(module => module.Value.Length > 0));
        Assert.Equal(5, row.DashboardWidgetCount);
        Assert.Equal(3, model.TotalRoles);
    }

    [Fact]
    public async Task RoleActivityPageOnlyShowsChangesForTheRequestedRole()
    {
        await using var db = CreateContext();
        db.AuditLogs.AddRange(
            new AuditLog { Action = "Role created", TargetItem = "Volunteer", TableName = "Roles" },
            new AuditLog { Action = "Role updated", TargetItem = "Back Office", TableName = "Roles" },
            new AuditLog { Action = "System user updated", TargetItem = "400001", TableName = "Users" });
        await db.SaveChangesAsync();

        var result = await CreateController(db).RoleActivity("Volunteer", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<RoleActivityViewModel>(view.Model);
        Assert.Equal("Volunteer", model.Role);
        Assert.Equal("Role created", Assert.Single(model.Rows).Action);
    }

    [Fact]
    public async Task AdminCanPersistPermissionAndWidgetChangesForBuiltInStaffRoles()
    {
        await using var db = CreateContext();
        var originalPermissions = JsonSerializer.Serialize(RoleAccess.GetPermissions("Back Office"));
        var originalWidgets = JsonSerializer.Serialize(RoleAccess.GetDashboardWidgets("Back Office"));
        var controller = CreateController(db);

        try
        {
            var result = await controller.EditRole(
                "Back Office",
                new RoleFormViewModel
                {
                    Name = "Back Office",
                    Permissions = ["dashboard|v", "gate|v"],
                    DashboardWidgets = ["tasks"]
                },
                CancellationToken.None);

            Assert.IsType<RedirectToActionResult>(result);
            Assert.True(RoleAccess.CanAccessAction("Back Office", "Management", "Gate"));
            Assert.Equal(new[] { "tasks" }, RoleAccess.GetDashboardWidgets("Back Office"));
            var saved = await db.RoleDefinitions.SingleAsync(role => role.Name == "Back Office");
            Assert.Contains("gate", saved.PermissionsJson);
            var audit = await db.AuditLogs.SingleAsync(log => log.Action == "Role updated");
            Assert.Equal("Roles", audit.TableName);
        }
        finally
        {
            RoleAccess.RegisterBuiltInRoleOverride("Back Office", originalPermissions, originalWidgets);
        }
    }

    private static SystemUser CreateUser(string itsId, string name, string role) => new()
    {
        ItsId = itsId,
        Name = name,
        Role = role,
        Phone = "0000000000",
        Status = "Active",
        PinHash = "test-hash"
    };

    private static TiffinTrackerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TiffinTrackerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TiffinTrackerDbContext(options);
    }

    [CollectionDefinition("Role configuration")]
    public sealed class RoleConfigurationCollection
    {
    }

    private static OperationsController CreateController(TiffinTrackerDbContext db)
    {
        var httpContext = new DefaultHttpContext
        {
            Session = new TestSession()
        };
        httpContext.Session.SetString("FMB.Auth.User", "30455616|StaffUser|Murtaza Sagarwala|Admin");
        return new OperationsController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> values = new(StringComparer.Ordinal);

        public string Id => "role-page-test";
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

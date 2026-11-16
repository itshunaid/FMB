using System.Text.Json;
using FMB.WebMVC.Services;

namespace FMB.Tests;

[Collection("Role configuration")]
public sealed class RoleAccessTests
{
    [Fact]
    public void AdminCanAccessEveryOperationsModule()
    {
        Assert.True(RoleAccess.CanAccessAction("Admin", "Management", "Analytics"));
        Assert.True(RoleAccess.CanAccessAction("Admin", "Operations", "DeleteUser"));
        Assert.True(RoleAccess.CanAccessAction("Admin", "Operations", "Roles"));
    }

    [Fact]
    public void VolunteerCanOperateTheGateButCannotManageUsersOrExport()
    {
        Assert.True(RoleAccess.CanAccessAction("Volunteer", "Management", "Gate"));
        Assert.True(RoleAccess.CanAccessAction("Volunteer", "Home", "SimulateScan"));
        Assert.False(RoleAccess.CanAccessAction("Volunteer", "Operations", "Users"));
        Assert.False(RoleAccess.CanAccessAction("Volunteer", "Operations", "ExportHouseholds"));
        Assert.False(RoleAccess.CanAccessAction("Volunteer", "Management", "Analytics"));
    }

    [Fact]
    public void BackOfficeCanManageExpensesAndQueriesButNotGateOperations()
    {
        Assert.True(RoleAccess.CanAccessAction("Back Office", "Operations", "CreateExpense"));
        Assert.True(RoleAccess.CanAccessAction("Back Office", "Operations", "ReplyQuery"));
        Assert.False(RoleAccess.CanAccessAction("Back Office", "Management", "Gate"));
        Assert.False(RoleAccess.CanAccessAction("Back Office", "Management", "Reconciliation"));
    }

    [Fact]
    public void OnlyRolesWithReconciliationAccessCanCloseOrFinalizeADay()
    {
        Assert.True(RoleAccess.CanAccessAction("Admin", "Management", "CloseWindow"));
        Assert.True(RoleAccess.CanAccessAction("Volunteer", "Management", "CloseWindow"));
        Assert.True(RoleAccess.CanAccessAction("Volunteer", "Management", "FinalizeDay"));
        Assert.False(RoleAccess.CanAccessAction("Back Office", "Management", "FinalizeDay"));
    }

    [Fact]
    public void BulkMenuSchedulingRequiresMenuCreatePermission()
    {
        Assert.True(RoleAccess.CanAccessAction("Admin", "Operations", "BulkScheduleMenus"));
        Assert.False(RoleAccess.CanAccessAction("Volunteer", "Operations", "BulkScheduleMenus"));
        Assert.False(RoleAccess.CanAccessAction("Back Office", "Operations", "BulkScheduleMenus"));
    }

    [Fact]
    public void UnknownRolesAndHouseholdSessionsHaveNoStaffPermissions()
    {
        Assert.False(RoleAccess.IsKnownRole("Household"));
        Assert.False(RoleAccess.CanAccessAction("Household", "Management", "Households"));
        Assert.False(RoleAccess.CanAccessAction("Volunteer", "Home", "GetTab", "aud"));
    }

    [Fact]
    public void CustomRolePermissionsControlOperationsAndAssignedRolePrivilege()
    {
        const string role = "Test Expense Clerk";
        RoleAccess.RegisterCustomRole(
            role,
            """{"dashboard":"v","expenses":"vce"}""",
            """["kpi_fin","tasks","kpi_ops"]""");

        try
        {
            Assert.True(RoleAccess.CanAccessAction(role, "Operations", "CreateExpense"));
            Assert.False(RoleAccess.CanAccessAction(role, "Operations", "DeleteExpense"));
            Assert.False(RoleAccess.CanAccessAction(role, "Management", "Analytics"));
            Assert.Equal(new[] { "kpi_fin", "tasks", "kpi_ops" }, RoleAccess.GetDashboardWidgets(role));
            Assert.Equal(new[] { "kpi_fin", "tasks" }, RoleAccess.GetVisibleDashboardWidgets(role));
            Assert.False(RoleAccess.CanAssignRole(role, "Admin"));
            Assert.True(RoleAccess.CanAssignRole("Admin", role));
        }
        finally
        {
            RoleAccess.RemoveCustomRole(role);
        }
    }

    [Fact]
    public void OnlyAdminCanCreateOrModifyRoles()
    {
        Assert.False(RoleAccess.CanAccessAction("Volunteer", "Operations", "CreateRole"));
        Assert.False(RoleAccess.CanAccessAction("Back Office", "Operations", "EditRole"));
        Assert.False(RoleAccess.CanAccessAction("Volunteer", "Operations", "DeleteRole"));
        Assert.True(RoleAccess.CanAccessAction("Admin", "Operations", "CreateRole"));
    }

    [Fact]
    public void AdminCanCustomizeNonAdminBuiltInRolesWithoutChangingAdmin()
    {
        var permissions = JsonSerializer.Serialize(RoleAccess.GetPermissions("Volunteer"));
        var widgets = JsonSerializer.Serialize(RoleAccess.GetDashboardWidgets("Volunteer"));

        try
        {
            RoleAccess.RegisterBuiltInRoleOverride(
                "Volunteer",
                """{"dashboard":"v","menus":"v"}""",
                """["tasks"]""");

            Assert.True(RoleAccess.CanAccessAction("Volunteer", "Operations", "Menus"));
            Assert.False(RoleAccess.CanAccessAction("Volunteer", "Management", "Gate"));
            Assert.Equal(new[] { "tasks" }, RoleAccess.GetDashboardWidgets("Volunteer"));
            Assert.Throws<InvalidOperationException>(() => RoleAccess.RegisterBuiltInRoleOverride(
                "Admin",
                """{"dashboard":"v"}""",
                """[]"""));
        }
        finally
        {
            RoleAccess.RegisterBuiltInRoleOverride("Volunteer", permissions, widgets);
        }
    }
}

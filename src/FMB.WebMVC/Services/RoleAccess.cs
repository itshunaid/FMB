using System.Collections.Concurrent;
using System.Text.Json;

namespace FMB.WebMVC.Services;

public static class RoleAccess
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> BuiltInPermissions =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["Admin"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dashboard"] = "v",
                ["gate"] = "vced",
                ["reconciliation"] = "vce",
                ["households"] = "vcedlx",
                ["distributors"] = "vcedlx",
                ["menus"] = "vcedlx",
                ["feedback"] = "vcedlx",
                ["expenses"] = "vcedlx",
                ["queries"] = "vcedlx",
                ["notifications"] = "v",
                ["audit"] = "v",
                ["users"] = "vcedlx",
                ["roles"] = "v",
                ["analytics"] = "vx",
                ["masters"] = "vcedlx",
                ["automations"] = "vcedlx"
            },
            ["Volunteer"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dashboard"] = "v",
                ["gate"] = "vce",
                ["reconciliation"] = "vce",
                ["households"] = "vcel",
                ["distributors"] = "vl",
                ["menus"] = "v",
                ["feedback"] = "v",
                ["expenses"] = "vce",
                ["queries"] = "vce",
                ["notifications"] = "v"
            },
            ["Back Office"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dashboard"] = "v",
                ["households"] = "v",
                ["distributors"] = "v",
                ["menus"] = "v",
                ["feedback"] = "v",
                ["expenses"] = "vcel",
                ["queries"] = "vce",
                ["notifications"] = "v"
            }
        };

    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> CustomPermissions =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>> CustomDashboardWidgets =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> BuiltInPermissionOverrides =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>> BuiltInWidgetOverrides =
        new(StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> BuiltInDashboardWidgets =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["Admin"] = ["kpi_ops", "kpi_fin", "g_perf", "d_status", "l_cost", "p_cat", "tasks"],
            ["Volunteer"] = ["kpi_ops", "g_perf", "d_status", "tasks", "fb_gauge"],
            ["Back Office"] = ["kpi_fin", "l_cost", "p_cat", "q_open", "fb_gauge"]
        };

    public static IReadOnlyList<string> BuiltInRoles { get; } = ["Admin", "Volunteer", "Back Office"];

    public static IReadOnlyDictionary<string, string> Modules { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dashboard"] = "Dashboard",
            ["gate"] = "Gate",
            ["reconciliation"] = "Reconciliation",
            ["households"] = "Households",
            ["distributors"] = "Distributors",
            ["menus"] = "Menus",
            ["feedback"] = "Feedback",
            ["expenses"] = "Expenses",
            ["queries"] = "Queries",
            ["notifications"] = "Notifications",
            ["audit"] = "Audit logs",
            ["users"] = "Users",
            ["roles"] = "Roles",
            ["analytics"] = "Analytics"
        };

    public static IReadOnlyDictionary<string, string> DashboardWidgetOptions { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kpi_ops"] = "Operations KPIs",
            ["kpi_fin"] = "Finance KPIs",
            ["g_perf"] = "Collection performance",
            ["d_status"] = "Distributor status",
            ["l_cost"] = "Cost per thali",
            ["p_cat"] = "Expense categories",
            ["q_open"] = "Open queries",
            ["fb_gauge"] = "Feedback rating",
            ["tasks"] = "Quick tasks"
        };

    public static bool IsKnownRole(string? role) =>
        role is not null && (BuiltInPermissions.ContainsKey(role) || CustomPermissions.ContainsKey(role));

    public static bool IsBuiltInRole(string? role) =>
        role is not null && BuiltInPermissions.ContainsKey(role);

    public static bool CanAssignRole(string actorRole, string assignedRole)
    {
        if (!IsKnownRole(assignedRole))
        {
            return false;
        }
        if (actorRole == "Admin")
        {
            return true;
        }
        if (assignedRole == "Admin")
        {
            return false;
        }

        return GetPermissions(assignedRole).All(module =>
            module.Value.All(permission => Can(actorRole, module.Key, permission)));
    }

    public static void RegisterCustomRole(
        string role,
        string permissionsJson,
        string dashboardWidgetsJson)
    {
        if (IsBuiltInRole(role))
        {
            throw new InvalidOperationException("A custom role cannot replace a built-in role.");
        }

        var (permissions, widgets) = ParseRoleDefinition(role, permissionsJson, dashboardWidgetsJson);
        CustomPermissions[role] = permissions;
        CustomDashboardWidgets[role] = widgets;
    }

    public static void RegisterRoleDefinition(
        string role,
        string permissionsJson,
        string dashboardWidgetsJson)
    {
        if (IsBuiltInRole(role))
        {
            RegisterBuiltInRoleOverride(role, permissionsJson, dashboardWidgetsJson);
            return;
        }

        RegisterCustomRole(role, permissionsJson, dashboardWidgetsJson);
    }

    public static void RegisterBuiltInRoleOverride(
        string role,
        string permissionsJson,
        string dashboardWidgetsJson)
    {
        if (!IsBuiltInRole(role) || role == "Admin")
        {
            throw new InvalidOperationException("Only non-admin built-in roles can be customized.");
        }

        var (permissions, widgets) = ParseRoleDefinition(role, permissionsJson, dashboardWidgetsJson);
        BuiltInPermissionOverrides[role] = permissions;
        BuiltInWidgetOverrides[role] = widgets;
    }

    public static void RemoveCustomRole(string role)
    {
        CustomPermissions.TryRemove(role, out _);
        CustomDashboardWidgets.TryRemove(role, out _);
    }

    public static bool Can(string? role, string module, char permission) =>
        role is not null
        && GetPermissions(role).TryGetValue(module, out var permissions)
        && permissions.Contains(permission);

    public static bool CanAccessAction(string? role, string controller, string action, string? resource = null)
    {
        if (role == "Admin")
        {
            return true;
        }

        if (controller == "Operations" && action is "CreateRole" or "EditRole" or "DeleteRole")
        {
            return false;
        }

        if (controller == "Home")
        {
            if (action is "Index" or "Dashboard")
            {
                return Can(role, "dashboard", 'v');
            }

            if (action == "GetTab")
            {
                var module = resource switch
                {
                    "dash" => "dashboard",
                    "gate" => "gate",
                    "recon" => "reconciliation",
                    "hh" => "households",
                    "aud" => "audit",
                    _ => string.Empty
                };
                return module.Length > 0 && Can(role, module, 'v');
            }

            return action == "SimulateScan" && Can(role, "gate", 'c');
        }

        var moduleName = controller switch
        {
            "Management" => action switch
            {
                "Households" or "CreateHousehold" or "EditHousehold" or "DeleteHousehold" => "households",
                "Distributors" or "CreateDistributor" or "EditDistributor" or "DeleteDistributor" => "distributors",
                "Gate" => "gate",
                "Reconciliation" or "MarkCollected" or "CloseWindow" or "FinalizeDay" => "reconciliation",
                "Analytics" => "analytics",
                _ => string.Empty
            },
            "Operations" => action switch
            {
                "Menus" or "CreateMenu" or "BulkScheduleMenus" or "EditMenu" or "DeleteMenu" or "ExportMenus" => "menus",
                "Expenses" or "CreateExpense" or "EditExpense" or "DeleteExpense" or "ExportExpenses" => "expenses",
                "Feedback" or "DeleteFeedback" or "ExportFeedback" => "feedback",
                "Queries" or "CreateQuery" or "ReplyQuery" or "DeleteQuery" or "ExportQueries" => "queries",
                "Notifications" => "notifications",
                "AuditLogs" or "ExportAuditLogs" => "audit",
                "Users" or "CreateUser" or "EditUser" or "DeleteUser" or "UserActivity" or "ExportUsers" => "users",
                "Roles" or "ViewRole" or "RoleActivity" or "CreateRole" or "EditRole" or "DeleteRole" => "roles",
                "ExportHouseholds" => "households",
                "ExportReconciliation" => "reconciliation",
                _ => string.Empty
            },
            _ => string.Empty
        };
        if (moduleName.Length == 0)
        {
            return false;
        }

        var permission = action switch
        {
            var value when value.StartsWith("Create", StringComparison.Ordinal) => 'c',
            "BulkScheduleMenus" => 'c',
            var value when value.StartsWith("Edit", StringComparison.Ordinal) => 'e',
            var value when value.StartsWith("Delete", StringComparison.Ordinal) => 'd',
            var value when value.StartsWith("Export", StringComparison.Ordinal) => 'x',
            "MarkCollected" or "CloseWindow" or "FinalizeDay" or "SimulateScan" => 'e',
            "UserActivity" => 'l',
            "RoleActivity" => 'l',
            _ => 'v'
        };
        return Can(role, moduleName, permission);
    }

    public static IReadOnlyDictionary<string, string> GetPermissions(string role) =>
        BuiltInPermissions.TryGetValue(role, out var permissions)
            ? BuiltInPermissionOverrides.TryGetValue(role, out var builtInOverride)
                ? builtInOverride
                : permissions
            : CustomPermissions.TryGetValue(role, out permissions)
            ? permissions
            : new Dictionary<string, string>();

    public static IReadOnlyList<string> GetDashboardWidgets(string role) =>
        BuiltInWidgetOverrides.TryGetValue(role, out var builtInOverride)
            ? builtInOverride
            : CustomDashboardWidgets.TryGetValue(role, out var widgets)
                ? widgets
                : BuiltInDashboardWidgets.TryGetValue(role, out var defaults)
                    ? defaults
                    : Array.Empty<string>();

    public static IReadOnlyList<string> GetVisibleDashboardWidgets(string role) =>
        GetDashboardWidgets(role)
            .Where(widget => widget switch
            {
                "kpi_ops" or "g_perf" or "d_status" => Can(role, "reconciliation", 'v'),
                "kpi_fin" or "l_cost" or "p_cat" => Can(role, "expenses", 'v'),
                "q_open" => Can(role, "queries", 'v'),
                "fb_gauge" => Can(role, "feedback", 'v'),
                "tasks" => true,
                _ => false
            })
            .ToArray();

    private static (IReadOnlyDictionary<string, string> Permissions, IReadOnlyList<string> Widgets)
        ParseRoleDefinition(string role, string permissionsJson, string dashboardWidgetsJson)
    {
        var permissions = JsonSerializer.Deserialize<Dictionary<string, string>>(permissionsJson)
            ?? throw new InvalidOperationException($"Role '{role}' has invalid permissions.");
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (module, values) in permissions)
        {
            if (!Modules.ContainsKey(module)
                || string.IsNullOrEmpty(values)
                || values.Any(value => !"vcedlx".Contains(value)))
            {
                throw new InvalidOperationException($"Role '{role}' has invalid permissions.");
            }
            normalized[module] = new string(values.Distinct().ToArray());
        }

        var widgets = JsonSerializer.Deserialize<List<string>>(dashboardWidgetsJson)
            ?? throw new InvalidOperationException($"Role '{role}' has invalid dashboard widgets.");
        if (widgets.Any(widget => !DashboardWidgetOptions.ContainsKey(widget)))
        {
            throw new InvalidOperationException($"Role '{role}' has invalid dashboard widgets.");
        }

        return (normalized, widgets.Distinct(StringComparer.Ordinal).ToArray());
    }
}

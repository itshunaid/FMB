using System.Globalization;
using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FMB.Application.Commands;
using FMB.Application.Interfaces;
using FMB.Application.Queries;
using FMB.Domain.Entities;
using FMB.WebMVC.Services;

namespace FMB.WebMVC.Controllers
{
    public class HomeController : Controller
    {
        private const string SessionUserKey = "FMB.Auth.User";

        private readonly IMediator _mediator;
        private readonly ITiffinTrackerDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public HomeController(
            IMediator mediator,
            ITiffinTrackerDbContext context,
            IWebHostEnvironment environment)
        {
            _mediator = mediator;
            _context = context;
            _environment = environment;
        }

        [HttpGet]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            if (!IsAuthenticated())
            {
                return RedirectToAction(nameof(Login));
            }

            if (IsHouseholdAuthenticated())
            {
                var householdPortal = await BuildHouseholdPortalAsync(cancellationToken);
                return householdPortal is null
                    ? RedirectToAction(nameof(Logout))
                    : View("HouseholdPortal", householdPortal);
            }

            await RegisterCustomRolePermissionsAsync(
                HttpContext.Session.GetString(SessionUserKey)?.Split('|').ElementAtOrDefault(3),
                cancellationToken);
            var dashboard = await BuildDashboardModelAsync(cancellationToken);
            return DashboardView(dashboard);
        }

        [HttpGet]
        public async Task<IActionResult> Login(CancellationToken cancellationToken)
        {
            if (IsAuthenticated())
            {
                return RedirectToAction(nameof(Dashboard));
            }

            var model = new LoginViewModel();
            await PopulateLoginDemosAsync(model, cancellationToken);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Authenticate(LoginViewModel model, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                await PopulateLoginDemosAsync(model, cancellationToken);
                return View("Login", model);
            }

            var loginId = model.ItsId!.Trim();
            if (string.Equals(model.LoginAs, "Household", StringComparison.Ordinal))
            {
                var household = await FindHouseholdByLoginIdAsync(loginId, cancellationToken);
                if (household is null
                    || string.Equals(household.Status, "Discontinued", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(household.PasswordHash)
                    || !PinCredential.Verify(model.Pin!, household.PasswordHash))
                {
                    model.ErrorMessage = "Invalid household number or password.";
                    await PopulateLoginDemosAsync(model, cancellationToken);
                    return View("Login", model);
                }

                HttpContext.Session.SetString(SessionUserKey, $"{household.Id}|Household|{household.Name}");
                _context.AuditLogs.Add(new AuditLog
                {
                    Timestamp = DateTime.UtcNow,
                    PerformedBy = household.Name,
                    Action = "Household login",
                    TargetItem = household.Id,
                    TableName = "Users"
                });
                await _context.SaveChangesAsync(cancellationToken);
                return RedirectToAction(nameof(Index));
            }

            if (!string.Equals(model.LoginAs, "Staff", StringComparison.Ordinal))
            {
                model.ErrorMessage = "Choose Household or Staff user.";
                await PopulateLoginDemosAsync(model, cancellationToken);
                return View("Login", model);
            }

            var user = await _context.SystemUsers.FirstOrDefaultAsync(
                account => account.ItsId == loginId,
                cancellationToken);
            var roleDefinition = user is not null
                ? await _context.RoleDefinitions.AsNoTracking()
                    .FirstOrDefaultAsync(role => role.Name == user.Role, cancellationToken)
                : null;
            if (user is null
                || !string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase)
                || (!RoleAccess.IsBuiltInRole(user.Role) && roleDefinition is null)
                || !PinCredential.Verify(model.Pin!, user.PinHash))
            {
                model.ErrorMessage = "Invalid staff ID or password.";
                await PopulateLoginDemosAsync(model, cancellationToken);
                return View("Login", model);
            }

            user.LastLogin = DateTime.UtcNow;
            _context.AuditLogs.Add(new AuditLog
            {
                Timestamp = DateTime.UtcNow,
                PerformedBy = user.Name,
                Action = "Login",
                TargetItem = user.ItsId,
                TableName = "Users"
            });
            await _context.SaveChangesAsync(cancellationToken);
            if (roleDefinition is not null)
            {
                RoleAccess.RegisterRoleDefinition(
                    roleDefinition.Name,
                    roleDefinition.PermissionsJson,
                    roleDefinition.DashboardWidgetsJson);
            }
            HttpContext.Session.SetString(SessionUserKey, $"{user.ItsId}|StaffUser|{user.Name}|{user.Role}");
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
        {
            if (!IsAuthenticated())
            {
                return RedirectToAction(nameof(Login));
            }

            if (IsHouseholdAuthenticated())
            {
                var householdPortal = await BuildHouseholdPortalAsync(cancellationToken);
                return householdPortal is null
                    ? RedirectToAction(nameof(Logout))
                    : View("HouseholdPortal", householdPortal);
            }

            await RegisterCustomRolePermissionsAsync(
                HttpContext.Session.GetString(SessionUserKey)?.Split('|').ElementAtOrDefault(3),
                cancellationToken);
            var dashboard = await BuildDashboardModelAsync(cancellationToken);
            return DashboardView(dashboard);
        }

        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            HttpContext.Session.Remove(SessionUserKey);
            return RedirectToAction(nameof(Login));
        }

        public async Task<IActionResult> GetTab(string name, CancellationToken cancellationToken)
        {
            var sessionUser = HttpContext.Session.GetString(SessionUserKey)?.Split('|');
            var role = sessionUser?.ElementAtOrDefault(3);
            await RegisterCustomRolePermissionsAsync(role, cancellationToken);
            if (sessionUser is not { Length: > 1 }
                || sessionUser[1] != "StaffUser"
                || !RoleAccess.CanAccessAction(role, "Home", "GetTab", name))
            {
                return sessionUser is { Length: > 1 } ? Forbid() : Unauthorized();
            }

            switch (name)
            {
                case "dash":
                    var stats = await _mediator.Send(new GetDashboardStatsQuery(DateTime.Today), cancellationToken);
                    return PartialView("_DashboardPartial", stats);
                case "gate":
                    var households = await _context.Households.ToListAsync(cancellationToken);
                    return PartialView("_GatePartial", households);
                case "hh":
                    var hhList = await _context.Households.Include(h => h.Distributor).ToListAsync(cancellationToken);
                    return PartialView("_HouseholdsPartial", hhList);
                case "aud":
                    var logs = await _context.AuditLogs.OrderByDescending(a => a.Timestamp).Take(50).ToListAsync(cancellationToken);
                    return PartialView("_AuditPartial", logs);
                default:
                    var defaultStats = await _mediator.Send(new GetDashboardStatsQuery(DateTime.Today), cancellationToken);
                    return PartialView("_DashboardPartial", defaultStats);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SimulateScan(
            [FromBody] ScanRequestDto dto,
            CancellationToken cancellationToken)
        {
            var sessionUser = HttpContext.Session.GetString(SessionUserKey)?.Split('|');
            var role = sessionUser?.ElementAtOrDefault(3);
            await RegisterCustomRolePermissionsAsync(role, cancellationToken);
            if (sessionUser is not { Length: > 1 }
                || sessionUser[1] != "StaffUser"
                || !RoleAccess.CanAccessAction(role, "Home", "SimulateScan"))
            {
                return sessionUser is { Length: > 1 } ? Forbid() : Unauthorized();
            }

            var result = await _mediator.Send(
                new ScanTiffinCommand(dto.TagId, "Manual", DateTimeOffset.UtcNow, "SIMULATOR"),
                cancellationToken);
            return Json(result);
        }

        private IActionResult DashboardView(DashboardViewModel model) =>
            string.Equals(model.Role, "Admin", StringComparison.Ordinal)
                ? View("Index", model)
                : View("StaffOperationsDashboard", model);

        private async Task RegisterCustomRolePermissionsAsync(
            string? role,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                return;
            }

            var definition = await _context.RoleDefinitions.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Name == role, cancellationToken);
            if (definition is null)
            {
                if (!RoleAccess.IsBuiltInRole(role))
                {
                    RoleAccess.RemoveCustomRole(role);
                }
                return;
            }

            RoleAccess.RegisterRoleDefinition(
                definition.Name,
                definition.PermissionsJson,
                definition.DashboardWidgetsJson);
        }

        private async Task<DashboardViewModel> BuildDashboardModelAsync(CancellationToken cancellationToken)
        {
            var userValue = HttpContext.Session.GetString(SessionUserKey);
            var userParts = userValue?.Split('|');

            var stats = await _mediator.Send(new GetDashboardStatsQuery(DateTime.Today), cancellationToken);
            var logCount = await _context.TiffinLogs.CountAsync(cancellationToken);
            var today = DateTime.Today;
            var averageRating = await _context.FeedbackRecords.AsNoTracking()
                .Where(record => record.Date >= today.AddDays(-9) && record.Date <= today)
                .Select(record => (double?)record.Rating)
                .AverageAsync(cancellationToken) ?? 0;
            var openQueries = await _context.QueryRecords.AsNoTracking()
                .CountAsync(record => record.Status == "Open", cancellationToken);
            var menuDates = await _context.DailyMenus.AsNoTracking()
                .Where(menu => menu.Date >= today.AddDays(-4) && menu.Date <= today)
                .Select(menu => menu.Date)
                .ToListAsync(cancellationToken);
            var expenseDates = await _context.ExpenseRecords.AsNoTracking()
                .Where(expense => expense.Date >= today.AddDays(-4) && expense.Date <= today)
                .Select(expense => expense.Date)
                .ToListAsync(cancellationToken);
            var monthStart = new DateTime(today.Year, today.Month, 1);
            var expenses = await _context.ExpenseRecords.AsNoTracking()
                .Where(expense => expense.Date >= monthStart && expense.Date <= today)
                .ToListAsync(cancellationToken);
            var households = await _context.Households.AsNoTracking()
                .Include(household => household.Distributor)
                .Where(household => household.Status != null
                    && (household.Status.Trim().ToLower() == "active"
                        || household.Status.Trim().ToLower() == "on hold"))
                .ToListAsync(cancellationToken);
            var activeHouseholdIds = households
                .Where(household => string.Equals(household.Status?.Trim(), "Active", StringComparison.OrdinalIgnoreCase))
                .Select(household => household.Id)
                .ToHashSet(StringComparer.Ordinal);
            var trendStart = today.AddDays(-13);
            var trendLogs = await _context.TiffinLogs.AsNoTracking()
                .Where(log => log.Date >= trendStart && log.Date < today.AddDays(1)
                    && log.Status != null && log.Status.Trim().ToLower() == "collected")
                .ToListAsync(cancellationToken);
            var collectedByDate = trendLogs
                .Where(log => activeHouseholdIds.Contains(log.HouseholdId))
                .GroupBy(log => log.Date.Date)
                .ToDictionary(group => group.Key, group => group.Select(log => log.HouseholdId).ToHashSet(StringComparer.Ordinal));
            var collectionTrend = Enumerable.Range(0, 14)
                .Select(offset =>
                {
                    var date = trendStart.AddDays(offset);
                    var collected = collectedByDate.TryGetValue(date, out var ids) ? ids.Count : 0;
                    return new DailyTrendRow(
                        date,
                        activeHouseholdIds.Count,
                        collected,
                        activeHouseholdIds.Count == 0 ? 0 : (int)Math.Round(collected * 100d / activeHouseholdIds.Count));
                })
                .ToList();
            var todayCollectedIds = collectedByDate.TryGetValue(today, out var todayIds)
                ? todayIds
                : new HashSet<string>(StringComparer.Ordinal);
            var distributorRows = households
                .Where(household => activeHouseholdIds.Contains(household.Id)
                    && !string.IsNullOrWhiteSpace(household.DistributorId))
                .GroupBy(household => new { household.DistributorId, Name = household.Distributor?.Name ?? household.DistributorId! })
                .Select(group => new DistributorTrendRow(
                    group.Key.DistributorId,
                    group.Key.Name,
                    group.Count(),
                    group.Count(household => todayCollectedIds.Contains(household.Id))))
                .OrderBy(row => row.Name)
                .ToList();
            var expenseCategories = new[]
            {
                new ExpenseCategoryRow("Groceries", expenses.Sum(expense => expense.Groceries)),
                new ExpenseCategoryRow("Meat", expenses.Sum(expense => expense.Meat)),
                new ExpenseCategoryRow("Gas", expenses.Sum(expense => expense.Gas)),
                new ExpenseCategoryRow("Labour", expenses.Sum(expense => expense.Labour)),
                new ExpenseCategoryRow("Packaging", expenses.Sum(expense => expense.Packaging)),
                new ExpenseCategoryRow("Other", expenses.Sum(expense => expense.Other))
            };
            var totalExpense = expenseCategories.Sum(category => category.Total);
            var totalThalis = expenses.Sum(expense => expense.ThaliCount);
            var collectedShare = stats.TotalActive + stats.OnHold == 0
                ? 0
                : (int)Math.Round(stats.Collected * 100d / (stats.TotalActive + stats.OnHold));
            var onHoldShare = stats.TotalActive + stats.OnHold == 0
                ? 0
                : (int)Math.Round(stats.OnHold * 100d / (stats.TotalActive + stats.OnHold));
            var pendingShare = Math.Max(0, 100 - collectedShare - onHoldShare);
            var recentAudits = await _context.AuditLogs.AsNoTracking()
                .OrderByDescending(log => log.Timestamp)
                .Take(8)
                .ToListAsync(cancellationToken);

            return new DashboardViewModel
            {
                UserName = userParts is { Length: > 2 } ? userParts[2] : "Volunteer",
                Role = userParts?.ElementAtOrDefault(3) ?? "Volunteer",
                CollectionRate = stats.CollectionRatePercent,
                Collected = stats.Collected,
                Pending = stats.Pending,
                OnHold = stats.OnHold,
                ExpenseThisMonth = totalExpense,
                RecentActivity = recentAudits.Select(log => new ActivityItem(
                    log.Timestamp.ToLocalTime().ToString("dd MMM HH:mm"),
                    log.PerformedBy,
                    log.Action,
                    log.TargetItem,
                    log.Reason ?? string.Empty)).ToList(),
                TotalHouseholds = stats.TotalActive,
                LogCount = logCount,
                SelectedDate = today.ToString("yyyy-MM-dd"),
                TodayMenu = stats.TodayMenu,
                CollectionTrend = collectionTrend,
                DistributorRows = distributorRows,
                ExpenseCategories = expenseCategories,
                CostPerThali = totalThalis == 0
                    ? 0
                    : totalExpense / totalThalis,
                AverageRating = averageRating,
                OpenQueries = openQueries,
                ExpenseDaysMissing = menuDates.Count(date => !expenseDates.Contains(date)),
                CollectedShare = collectedShare,
                PendingShare = pendingShare,
                OnHoldShare = onHoldShare
            };
        }

        private async Task<StaffDashboardViewModel?> BuildStaffDashboardAsync(
            CancellationToken cancellationToken)
        {
            var userParts = HttpContext.Session.GetString(SessionUserKey)?.Split('|');
            if (userParts is not { Length: > 1 })
            {
                return null;
            }

            var user = await _context.SystemUsers.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ItsId == userParts[0], cancellationToken);
            if (user is null || !string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var recentActivity = await _context.AuditLogs.AsNoTracking()
                .Where(item => item.PerformedBy == user.Name)
                .OrderByDescending(item => item.Timestamp)
                .Take(5)
                .ToListAsync(cancellationToken);

            return new StaffDashboardViewModel
            {
                ItsId = user.ItsId,
                Name = user.Name,
                Role = user.Role,
                Phone = user.Phone,
                Status = user.Status,
                LastLogin = user.LastLogin,
                RecentActivity = recentActivity.Select(item => new StaffActivityRow(
                    item.Timestamp.ToLocalTime(),
                    item.Action,
                    item.TargetItem)).ToList()
            };
        }

        private async Task<Household?> FindHouseholdByLoginIdAsync(
            string loginId,
            CancellationToken cancellationToken)
        {
            var household = await _context.Households.FirstOrDefaultAsync(
                item => item.Id == loginId,
                cancellationToken);
            if (household is not null
                || !int.TryParse(loginId, NumberStyles.None, CultureInfo.InvariantCulture, out var thaliNumber)
                || thaliNumber < 1)
            {
                return household;
            }

            return await _context.Households.FirstOrDefaultAsync(
                item => item.ThaliNumber == thaliNumber,
                cancellationToken);
        }

        private async Task PopulateLoginDemosAsync(
            LoginViewModel model,
            CancellationToken cancellationToken)
        {
            model.DemoLoginEnabled = _environment.IsDevelopment();
            if (!model.DemoLoginEnabled)
            {
                return;
            }

            var demoHousehold = await _context.Households
                .FirstOrDefaultAsync(household => household.ThaliNumber == 2, cancellationToken);
            model.DemoHouseholdItsId = demoHousehold?.Id;
            model.DemoHouseholdThaliNumber = demoHousehold?.ThaliNumber ?? 2;

            var demoStaffIds = new[] { "30455616", "40000002", "40000003" };
            model.DemoStaffUsers = await _context.SystemUsers
                .Where(user => demoStaffIds.Contains(user.ItsId))
                .OrderBy(user => user.ItsId)
                .Select(user => new LoginDemoUser
                {
                    ItsId = user.ItsId,
                    Name = user.Name,
                    Role = user.Role
                })
                .ToListAsync(cancellationToken);
        }

        private bool IsAuthenticated()
        {
            return !string.IsNullOrWhiteSpace(HttpContext.Session.GetString(SessionUserKey));
        }

        private bool IsHouseholdAuthenticated() =>
            string.Equals(
                HttpContext.Session.GetString(SessionUserKey)?.Split('|').ElementAtOrDefault(1),
                "Household",
                StringComparison.Ordinal);

        private async Task<HouseholdPortalViewModel?> BuildHouseholdPortalAsync(
            CancellationToken cancellationToken)
        {
            var userParts = HttpContext.Session.GetString(SessionUserKey)?.Split('|');
            if (userParts is not { Length: > 1 })
            {
                return null;
            }

            var household = await _context.Households.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == userParts[0], cancellationToken);
            if (household is null || string.Equals(household.Status, "Discontinued", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var today = DateTime.Today;
            var menu = await _context.DailyMenus.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Date == today, cancellationToken);
            var todayKey = today.ToString("yyyy-MM-dd");
            var distributionWindowClosed = await _context.AuditLogs.AsNoTracking().AnyAsync(
                item => item.TableName == "DailyOperations"
                    && item.Action == "Distribution window closed"
                    && item.TargetItem == todayKey,
                cancellationToken);
            var collection = await _context.TiffinLogs.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Date == today && item.HouseholdId == household.Id, cancellationToken);
            var feedback = await _context.FeedbackRecords.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Date == today && item.ItsId == household.Id, cancellationToken);
            var logs = await _context.TiffinLogs.AsNoTracking()
                .Where(item => item.HouseholdId == household.Id && item.Date >= today.AddDays(-6) && item.Date <= today)
                .OrderByDescending(item => item.Date)
                .ToListAsync(cancellationToken);
            var menus = await _context.DailyMenus.AsNoTracking()
                .Where(item => item.Date >= today.AddDays(-6) && item.Date <= today)
                .ToDictionaryAsync(item => item.Date, item => item.MenuName, cancellationToken);
            var upcomingMenus = await _context.DailyMenus.AsNoTracking()
                .Where(item => item.Date > today && item.Date <= today.AddDays(14))
                .OrderBy(item => item.Date)
                .Select(item => new HouseholdPortalMenu(item.Date, item.MenuName))
                .ToListAsync(cancellationToken);

            return new HouseholdPortalViewModel
            {
                ItsId = household.Id,
                Name = household.Name,
                ThaliNumber = household.ThaliNumber,
                Zone = household.Zone,
                Status = household.Status ?? "Active",
                Today = today,
                TodayMenu = menu?.MenuName,
                CollectionStatus = string.Equals(collection?.Status, "Collected", StringComparison.OrdinalIgnoreCase)
                    ? "Collected"
                    : string.Equals(collection?.Status, "Not Collected", StringComparison.OrdinalIgnoreCase)
                        ? "Not Collected"
                    : string.Equals(household.Status, "On Hold", StringComparison.OrdinalIgnoreCase)
                        ? "On Hold"
                        : menu is null
                            ? "No service"
                            : distributionWindowClosed ? "Not Collected" : "Pending",
                CollectionTime = collection?.ScanTime?.ToLocalTime(),
                CanSubmitFeedback = string.Equals(collection?.Status, "Collected", StringComparison.OrdinalIgnoreCase)
                    && menu is not null
                    && feedback is null,
                SubmittedFeedback = feedback is null
                    ? null
                    : new HouseholdFeedbackSummary(
                        feedback.Rating,
                        feedback.Quantity,
                        feedback.Quality,
                        feedback.Comment),
                FeedbackUrl = Url.Action("Submit", "Feedback", new { date = today.ToString("yyyy-MM-dd") }) ?? string.Empty,
                UpcomingMenus = upcomingMenus,
                RecentDays = Enumerable.Range(0, 7).Select(offset =>
                {
                    var date = today.AddDays(-offset);
                    var log = logs.FirstOrDefault(item => item.Date == date);
                    var hasMenu = menus.TryGetValue(date, out var menuName);
                    var dayStatus = string.Equals(log?.Status, "Collected", StringComparison.OrdinalIgnoreCase)
                        ? "Collected"
                        : string.Equals(log?.Status, "On Hold", StringComparison.OrdinalIgnoreCase)
                            || date == today && string.Equals(household.Status, "On Hold", StringComparison.OrdinalIgnoreCase)
                                ? "On Hold"
                                : date == today && !hasMenu
                                    ? "No service"
                                    : date == today && !distributionWindowClosed
                                        ? "Pending"
                                    : hasMenu ? "Not Collected" : "No service";
                    return new HouseholdPortalDay(
                        date,
                        hasMenu ? menuName! : "—",
                        dayStatus,
                        log?.ScanTime?.ToLocalTime());
                }).ToList()
            };
        }
    }

    public class LoginViewModel
    {
        [Required, StringLength(20)]
        public string? ItsId { get; set; }

        [Required, StringLength(128, MinimumLength = 4)]
        public string? Pin { get; set; }
        [Required]
        public string LoginAs { get; set; } = "Household";
        public string? ErrorMessage { get; set; }
        public bool DemoLoginEnabled { get; set; }
        public int DemoHouseholdThaliNumber { get; set; } = 2;
        public string? DemoHouseholdItsId { get; set; }
        public List<LoginDemoUser> DemoStaffUsers { get; set; } = new();
    }

    public class LoginDemoUser
    {
        public string ItsId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    public class DashboardViewModel
    {
        public string UserName { get; set; } = "Admin";
        public string Role { get; set; } = "Admin";
        public int CollectionRate { get; set; }
        public int Collected { get; set; }
        public int Pending { get; set; }
        public int OnHold { get; set; }
        public int TotalHouseholds { get; set; }
        public int LogCount { get; set; }
        public decimal ExpenseThisMonth { get; set; }
        public string SelectedDate { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
        public IList<ActivityItem> RecentActivity { get; set; } = new List<ActivityItem>();
        public string TodayMenu { get; set; } = string.Empty;
        public IReadOnlyList<DailyTrendRow> CollectionTrend { get; set; } = Array.Empty<DailyTrendRow>();
        public IReadOnlyList<DistributorTrendRow> DistributorRows { get; set; } = Array.Empty<DistributorTrendRow>();
        public IReadOnlyList<ExpenseCategoryRow> ExpenseCategories { get; set; } = Array.Empty<ExpenseCategoryRow>();
        public decimal CostPerThali { get; set; }
        public double AverageRating { get; set; }
        public int OpenQueries { get; set; }
        public int ExpenseDaysMissing { get; set; }
        public int CollectedShare { get; set; }
        public int PendingShare { get; set; }
        public int OnHoldShare { get; set; }
    }

    public record ActivityItem(string When, string By, string Action, string Item, string Reason, string? Target = null);
    public sealed record ExpenseCategoryRow(string Category, decimal Total);

    public sealed class StaffDashboardViewModel
    {
        public string ItsId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
        public string Phone { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public DateTime? LastLogin { get; init; }
        public IReadOnlyList<StaffActivityRow> RecentActivity { get; init; } = Array.Empty<StaffActivityRow>();
    }

    public sealed record StaffActivityRow(DateTime Timestamp, string Action, string Target);
    public sealed class HouseholdPortalViewModel
    {
        public string ItsId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int ThaliNumber { get; init; }
        public string Zone { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public DateTime Today { get; init; }
        public string? TodayMenu { get; init; }
        public string CollectionStatus { get; init; } = "Pending";
        public DateTime? CollectionTime { get; init; }
        public bool CanSubmitFeedback { get; init; }
        public HouseholdFeedbackSummary? SubmittedFeedback { get; init; }
        public string FeedbackUrl { get; init; } = string.Empty;
        public IReadOnlyList<HouseholdPortalMenu> UpcomingMenus { get; init; } = Array.Empty<HouseholdPortalMenu>();
        public IReadOnlyList<HouseholdPortalDay> RecentDays { get; init; } = Array.Empty<HouseholdPortalDay>();
    }

    public sealed record HouseholdPortalDay(DateTime Date, string Menu, string Status, DateTime? CollectionTime);
    public sealed record HouseholdPortalMenu(DateTime Date, string Menu);
    public sealed record HouseholdFeedbackSummary(int Rating, string Quantity, string Quality, string? Comment);
    public record ScanRequestDto(string TagId);
}

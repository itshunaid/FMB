using System.Globalization;
using System.Text;
using System.Text.Json;
using FMB.Application.Interfaces;
using FMB.Domain.Entities;
using FMB.WebMVC.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FMB.WebMVC.Controllers;

[AutoValidateAntiforgeryToken]
[Route("Management")]
public sealed class OperationsController(ITiffinTrackerDbContext context) : Controller
{
    private const string SessionUserKey = "FMB.Auth.User";
    private static readonly string[] UserStatuses = ["Active", "Inactive"];
    private static readonly string[] QueryStatuses = ["Open", "In Progress", "Resolved"];

    public override async Task OnActionExecutionAsync(
        Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext filterContext,
        Microsoft.AspNetCore.Mvc.Filters.ActionExecutionDelegate next)
    {
        var user = HttpContext.Session.GetString(SessionUserKey)?.Split('|');
        if (user is not { Length: > 1 })
        {
            filterContext.Result = new RedirectToActionResult("Login", "Home", null);
            return;
        }

        if (user[1] != "StaffUser")
        {
            filterContext.Result = new RedirectToActionResult("Index", "Home", null);
            return;
        }

        var role = user.ElementAtOrDefault(3);
        if (!string.IsNullOrWhiteSpace(role))
        {
            var definition = await context.RoleDefinitions.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Name == role, HttpContext.RequestAborted);
            if (definition is not null)
            {
                RoleAccess.RegisterRoleDefinition(
                    definition.Name,
                    definition.PermissionsJson,
                    definition.DashboardWidgetsJson);
            }
        }

        var action = filterContext.ActionDescriptor.RouteValues["action"] ?? string.Empty;
        if (!RoleAccess.CanAccessAction(role, "Operations", action))
        {
            filterContext.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
            return;
        }

        await next();
    }

    [HttpGet("Menus")]
    public async Task<IActionResult> Menus(
        string? search,
        string? menu,
        DateTime? date,
        CancellationToken cancellationToken)
    {
        var query = context.DailyMenus.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(item => item.MenuName.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(menu))
        {
            query = query.Where(item => item.MenuName == menu);
        }
        if (date.HasValue)
        {
            var selected = date.Value.Date;
            query = query.Where(item => item.Date == selected);
        }

        var rows = await query.OrderByDescending(item => item.Date)
            .Select(item => new MenuRow(item.Date, item.MenuName))
            .ToListAsync(cancellationToken);
        return View(new MenusViewModel
        {
            Search = search,
            MenuFilter = menu,
            SelectedDate = date?.Date,
            MenuNames = await GetMenuNamesAsync(cancellationToken),
            Rows = rows,
            Message = TempData["Message"] as string
        });
    }

    [HttpGet("Households/Export")]
    public async Task<IActionResult> ExportHouseholds(
        string? search,
        string? zone,
        string? status,
        CancellationToken cancellationToken)
    {
        var query = context.Households.AsNoTracking().Include(item => item.Distributor).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(item => item.Id.ToLower().Contains(term)
                || item.Name.ToLower().Contains(term)
                || item.TagId.ToLower().Contains(term)
                || item.Phone.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(zone)) query = query.Where(item => item.Zone == zone);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(item => item.Status == status);
        var records = await query.OrderBy(item => item.Id).ToListAsync(cancellationToken);
        var rows = records.Select(item => new[]
        {
            item.ThaliNumber.ToString(CultureInfo.InvariantCulture), item.Id, item.Name, item.Address, item.Zone,
            item.Phone, item.Size.ToString(CultureInfo.InvariantCulture),
            item.TagId, item.Distributor?.Name ?? string.Empty, item.Status ?? string.Empty
        }).ToList();
        rows.Insert(0, ["Thali number", "ITS ID", "Name", "Address", "Zone", "Phone", "Household size", "RFID tag", "Distributor", "Status"]);
        return Csv(rows, "households.csv");
    }

    [HttpGet("Reconciliation/Export")]
    public async Task<IActionResult> ExportReconciliation(
        DateTime? date,
        string? search,
        string? zone,
        string? status,
        CancellationToken cancellationToken)
    {
        var selectedDate = (date ?? DateTime.Today).Date;
        var households = await context.Households.AsNoTracking()
            .OrderBy(item => item.Zone).ThenBy(item => item.Name).ToListAsync(cancellationToken);
        var logs = await context.TiffinLogs.AsNoTracking()
            .Where(item => item.Date == selectedDate).ToListAsync(cancellationToken);
        var logLookup = logs.GroupBy(item => item.HouseholdId)
            .ToDictionary(group => group.Key, group => group.First());
        var dateKey = selectedDate.ToString("yyyy-MM-dd");
        var windowClosed = await context.AuditLogs.AsNoTracking().AnyAsync(
            item => item.TableName == "DailyOperations"
                && item.Action == "Distribution window closed"
                && item.TargetItem == dateKey,
            cancellationToken);
        var term = search?.Trim();
        var rows = households.Select(item =>
            {
                logLookup.TryGetValue(item.Id, out var log);
                var rowStatus = string.Equals(item.Status?.Trim(), "On Hold", StringComparison.OrdinalIgnoreCase)
                    ? "On Hold"
                    : string.Equals(log?.Status, "Collected", StringComparison.OrdinalIgnoreCase)
                        ? "Collected"
                        : windowClosed ? "Not Collected" : "Pending";
                return new[]
                {
                    item.Id, item.Name, item.Zone, item.TagId, rowStatus,
                    log?.ScanMode is null ? string.Empty
                        : string.Equals(log.ScanMode, "manual", StringComparison.OrdinalIgnoreCase) ? "Manual" : "Auto",
                    log?.ScanTime?.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty
                };
            })
            .Where(row => string.IsNullOrWhiteSpace(term)
                || row[0].Contains(term, StringComparison.OrdinalIgnoreCase)
                || row[1].Contains(term, StringComparison.OrdinalIgnoreCase)
                || row[3].Contains(term, StringComparison.OrdinalIgnoreCase))
            .Where(row => string.IsNullOrWhiteSpace(zone) || row[2] == zone)
            .Where(row => string.IsNullOrWhiteSpace(status) || row[4] == status)
            .ToList();
        rows.Insert(0, ["ITS ID", "Household", "Zone", "RFID tag", "Status", "Entry type", "Scan time"]);
        return Csv(rows, $"reconciliation-{selectedDate:yyyy-MM-dd}.csv");
    }

    [HttpGet("Menus/Create")]
    public IActionResult CreateMenu(DateTime? date) =>
        View("MenuForm", new MenuFormViewModel { Date = (date ?? DateTime.Today).Date });

    [HttpGet("Menus/BulkSchedule")]
    public IActionResult BulkScheduleMenus() => View("BulkMenuSchedule", new BulkMenuScheduleViewModel());

    [HttpPost("Menus/BulkSchedule")]
    public async Task<IActionResult> BulkScheduleMenus(
        BulkMenuScheduleViewModel model,
        CancellationToken cancellationToken)
    {
        model.StartDate = model.StartDate.Date;
        model.EndDate = model.EndDate.Date;
        model.MenuName = model.MenuName?.Trim() ?? string.Empty;

        if (model.EndDate < model.StartDate)
        {
            ModelState.AddModelError(nameof(model.EndDate), "End date must be on or after the start date.");
        }
        if ((model.EndDate - model.StartDate).TotalDays > 365)
        {
            ModelState.AddModelError(nameof(model.EndDate), "A bulk schedule cannot exceed 366 days.");
        }
        if (model.EndDate > DateTime.Today.AddDays(365))
        {
            ModelState.AddModelError(nameof(model.EndDate), "Menu dates cannot be more than one year in the future.");
        }
        if (!ModelState.IsValid)
        {
            return View("BulkMenuSchedule", model);
        }

        var dates = Enumerable.Range(0, (model.EndDate - model.StartDate).Days + 1)
            .Select(offset => model.StartDate.AddDays(offset))
            .ToArray();
        var dateKeys = dates.Select(date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToArray();
        var existingMenus = await context.DailyMenus.AsNoTracking()
            .Where(item => item.Date >= model.StartDate && item.Date <= model.EndDate)
            .Select(item => item.Date)
            .ToListAsync(cancellationToken);
        var finalizedDates = await context.AuditLogs.AsNoTracking()
            .Where(item => item.TableName == "DailyOperations"
                && item.Action == "Distribution day finalized"
                && dateKeys.Contains(item.TargetItem))
            .Select(item => item.TargetItem)
            .ToListAsync(cancellationToken);
        var existing = existingMenus.ToHashSet();
        var finalized = finalizedDates.ToHashSet(StringComparer.Ordinal);
        var scheduled = dates
            .Where(date => !existing.Contains(date)
                && !finalized.Contains(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .ToArray();

        foreach (var date in scheduled)
        {
            context.DailyMenus.Add(new DailyMenu { Date = date, MenuName = model.MenuName });
        }
        if (scheduled.Length > 0)
        {
            context.AuditLogs.Add(CreateAuditLog(
                "Menus bulk scheduled",
                $"{model.StartDate:yyyy-MM-dd} to {model.EndDate:yyyy-MM-dd}",
                $"{model.MenuName}; {scheduled.Length} date(s) scheduled."));
        }

        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = $"{scheduled.Length} menu date(s) scheduled; {dates.Length - scheduled.Length} existing or finalized date(s) skipped.";
        return RedirectToAction(nameof(Menus));
    }

    [HttpPost("Menus/Create")]
    public async Task<IActionResult> CreateMenu(MenuFormViewModel model, CancellationToken cancellationToken)
    {
        model.Date = model.Date.Date;
        model.MenuName = model.MenuName?.Trim() ?? string.Empty;
        if (model.Date > DateTime.Today.AddDays(365))
        {
            ModelState.AddModelError(nameof(model.Date), "Menu date cannot be more than one year in the future.");
        }
        if (await context.DailyMenus.AnyAsync(item => item.Date == model.Date, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.Date), "A menu already exists for this date.");
        }
        if (!ModelState.IsValid)
        {
            return View("MenuForm", model);
        }

        context.DailyMenus.Add(new DailyMenu { Date = model.Date, MenuName = model.MenuName });
        context.AuditLogs.Add(CreateAuditLog("Menu created", model.Date.ToString("yyyy-MM-dd"), null));
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Menu added.";
        return RedirectToAction(nameof(Menus));
    }

    [HttpGet("Menus/{date}/Edit")]
    public async Task<IActionResult> EditMenu(DateTime date, CancellationToken cancellationToken)
    {
        var menu = await context.DailyMenus.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Date == date.Date, cancellationToken);
        return menu is null
            ? NotFound()
            : View("MenuForm", new MenuFormViewModel { Date = menu.Date, MenuName = menu.MenuName });
    }

    [HttpPost("Menus/{date}/Edit")]
    public async Task<IActionResult> EditMenu(
        DateTime date,
        MenuFormViewModel model,
        CancellationToken cancellationToken)
    {
        var menu = await context.DailyMenus.FirstOrDefaultAsync(item => item.Date == date.Date, cancellationToken);
        if (menu is null)
        {
            return NotFound();
        }
        if (await IsDayFinalizedAsync(date.Date, cancellationToken))
        {
            TempData["Message"] = "A finalized day's menu cannot be changed.";
            return RedirectToAction(nameof(Menus));
        }

        model.MenuName = model.MenuName?.Trim() ?? string.Empty;
        if (model.Date.Date != date.Date)
        {
            ModelState.AddModelError(nameof(model.Date), "A menu's date cannot be changed.");
        }
        if (!ModelState.IsValid)
        {
            model.Date = date.Date;
            return View("MenuForm", model);
        }

        menu.MenuName = model.MenuName;
        context.AuditLogs.Add(CreateAuditLog("Menu updated", date.ToString("yyyy-MM-dd"), null));
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Menu updated.";
        return RedirectToAction(nameof(Menus));
    }

    [HttpPost("Menus/{date}/Delete")]
    public async Task<IActionResult> DeleteMenu(DateTime date, CancellationToken cancellationToken)
    {
        var menu = await context.DailyMenus.FirstOrDefaultAsync(item => item.Date == date.Date, cancellationToken);
        if (menu is null)
        {
            return NotFound();
        }
        if (await IsDayFinalizedAsync(date.Date, cancellationToken))
        {
            TempData["Message"] = "A finalized day's menu cannot be deleted.";
            return RedirectToAction(nameof(Menus));
        }

        if (await context.ExpenseRecords.AnyAsync(item => item.Date == date.Date, cancellationToken)
            || await context.FeedbackRecords.AnyAsync(item => item.Date == date.Date, cancellationToken)
            || await context.TiffinLogs.AnyAsync(item => item.Date == date.Date, cancellationToken))
        {
            TempData["Message"] = "This menu has expenses, feedback or collection records. Remove those records before deleting the menu.";
            return RedirectToAction(nameof(Menus));
        }

        context.AuditLogs.Add(CreateAuditLog("Menu deleted", date.ToString("yyyy-MM-dd"), "Deleted from the menu register."));
        context.DailyMenus.Remove(menu);
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Menu deleted.";
        return RedirectToAction(nameof(Menus));
    }

    [HttpGet("Menus/Export")]
    public async Task<IActionResult> ExportMenus(string? search, string? menu, DateTime? date, CancellationToken cancellationToken)
    {
        var query = context.DailyMenus.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(item => item.MenuName.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(menu))
        {
            query = query.Where(item => item.MenuName == menu);
        }
        if (date.HasValue)
        {
            var selected = date.Value.Date;
            query = query.Where(item => item.Date == selected);
        }
        var records = await query.OrderByDescending(item => item.Date).ToListAsync(cancellationToken);
        var rows = records.Select(item => new[] { item.Date.ToString("yyyy-MM-dd"), item.MenuName }).ToList();
        rows.Insert(0, ["Date", "Menu"]);
        return Csv(rows, "menus.csv");
    }

    [HttpGet("Expenses")]
    public async Task<IActionResult> Expenses(
        string? search,
        string? menu,
        DateTime? date,
        CancellationToken cancellationToken)
    {
        var records = await context.ExpenseRecords.AsNoTracking().OrderByDescending(item => item.Date)
            .ToListAsync(cancellationToken);
        var menus = await context.DailyMenus.AsNoTracking().ToDictionaryAsync(item => item.Date, item => item.MenuName, cancellationToken);
        var term = search?.Trim();
        var rows = records.Select(item => new ExpenseRow(
                item.Date,
                menus.TryGetValue(item.Date, out var menuName) ? menuName : "—",
                item.ThaliCount,
                item.TotalExpense,
                item.ThaliCount == 0 ? 0 : item.TotalExpense / item.ThaliCount,
                CountCategories(item),
                item.Groceries,
                item.Meat,
                item.Gas,
                item.Labour,
                item.Packaging,
                item.Other,
                item.Notes))
            .Where(item => !date.HasValue || item.Date == date.Value.Date)
            .Where(item => string.IsNullOrWhiteSpace(menu) || item.MenuName == menu)
            .Where(item => string.IsNullOrWhiteSpace(term)
                || item.MenuName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || item.Date.ToString("yyyy-MM-dd").Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return View(new ExpensesViewModel
        {
            Search = search,
            MenuFilter = menu,
            SelectedDate = date?.Date,
            MenuNames = await GetMenuNamesAsync(cancellationToken),
            Rows = rows,
            Message = TempData["Message"] as string
        });
    }

    [HttpGet("Expenses/Create")]
    public async Task<IActionResult> CreateExpense(DateTime? date, CancellationToken cancellationToken)
    {
        var selectedDate = (date ?? DateTime.Today).Date;
        if (await context.ExpenseRecords.AnyAsync(item => item.Date == selectedDate, cancellationToken))
        {
            return RedirectToAction(nameof(EditExpense), new { date = selectedDate.ToString("yyyy-MM-dd") });
        }
        if (!await context.DailyMenus.AnyAsync(item => item.Date == selectedDate, cancellationToken))
        {
            TempData["Message"] = "Add a daily menu before recording its expenses.";
            return RedirectToAction(nameof(Menus), new { date = selectedDate.ToString("yyyy-MM-dd") });
        }
        return View("ExpenseForm", new ExpenseFormViewModel { Date = selectedDate });
    }

    [HttpPost("Expenses/Create")]
    public async Task<IActionResult> CreateExpense(ExpenseFormViewModel model, CancellationToken cancellationToken)
    {
        model.Date = model.Date.Date;
        model.Notes = model.Notes?.Trim() ?? string.Empty;
        if (!await context.DailyMenus.AnyAsync(item => item.Date == model.Date, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.Date), "Add a daily menu for this date before recording expenses.");
        }
        if (await context.ExpenseRecords.AnyAsync(item => item.Date == model.Date, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.Date), "Expenses already exist for this date.");
        }
        if (!ModelState.IsValid)
        {
            return View("ExpenseForm", model);
        }

        var expense = CreateExpenseRecord(model);
        context.ExpenseRecords.Add(expense);
        context.AuditLogs.Add(CreateAuditLog("Expense record created", model.Date.ToString("yyyy-MM-dd"), null));
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Expense record added.";
        return RedirectToAction(nameof(Expenses));
    }

    [HttpGet("Expenses/{date}/Edit")]
    public async Task<IActionResult> EditExpense(DateTime date, CancellationToken cancellationToken)
    {
        var expense = await context.ExpenseRecords.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Date == date.Date, cancellationToken);
        return expense is null
            ? NotFound()
            : View("ExpenseForm", new ExpenseFormViewModel
            {
                Date = expense.Date,
                Groceries = expense.Groceries,
                Meat = expense.Meat,
                Gas = expense.Gas,
                Labour = expense.Labour,
                Packaging = expense.Packaging,
                Other = expense.Other,
                ThaliCount = expense.ThaliCount,
                Notes = expense.Notes
            });
    }

    [HttpPost("Expenses/{date}/Edit")]
    public async Task<IActionResult> EditExpense(DateTime date, ExpenseFormViewModel model, CancellationToken cancellationToken)
    {
        model.Notes = model.Notes?.Trim() ?? string.Empty;
        var expense = await context.ExpenseRecords.FirstOrDefaultAsync(item => item.Date == date.Date, cancellationToken);
        if (expense is null)
        {
            return NotFound();
        }
        if (model.Date.Date != date.Date)
        {
            ModelState.AddModelError(nameof(model.Date), "The expense date cannot be changed.");
        }
        if (!ModelState.IsValid)
        {
            model.Date = date.Date;
            return View("ExpenseForm", model);
        }

        expense.Groceries = model.Groceries;
        expense.Meat = model.Meat;
        expense.Gas = model.Gas;
        expense.Labour = model.Labour;
        expense.Packaging = model.Packaging;
        expense.Other = model.Other;
        expense.TotalExpense = SumExpense(model);
        expense.ThaliCount = model.ThaliCount;
        expense.Notes = model.Notes?.Trim() ?? string.Empty;
        context.AuditLogs.Add(CreateAuditLog("Expense record updated", date.ToString("yyyy-MM-dd"), null));
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Expense record updated.";
        return RedirectToAction(nameof(Expenses));
    }

    [HttpPost("Expenses/{date}/Delete")]
    public async Task<IActionResult> DeleteExpense(DateTime date, CancellationToken cancellationToken)
    {
        var expense = await context.ExpenseRecords.FirstOrDefaultAsync(item => item.Date == date.Date, cancellationToken);
        if (expense is null)
        {
            return NotFound();
        }
        context.AuditLogs.Add(CreateAuditLog("Expense record deleted", date.ToString("yyyy-MM-dd"), "Deleted from the expense register."));
        context.ExpenseRecords.Remove(expense);
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Expense record deleted.";
        return RedirectToAction(nameof(Expenses));
    }

    [HttpGet("Expenses/Export")]
    public async Task<IActionResult> ExportExpenses(string? search, string? menu, DateTime? date, CancellationToken cancellationToken)
    {
        var records = await context.ExpenseRecords.AsNoTracking().OrderByDescending(item => item.Date)
            .ToListAsync(cancellationToken);
        var menus = await context.DailyMenus.AsNoTracking().ToDictionaryAsync(item => item.Date, item => item.MenuName, cancellationToken);
        var term = search?.Trim();
        var rows = records.Select(item =>
            {
                var name = menus.TryGetValue(item.Date, out var value) ? value : "—";
                return new[] { item.Date.ToString("yyyy-MM-dd"), name, item.ThaliCount.ToString(CultureInfo.InvariantCulture),
                    item.TotalExpense.ToString("0.00", CultureInfo.InvariantCulture),
                    (item.ThaliCount == 0 ? 0 : item.TotalExpense / item.ThaliCount).ToString("0.00", CultureInfo.InvariantCulture),
                    CountCategories(item).ToString(CultureInfo.InvariantCulture) };
            })
            .Where(row => !date.HasValue || row[0] == date.Value.ToString("yyyy-MM-dd"))
            .Where(row => string.IsNullOrWhiteSpace(menu) || row[1] == menu)
            .Where(row => string.IsNullOrWhiteSpace(term) || row[1].Contains(term, StringComparison.OrdinalIgnoreCase)
                || row[0].Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();
        rows.Insert(0, ["Date", "Menu", "Thali count", "Total", "Per thali", "Cost categories"]);
        return Csv(rows, "expenses.csv");
    }

    [HttpGet("Queries")]
    public async Task<IActionResult> Queries(string? search, string? status, CancellationToken cancellationToken)
    {
        var query = context.QueryRecords.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(item => item.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(item =>
                item.ItsId.ToLower().Contains(term)
                || item.MemberName.ToLower().Contains(term)
                || item.Subject.ToLower().Contains(term)
                || item.Message.ToLower().Contains(term));
        }
        var rows = await query.OrderByDescending(item => item.Id)
            .Select(item => new QueryRow(item.Id, item.ItsId, item.MemberName, item.Subject, item.Message, item.Status, item.Reply))
            .ToListAsync(cancellationToken);
        return View(new QueriesViewModel
        {
            Search = search,
            StatusFilter = status,
            Rows = rows,
            Message = TempData["Message"] as string
        });
    }

    [HttpGet("Notifications")]
    public async Task<IActionResult> Notifications(
        string? search,
        string? type,
        DateTime? date,
        CancellationToken cancellationToken)
    {
        var alertTypes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Unknown tag"] = "Unknown RFID Tag Scan",
            ["Household on hold"] = "Scan Rejected - Household On Hold",
            ["Inactive household"] = "Scan Rejected - Household Inactive",
            ["Duplicate scan"] = "Duplicate Tiffin Scan"
        };
        var query = context.AuditLogs.AsNoTracking()
            .Where(item => item.Action == "Unknown RFID Tag Scan"
                || item.Action == "Scan Rejected - Household On Hold"
                || item.Action == "Scan Rejected - Household Inactive"
                || item.Action == "Duplicate Tiffin Scan");

        if (alertTypes.TryGetValue(type ?? string.Empty, out var action))
        {
            query = query.Where(item => item.Action == action);
        }

        if (date.HasValue)
        {
            var localDate = DateTime.SpecifyKind(date.Value.Date, DateTimeKind.Local);
            var startUtc = localDate.ToUniversalTime();
            var endUtc = localDate.AddDays(1).ToUniversalTime();
            query = query.Where(item => item.Timestamp >= startUtc && item.Timestamp < endUtc);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(item => item.Action.ToLower().Contains(term)
                || item.TargetItem.ToLower().Contains(term)
                || item.PerformedBy.ToLower().Contains(term)
                || (item.Reason != null && item.Reason.ToLower().Contains(term)));
        }

        var records = await query.OrderByDescending(item => item.Timestamp)
            .ToListAsync(cancellationToken);
        var rows = records.Select(item =>
        {
            var alertType = alertTypes.First(pair => pair.Value == item.Action).Key;
            var time = item.Timestamp.ToLocalTime().ToString("HH:mm");
            var message = item.Action switch
            {
                "Unknown RFID Tag Scan" => $"Unknown tag {item.TargetItem} was read at the gate at {time}.",
                "Scan Rejected - Household On Hold" => $"Collection was rejected for {item.TargetItem} at the gate at {time}.",
                "Scan Rejected - Household Inactive" => $"Collection was rejected for inactive household {item.TargetItem} at {time}.",
                _ => $"A duplicate collection scan was recorded for {item.TargetItem} at {time}."
            };
            return new NotificationRow(item.Timestamp, alertType, message);
        }).ToList();

        return View(new NotificationsViewModel
        {
            Search = search,
            TypeFilter = type,
            SelectedDate = date?.Date,
            Types = alertTypes.Keys.OrderBy(value => value).ToArray(),
            Rows = rows
        });
    }

    [HttpGet("Queries/Create")]
    public IActionResult CreateQuery() => View("QueryForm", new QueryFormViewModel());

    [HttpPost("Queries/Create")]
    public async Task<IActionResult> CreateQuery(QueryFormViewModel model, CancellationToken cancellationToken)
    {
        model.ItsId = model.ItsId?.Trim() ?? string.Empty;
        model.MemberName = model.MemberName?.Trim() ?? string.Empty;
        model.Subject = model.Subject?.Trim() ?? string.Empty;
        model.Message = model.Message?.Trim() ?? string.Empty;
        if (!ModelState.IsValid)
        {
            return View("QueryForm", model);
        }

        var record = new QueryRecord
        {
            ItsId = model.ItsId,
            MemberName = model.MemberName,
            Subject = model.Subject,
            Message = model.Message,
            Status = "Open"
        };
        context.QueryRecords.Add(record);
        await context.SaveChangesAsync(cancellationToken);
        context.AuditLogs.Add(CreateAuditLog("Query created", record.Id.ToString(CultureInfo.InvariantCulture), null));
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Query added.";
        return RedirectToAction(nameof(Queries));
    }

    [HttpGet("Queries/{id:int}/Reply")]
    public async Task<IActionResult> ReplyQuery(int id, CancellationToken cancellationToken)
    {
        var query = await context.QueryRecords.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        return query is null
            ? NotFound()
            : View("QueryReplyForm", new QueryReplyViewModel
            {
                Id = query.Id,
                Reply = query.Reply ?? string.Empty,
                Status = query.Status
            });
    }

    [HttpPost("Queries/{id:int}/Reply")]
    public async Task<IActionResult> ReplyQuery(int id, QueryReplyViewModel model, CancellationToken cancellationToken)
    {
        var query = await context.QueryRecords.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (query is null)
        {
            return NotFound();
        }
        if (model.Id != id || !QueryStatuses.Contains(model.Status, StringComparer.Ordinal))
        {
            ModelState.AddModelError(nameof(model.Status), "Choose a valid query status.");
        }
        model.Reply = model.Reply?.Trim() ?? string.Empty;
        if (!ModelState.IsValid)
        {
            return View("QueryReplyForm", model);
        }

        query.Reply = model.Reply;
        query.Status = model.Status;
        context.AuditLogs.Add(CreateAuditLog("Query replied", id.ToString(CultureInfo.InvariantCulture), model.Reply));
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Query response saved.";
        return RedirectToAction(nameof(Queries));
    }

    [HttpPost("Queries/{id:int}/Delete")]
    public async Task<IActionResult> DeleteQuery(int id, CancellationToken cancellationToken)
    {
        var query = await context.QueryRecords.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (query is null)
        {
            return NotFound();
        }
        context.AuditLogs.Add(CreateAuditLog("Query deleted", id.ToString(CultureInfo.InvariantCulture), "Deleted from the query register."));
        context.QueryRecords.Remove(query);
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Query deleted.";
        return RedirectToAction(nameof(Queries));
    }

    [HttpGet("Queries/Export")]
    public async Task<IActionResult> ExportQueries(string? search, string? status, CancellationToken cancellationToken)
    {
        var query = context.QueryRecords.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(item => item.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(item => item.ItsId.ToLower().Contains(term)
                || item.MemberName.ToLower().Contains(term)
                || item.Subject.ToLower().Contains(term)
                || item.Message.ToLower().Contains(term));
        }
        var records = await query.OrderByDescending(item => item.Id).ToListAsync(cancellationToken);
        var rows = records.Select(item => new[]
        {
            item.Id.ToString(CultureInfo.InvariantCulture), item.ItsId, item.MemberName, item.Subject,
            item.Message, item.Status, item.Reply ?? string.Empty
        }).ToList();
        rows.Insert(0, ["ID", "ITS ID", "Member", "Subject", "Message", "Status", "Reply"]);
        return Csv(rows, "queries.csv");
    }

    [HttpGet("Users")]
    public async Task<IActionResult> Users(string? search, string? role, string? status, CancellationToken cancellationToken)
    {
        var query = context.SystemUsers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(role))
        {
            query = query.Where(item => item.Role == role);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(item => item.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(item => item.ItsId.ToLower().Contains(term)
                || item.Name.ToLower().Contains(term)
                || item.Phone.ToLower().Contains(term));
        }
        var rows = await query.OrderBy(item => item.Name)
            .Select(item => new UserRow(item.ItsId, item.Name, item.Role, item.Phone, item.Status, item.LastLogin))
            .ToListAsync(cancellationToken);
        var availableRoles = await GetAvailableRolesAsync(cancellationToken);
        return View(new UsersViewModel
        {
            Search = search,
            RoleFilter = role,
            StatusFilter = status,
            Rows = rows,
            AvailableRoles = availableRoles,
            Message = TempData["Message"] as string
        });
    }

    [HttpGet("Roles")]
    public async Task<IActionResult> Roles(string? search, CancellationToken cancellationToken)
    {
        var definitions = await context.RoleDefinitions.AsNoTracking()
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);
        foreach (var role in definitions)
        {
            RoleAccess.RegisterRoleDefinition(role.Name, role.PermissionsJson, role.DashboardWidgetsJson);
        }

        var userCounts = await context.SystemUsers.AsNoTracking()
            .GroupBy(user => user.Role)
            .Select(group => new { Role = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Role, item => item.Count, cancellationToken);
        var allRows = RoleAccess.BuiltInRoles
            .Select(role => new RolePermissionRow(
                role,
                RoleAccess.GetPermissions(role),
                IsBuiltIn: true,
                AssignedUsers: userCounts.GetValueOrDefault(role),
                DashboardWidgetCount: RoleAccess.GetDashboardWidgets(role).Count))
            .Concat(definitions.Where(role => !RoleAccess.IsBuiltInRole(role.Name)).Select(role => new RolePermissionRow(
                role.Name,
                RoleAccess.GetPermissions(role.Name),
                IsBuiltIn: false,
                AssignedUsers: userCounts.GetValueOrDefault(role.Name),
                DashboardWidgetCount: RoleAccess.GetDashboardWidgets(role.Name).Count)))
            .ToList();
        var rows = string.IsNullOrWhiteSpace(search)
            ? allRows
            : allRows.Where(role => role.Role.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        var currentRole = HttpContext.Session.GetString(SessionUserKey)?.Split('|').ElementAtOrDefault(3);
        return View(new RolesViewModel
        {
            Rows = rows,
            Search = search,
            TotalRoles = allRows.Count,
            CanManageRoles = currentRole == "Admin",
            Message = TempData["Message"] as string
        });
    }

    [HttpGet("Roles/{roleName}/View")]
    public async Task<IActionResult> ViewRole(string roleName, CancellationToken cancellationToken)
    {
        var isBuiltIn = RoleAccess.IsBuiltInRole(roleName);
        var roleDefinition = await context.RoleDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Name == roleName, cancellationToken);
        if (!isBuiltIn && roleDefinition is null)
        {
            return NotFound();
        }
        if (roleDefinition is not null)
        {
            RoleAccess.RegisterRoleDefinition(
                roleDefinition.Name,
                roleDefinition.PermissionsJson,
                roleDefinition.DashboardWidgetsJson);
        }

        var currentRole = HttpContext.Session.GetString(SessionUserKey)?.Split('|').ElementAtOrDefault(3);
        return View("RoleDetails", new RoleDetailsViewModel(
            roleName,
            isBuiltIn,
            await context.SystemUsers.CountAsync(user => user.Role == roleName, cancellationToken),
            RoleAccess.GetPermissions(roleName),
            RoleAccess.GetDashboardWidgets(roleName),
            currentRole == "Admin"));
    }

    [HttpGet("Roles/{roleName}/Activity")]
    public async Task<IActionResult> RoleActivity(string roleName, CancellationToken cancellationToken)
    {
        var isBuiltIn = RoleAccess.IsBuiltInRole(roleName);
        if (!isBuiltIn && !await context.RoleDefinitions.AsNoTracking()
                .AnyAsync(item => item.Name == roleName, cancellationToken))
        {
            return NotFound();
        }

        var rows = await context.AuditLogs.AsNoTracking()
            .Where(item => item.TargetItem == roleName && item.Action.StartsWith("Role "))
            .OrderByDescending(item => item.Timestamp)
            .Take(500)
            .ToListAsync(cancellationToken);
        return View("RoleActivity", new RoleActivityViewModel(roleName, rows));
    }

    [HttpGet("Roles/Create")]
    public IActionResult CreateRole() => View("RoleForm", new RoleFormViewModel
    {
        Permissions = ["dashboard|v"]
    });

    [HttpPost("Roles/Create")]
    public async Task<IActionResult> CreateRole(RoleFormViewModel model, CancellationToken cancellationToken)
    {
        model.Name = model.Name?.Trim() ?? string.Empty;
        ValidateRoleForm(model);
        if (await context.RoleDefinitions.AnyAsync(
                role => role.Name.ToLower() == model.Name.ToLower(), cancellationToken)
            || RoleAccess.BuiltInRoles.Contains(model.Name, StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(model.Name), "A role with this name already exists.");
        }
        if (!ModelState.IsValid)
        {
            return View("RoleForm", model);
        }

        var definition = new RoleDefinition
        {
            Name = model.Name,
            PermissionsJson = JsonSerializer.Serialize(ToPermissionMap(model.Permissions)),
            DashboardWidgetsJson = JsonSerializer.Serialize(model.DashboardWidgets.Distinct(StringComparer.Ordinal).ToArray())
        };
        context.RoleDefinitions.Add(definition);
        context.AuditLogs.Add(CreateAuditLog("Role created", definition.Name, null));
        await context.SaveChangesAsync(cancellationToken);
        RoleAccess.RegisterCustomRole(
            definition.Name,
            definition.PermissionsJson,
            definition.DashboardWidgetsJson);
        TempData["Message"] = "Role added.";
        return RedirectToAction(nameof(Roles));
    }

    [HttpGet("Roles/{roleName}/Edit")]
    public async Task<IActionResult> EditRole(string roleName, CancellationToken cancellationToken)
    {
        if (roleName == "Admin")
        {
            return NotFound();
        }

        var role = await context.RoleDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Name == roleName, cancellationToken);
        var isBuiltIn = RoleAccess.IsBuiltInRole(roleName);
        if (role is null && !isBuiltIn)
        {
            return NotFound();
        }
        if (role is not null)
        {
            RoleAccess.RegisterRoleDefinition(role.Name, role.PermissionsJson, role.DashboardWidgetsJson);
        }
        var permissions = RoleAccess.GetPermissions(roleName)
            .SelectMany(module => module.Value.Select(permission => $"{module.Key}|{permission}"))
            .ToList();
        return View("RoleForm", new RoleFormViewModel
        {
            Name = roleName,
            Permissions = permissions,
            DashboardWidgets = RoleAccess.GetDashboardWidgets(roleName).ToList(),
            IsEdit = true
        });
    }

    [HttpPost("Roles/{roleName}/Edit")]
    public async Task<IActionResult> EditRole(
        string roleName,
        RoleFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (roleName == "Admin")
        {
            return NotFound();
        }

        var role = await context.RoleDefinitions.FirstOrDefaultAsync(item => item.Name == roleName, cancellationToken);
        var isBuiltIn = RoleAccess.IsBuiltInRole(roleName);
        if (role is null && !isBuiltIn)
        {
            return NotFound();
        }

        model.IsEdit = true;
        model.Name = model.Name?.Trim() ?? string.Empty;
        ValidateRoleForm(model);
        if (!string.Equals(model.Name, roleName, StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(model.Name), "A role name cannot be changed after creation.");
        }
        if (!ModelState.IsValid)
        {
            return View("RoleForm", model);
        }

        role ??= new RoleDefinition { Name = roleName };
        role.PermissionsJson = JsonSerializer.Serialize(ToPermissionMap(model.Permissions));
        role.DashboardWidgetsJson = JsonSerializer.Serialize(model.DashboardWidgets.Distinct(StringComparer.Ordinal).ToArray());
        if (context.RoleDefinitions.Entry(role).State == EntityState.Detached)
        {
            context.RoleDefinitions.Add(role);
        }
        context.AuditLogs.Add(CreateAuditLog("Role updated", role.Name, "Permissions and dashboard widgets updated."));
        await context.SaveChangesAsync(cancellationToken);
        RoleAccess.RegisterRoleDefinition(role.Name, role.PermissionsJson, role.DashboardWidgetsJson);
        TempData["Message"] = "Role updated.";
        return RedirectToAction(nameof(Roles));
    }

    [HttpPost("Roles/{roleName}/Delete")]
    public async Task<IActionResult> DeleteRole(string roleName, CancellationToken cancellationToken)
    {
        if (RoleAccess.IsBuiltInRole(roleName))
        {
            return NotFound();
        }

        var role = await context.RoleDefinitions.FirstOrDefaultAsync(item => item.Name == roleName, cancellationToken);
        if (role is null)
        {
            return NotFound();
        }

        if (await context.SystemUsers.AnyAsync(user => user.Role == roleName, cancellationToken))
        {
            TempData["Message"] = "This role is assigned to users and cannot be deleted.";
            return RedirectToAction(nameof(Roles));
        }

        context.RoleDefinitions.Remove(role);
        context.AuditLogs.Add(CreateAuditLog("Role deleted", role.Name, null));
        await context.SaveChangesAsync(cancellationToken);
        RoleAccess.RemoveCustomRole(role.Name);
        TempData["Message"] = "Role deleted.";
        return RedirectToAction(nameof(Roles));
    }

    [HttpGet("Users/Create")]
    public async Task<IActionResult> CreateUser(CancellationToken cancellationToken) =>
        View("UserForm", new UserFormViewModel
        {
            AvailableRoles = await GetAvailableRolesAsync(cancellationToken)
        });

    [HttpPost("Users/Create")]
    public async Task<IActionResult> CreateUser(UserFormViewModel model, CancellationToken cancellationToken)
    {
        model.ItsId = model.ItsId?.Trim() ?? string.Empty;
        model.Name = model.Name?.Trim() ?? string.Empty;
        model.Phone = model.Phone?.Trim() ?? string.Empty;
        ValidateUserForm(model, isNew: true);
        await ValidateAssignedRoleAsync(model.Role, cancellationToken);
        if (await context.SystemUsers.AnyAsync(item => item.ItsId == model.ItsId, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.ItsId), "This ITS ID already has a system account.");
        }
        if (!ModelState.IsValid)
        {
            model.AvailableRoles = await GetAvailableRolesAsync(cancellationToken);
            return View("UserForm", model);
        }

        context.SystemUsers.Add(new SystemUser
        {
            ItsId = model.ItsId,
            Name = model.Name,
            Role = model.Role,
            Phone = model.Phone,
            Status = model.Status,
            PinHash = PinCredential.Hash(model.Pin!)
        });
        context.AuditLogs.Add(CreateAuditLog("System user created", model.ItsId, null));
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "User added.";
        return RedirectToAction(nameof(Users));
    }

    [HttpGet("Users/{itsId}/Edit")]
    public async Task<IActionResult> EditUser(string itsId, CancellationToken cancellationToken)
    {
        var user = await context.SystemUsers.AsNoTracking().FirstOrDefaultAsync(item => item.ItsId == itsId, cancellationToken);
        return user is null
            ? NotFound()
            : View("UserForm", new UserFormViewModel
            {
                ItsId = user.ItsId,
                Name = user.Name,
                Role = user.Role,
                Phone = user.Phone,
                Status = user.Status,
                AvailableRoles = await GetAvailableRolesAsync(cancellationToken)
            });
    }

    [HttpPost("Users/{itsId}/Edit")]
    public async Task<IActionResult> EditUser(string itsId, UserFormViewModel model, CancellationToken cancellationToken)
    {
        var user = await context.SystemUsers.FirstOrDefaultAsync(item => item.ItsId == itsId, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }
        model.ItsId = model.ItsId?.Trim() ?? string.Empty;
        model.Name = model.Name?.Trim() ?? string.Empty;
        model.Phone = model.Phone?.Trim() ?? string.Empty;
        if (model.ItsId != itsId)
        {
            ModelState.AddModelError(nameof(model.ItsId), "A user's ITS ID cannot be changed.");
        }
        ValidateUserForm(model, isNew: false);
        await ValidateAssignedRoleAsync(model.Role, cancellationToken);
        if (!ModelState.IsValid)
        {
            model.AvailableRoles = await GetAvailableRolesAsync(cancellationToken);
            return View("UserForm", model);
        }

        user.Name = model.Name;
        user.Role = model.Role;
        user.Phone = model.Phone;
        user.Status = model.Status;
        if (!string.IsNullOrWhiteSpace(model.Pin))
        {
            user.PinHash = PinCredential.Hash(model.Pin);
        }
        context.AuditLogs.Add(CreateAuditLog("System user updated", itsId, null));
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "User updated.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost("Users/{itsId}/Delete")]
    public async Task<IActionResult> DeleteUser(string itsId, CancellationToken cancellationToken)
    {
        var user = await context.SystemUsers.FirstOrDefaultAsync(item => item.ItsId == itsId, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }
        var current = HttpContext.Session.GetString(SessionUserKey)?.Split('|').FirstOrDefault();
        if (string.Equals(current, itsId, StringComparison.OrdinalIgnoreCase))
        {
            TempData["Message"] = "You cannot delete the account currently in use.";
            return RedirectToAction(nameof(Users));
        }
        context.AuditLogs.Add(CreateAuditLog("System user deleted", itsId, "Deleted from the user register."));
        context.SystemUsers.Remove(user);
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "User deleted.";
        return RedirectToAction(nameof(Users));
    }

    [HttpGet("Users/{itsId}/Activity")]
    public async Task<IActionResult> UserActivity(string itsId, CancellationToken cancellationToken)
    {
        var user = await context.SystemUsers.AsNoTracking().FirstOrDefaultAsync(item => item.ItsId == itsId, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }
        var logs = await context.AuditLogs.AsNoTracking()
            .Where(item => item.PerformedBy == user.Name
                || item.TargetItem == itsId
                || item.TargetItem.StartsWith(itsId + " "))
            .OrderByDescending(item => item.Timestamp)
            .Take(500)
            .ToListAsync(cancellationToken);
        return View("UserActivity", new UserActivityViewModel { User = user, Logs = logs });
    }

    [HttpGet("Users/Export")]
    public async Task<IActionResult> ExportUsers(string? search, string? role, string? status, CancellationToken cancellationToken)
    {
        var query = context.SystemUsers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(role))
        {
            query = query.Where(item => item.Role == role);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(item => item.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(item => item.ItsId.ToLower().Contains(term)
                || item.Name.ToLower().Contains(term)
                || item.Phone.ToLower().Contains(term));
        }
        var users = await query.OrderBy(item => item.Name).ToListAsync(cancellationToken);
        var rows = users.Select(item => new[]
        {
            item.ItsId, item.Name, item.Role, item.Phone, item.Status,
            item.LastLogin?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty
        }).ToList();
        rows.Insert(0, ["ITS ID", "Name", "Role", "Phone", "Status", "Last login"]);
        return Csv(rows, "users.csv");
    }

    [HttpGet("AuditLogs")]
    public async Task<IActionResult> AuditLogs(
        string? search,
        string? performedBy,
        [FromQuery(Name = "action")] string? actionFilter,
        string? table,
        CancellationToken cancellationToken)
    {
        var query = context.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(item => item.TargetItem.ToLower().Contains(term)
                || item.Action.ToLower().Contains(term)
                || (item.Reason != null && item.Reason.ToLower().Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(performedBy))
        {
            query = query.Where(item => item.PerformedBy == performedBy);
        }
        if (!string.IsNullOrWhiteSpace(actionFilter))
        {
            query = query.Where(item => item.Action == actionFilter);
        }
        if (!string.IsNullOrWhiteSpace(table))
        {
            query = query.Where(item => item.TableName == table);
        }
        var rows = await query.OrderByDescending(item => item.Timestamp).Take(1000).ToListAsync(cancellationToken);
        var all = await context.AuditLogs.AsNoTracking().OrderByDescending(item => item.Timestamp)
            .Take(1000).ToListAsync(cancellationToken);
        return View(new AuditLogsViewModel
        {
            Search = search,
            PerformedBy = performedBy,
            ActionFilter = actionFilter,
            TableFilter = table,
            Performers = await context.AuditLogs.AsNoTracking().Select(item => item.PerformedBy).Distinct().OrderBy(item => item).ToListAsync(cancellationToken),
            Actions = await context.AuditLogs.AsNoTracking().Select(item => item.Action).Distinct().OrderBy(item => item).ToListAsync(cancellationToken),
            Tables = await context.AuditLogs.AsNoTracking().Select(item => item.TableName).Distinct().OrderBy(item => item).ToListAsync(cancellationToken),
            Rows = rows,
            DeletedRows = all.Where(item => item.Action.Contains("deleted", StringComparison.OrdinalIgnoreCase)).ToList()
        });
    }

    [HttpGet("AuditLogs/Export")]
    public async Task<IActionResult> ExportAuditLogs(
        string? search,
        string? performedBy,
        [FromQuery(Name = "action")] string? actionFilter,
        string? table,
        CancellationToken cancellationToken)
    {
        var query = context.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(item => item.TargetItem.ToLower().Contains(term)
                || item.Action.ToLower().Contains(term)
                || (item.Reason != null && item.Reason.ToLower().Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(performedBy))
        {
            query = query.Where(item => item.PerformedBy == performedBy);
        }
        if (!string.IsNullOrWhiteSpace(actionFilter))
        {
            query = query.Where(item => item.Action == actionFilter);
        }
        if (!string.IsNullOrWhiteSpace(table))
        {
            query = query.Where(item => item.TableName == table);
        }
        var logs = await query.OrderByDescending(item => item.Timestamp).Take(1000).ToListAsync(cancellationToken);
        var rows = logs.Select(item => new[]
        {
            item.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            item.PerformedBy, item.Action, item.TargetItem, item.TableName, item.Reason ?? string.Empty
        }).ToList();
        rows.Insert(0, ["Timestamp", "Performed by", "Action", "Target", "Table", "Reason"]);
        return Csv(rows, "audit-logs.csv");
    }

    [HttpGet("Feedback")]
    public async Task<IActionResult> Feedback(
        string? search,
        string? menu,
        string? zone,
        int? rating,
        string? quantity,
        string? quality,
        DateTime? fromDate,
        DateTime? toDate,
        DateTime? date,
        CancellationToken cancellationToken)
    {
        var query = context.FeedbackRecords.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(item => item.ItsId.ToLower().Contains(term)
                || item.MemberName.ToLower().Contains(term)
                || item.Comment.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(menu)) query = query.Where(item => item.MenuName == menu);
        if (!string.IsNullOrWhiteSpace(zone)) query = query.Where(item => item.Zone == zone);
        if (rating.HasValue) query = query.Where(item => item.Rating == rating.Value);
        if (!string.IsNullOrWhiteSpace(quantity)) query = query.Where(item => item.Quantity == quantity);
        if (!string.IsNullOrWhiteSpace(quality)) query = query.Where(item => item.Quality == quality);
        if (fromDate.HasValue)
        {
            var from = fromDate.Value.Date;
            query = query.Where(item => item.Date >= from);
        }
        if (toDate.HasValue)
        {
            var to = toDate.Value.Date;
            query = query.Where(item => item.Date <= to);
        }
        var records = await query.OrderByDescending(item => item.Date).ThenByDescending(item => item.Id)
            .Take(1000).ToListAsync(cancellationToken);
        var selectedMenu = await context.DailyMenus.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Date == (date ?? DateTime.Today).Date, cancellationToken);
        var feedbackDate = selectedMenu?.Date ?? (date ?? DateTime.Today).Date;
        var zones = await context.Households.AsNoTracking().Select(item => item.Zone).Distinct().OrderBy(item => item)
            .ToListAsync(cancellationToken);
        return View(new FeedbackViewModel
        {
            FormDate = feedbackDate,
            FormMenuName = selectedMenu?.MenuName ?? string.Empty,
            FeedbackUrl = Url.Action("Submit", "Feedback", new { date = feedbackDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, Request.Scheme) ?? "/Feedback/Submit",
            Search = search,
            MenuFilter = menu,
            ZoneFilter = zone,
            RatingFilter = rating,
            QuantityFilter = quantity,
            QualityFilter = quality,
            FromDate = fromDate?.Date,
            ToDate = toDate?.Date,
            MenuNames = await GetMenuNamesAsync(cancellationToken),
            Zones = zones,
            Rows = records.Select(item => new FeedbackRow(
                item.Id, item.Date, item.MenuName, item.ItsId, item.MemberName, item.Zone,
                item.Rating, item.Quantity, item.Quality, item.Comment)).ToList(),
            Message = TempData["Message"] as string
        });
    }

    [HttpPost("Feedback/{id:int}/Delete")]
    public async Task<IActionResult> DeleteFeedback(int id, CancellationToken cancellationToken)
    {
        var record = await context.FeedbackRecords.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (record is null)
        {
            return NotFound();
        }
        context.AuditLogs.Add(CreateAuditLog("Feedback deleted", $"{record.ItsId} ({record.Date:yyyy-MM-dd})", "Deleted from the feedback register."));
        context.FeedbackRecords.Remove(record);
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Feedback entry deleted.";
        return RedirectToAction(nameof(Feedback));
    }

    [HttpGet("Feedback/Export")]
    public async Task<IActionResult> ExportFeedback(
        string? search, string? menu, string? zone, int? rating,
        string? quantity, string? quality, DateTime? fromDate, DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var query = context.FeedbackRecords.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(item => item.ItsId.ToLower().Contains(term)
                || item.MemberName.ToLower().Contains(term) || item.Comment.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(menu)) query = query.Where(item => item.MenuName == menu);
        if (!string.IsNullOrWhiteSpace(zone)) query = query.Where(item => item.Zone == zone);
        if (rating.HasValue) query = query.Where(item => item.Rating == rating.Value);
        if (!string.IsNullOrWhiteSpace(quantity)) query = query.Where(item => item.Quantity == quantity);
        if (!string.IsNullOrWhiteSpace(quality)) query = query.Where(item => item.Quality == quality);
        if (fromDate.HasValue)
        {
            var from = fromDate.Value.Date;
            query = query.Where(item => item.Date >= from);
        }
        if (toDate.HasValue)
        {
            var to = toDate.Value.Date;
            query = query.Where(item => item.Date <= to);
        }
        var feedback = await query.OrderByDescending(item => item.Date).ThenByDescending(item => item.Id)
            .Take(1000).ToListAsync(cancellationToken);
        var rows = feedback.Select(item => new[]
        {
            item.Date.ToString("yyyy-MM-dd"), item.MenuName, item.ItsId, item.MemberName,
            item.Zone, item.Rating.ToString(CultureInfo.InvariantCulture), item.Quantity, item.Quality, item.Comment
        }).ToList();
        rows.Insert(0, ["Date", "Menu", "ITS ID", "Member", "Zone", "Rating", "Quantity", "Quality", "Comment"]);
        return Csv(rows, "feedback.csv");
    }

    private async Task<IReadOnlyList<string>> GetMenuNamesAsync(CancellationToken cancellationToken) =>
        await context.DailyMenus.AsNoTracking().Select(item => item.MenuName).Distinct().OrderBy(item => item)
            .ToListAsync(cancellationToken);

    private AuditLog CreateAuditLog(string action, string target, string? reason) => new()
    {
        Timestamp = DateTime.Now,
        PerformedBy = HttpContext.Session.GetString(SessionUserKey)?.Split('|').ElementAtOrDefault(2) ?? "System",
        Action = action,
        TargetItem = target,
        TableName = action.StartsWith("Menu", StringComparison.Ordinal) ? "Menus"
            : action.StartsWith("Expense", StringComparison.Ordinal) ? "Expenses"
            : action.StartsWith("Query", StringComparison.Ordinal) ? "Queries"
            : action.StartsWith("Role ", StringComparison.Ordinal) ? "Roles"
            : action.StartsWith("System user", StringComparison.Ordinal) ? "Users"
            : action.StartsWith("Feedback", StringComparison.Ordinal) ? "Feedback"
            : "Audit",
        Reason = reason
    };

    private Task<bool> IsDayFinalizedAsync(DateTime date, CancellationToken cancellationToken)
    {
        var target = date.ToString("yyyy-MM-dd");
        return context.AuditLogs.AsNoTracking().AnyAsync(
            item => item.TableName == "DailyOperations"
                && item.Action == "Distribution day finalized"
                && item.TargetItem == target,
            cancellationToken);
    }

    private static ExpenseRecord CreateExpenseRecord(ExpenseFormViewModel model) => new()
    {
        Date = model.Date,
        Groceries = model.Groceries,
        Meat = model.Meat,
        Gas = model.Gas,
        Labour = model.Labour,
        Packaging = model.Packaging,
        Other = model.Other,
        TotalExpense = SumExpense(model),
        ThaliCount = model.ThaliCount,
        Notes = model.Notes?.Trim() ?? string.Empty
    };

    private static decimal SumExpense(ExpenseFormViewModel model) =>
        model.Groceries + model.Meat + model.Gas + model.Labour + model.Packaging + model.Other;

    private static int CountCategories(ExpenseRecord item) =>
        new[] { item.Groceries, item.Meat, item.Gas, item.Labour, item.Packaging, item.Other }.Count(value => value > 0);

    private void ValidateUserForm(UserFormViewModel model, bool isNew)
    {
        if (model.Name.Contains('|'))
        {
            ModelState.AddModelError(nameof(model.Name), "The name cannot contain the session delimiter character.");
        }
        if (!UserStatuses.Contains(model.Status, StringComparer.Ordinal))
        {
            ModelState.AddModelError(nameof(model.Status), "Choose a valid status.");
        }
        if (isNew && string.IsNullOrWhiteSpace(model.Pin))
        {
            ModelState.AddModelError(nameof(model.Pin), "A PIN is required for a new user.");
        }
        if (!string.IsNullOrWhiteSpace(model.Pin)
            && (model.Pin.Length < 4 || model.Pin.Length > 128))
        {
            ModelState.AddModelError(nameof(model.Pin), "PIN must contain 4 to 128 characters.");
        }
    }

    private async Task ValidateAssignedRoleAsync(string role, CancellationToken cancellationToken)
    {
        var definition = await context.RoleDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Name == role, cancellationToken);
        if (definition is not null)
        {
            RoleAccess.RegisterRoleDefinition(
                definition.Name,
                definition.PermissionsJson,
                definition.DashboardWidgetsJson);
        }
        else if (!RoleAccess.IsBuiltInRole(role))
        {
            ModelState.AddModelError(nameof(UserFormViewModel.Role), "Choose a valid role.");
            return;
        }

        var actorRole = HttpContext.Session.GetString(SessionUserKey)?.Split('|').ElementAtOrDefault(3) ?? string.Empty;
        if (!RoleAccess.CanAssignRole(actorRole, role))
        {
            ModelState.AddModelError(nameof(UserFormViewModel.Role), "You cannot assign a role with permissions beyond your own.");
        }
    }

    private async Task<IReadOnlyList<string>> GetAvailableRolesAsync(CancellationToken cancellationToken)
    {
        var customRoles = await context.RoleDefinitions.AsNoTracking()
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);
        foreach (var role in customRoles)
        {
            RoleAccess.RegisterRoleDefinition(role.Name, role.PermissionsJson, role.DashboardWidgetsJson);
        }

        var actorRole = HttpContext.Session.GetString(SessionUserKey)?.Split('|').ElementAtOrDefault(3) ?? string.Empty;
        return RoleAccess.BuiltInRoles
            .Concat(customRoles.Where(role => !RoleAccess.IsBuiltInRole(role.Name)).Select(role => role.Name))
            .Where(role => RoleAccess.CanAssignRole(actorRole, role))
            .ToArray();
    }

    private void ValidateRoleForm(RoleFormViewModel model)
    {
        if (model.Name.Contains('|'))
        {
            ModelState.AddModelError(nameof(model.Name), "The role name cannot contain the session delimiter character.");
        }
        if (model.Permissions.Any(permission =>
            {
                var parts = permission.Split('|');
                return parts.Length != 2
                    || !RoleAccess.Modules.ContainsKey(parts[0])
                    || parts[1].Length != 1
                    || !"vcedlx".Contains(parts[1][0]);
            }))
        {
            ModelState.AddModelError(nameof(model.Permissions), "Choose valid module permissions.");
        }
        if (!model.Permissions.Contains("dashboard|v", StringComparer.Ordinal))
        {
            ModelState.AddModelError(nameof(model.Permissions), "Dashboard view permission is required.");
        }
        if (model.DashboardWidgets.Any(widget => !RoleAccess.DashboardWidgetOptions.ContainsKey(widget)))
        {
            ModelState.AddModelError(nameof(model.DashboardWidgets), "Choose valid dashboard widgets.");
        }
    }

    private static IReadOnlyDictionary<string, string> ToPermissionMap(IEnumerable<string> selections) =>
        selections.Select(selection => selection.Split('|'))
            .GroupBy(parts => parts[0], StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new string(group.Select(parts => parts[1][0]).Distinct().ToArray()),
                StringComparer.Ordinal);

    private static FileContentResult Csv(IEnumerable<string[]> rows, string fileName)
    {
        var content = new StringBuilder("\uFEFF");
        foreach (var row in rows)
        {
            content.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        }
        return new FileContentResult(Encoding.UTF8.GetBytes(content.ToString()), "text/csv; charset=utf-8")
        {
            FileDownloadName = fileName
        };
    }

    private static string EscapeCsv(string value)
    {
        var safe = value;
        if (safe.Length > 0 && (safe[0] is '=' or '+' or '@' or '\t' or '\r'
            || (safe[0] == '-' && !decimal.TryParse(safe, NumberStyles.Number, CultureInfo.InvariantCulture, out _))))
        {
            safe = "'" + safe;
        }
        return "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

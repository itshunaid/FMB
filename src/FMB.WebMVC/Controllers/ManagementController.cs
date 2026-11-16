using FMB.Application.Interfaces;
using FMB.Domain.Entities;
using FMB.WebMVC.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FMB.WebMVC.Controllers;

[AutoValidateAntiforgeryToken]
public sealed class ManagementController(ITiffinTrackerDbContext context) : Controller
{
    private const string SessionUserKey = "FMB.Auth.User";
    private static readonly string[] HouseholdStatuses = ["Active", "On Hold", "Discontinued"];
    private static readonly string[] DistributorStatuses = ["Active", "Inactive"];

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
        if (!RoleAccess.CanAccessAction(role, "Management", action))
        {
            filterContext.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
            return;
        }

        await next();
    }

    [HttpGet]
    public async Task<IActionResult> Households(
        string? search,
        string? zone,
        string? status,
        CancellationToken cancellationToken)
    {
        var query = context.Households
            .AsNoTracking()
            .Include(household => household.Distributor)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(household => household.Id.ToLower().Contains(term)
                || household.Name.ToLower().Contains(term)
                || household.TagId.ToLower().Contains(term)
                || household.Phone.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(zone))
        {
            query = query.Where(household => household.Zone == zone);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(household => household.Status == status);
        }

        ViewBag.Search = search;
        ViewBag.Zone = zone;
        ViewBag.Status = status;
        ViewBag.Zones = await context.Households.AsNoTracking()
            .Select(household => household.Zone).Distinct().OrderBy(value => value).ToListAsync(cancellationToken);
        var households = await query.OrderBy(household => household.Id).ToListAsync(cancellationToken);
        return View(households);
    }

    [HttpGet]
    public async Task<IActionResult> CreateHousehold(CancellationToken cancellationToken)
    {
        await PopulateDistributorsAsync(null, cancellationToken);
        return View("HouseholdForm", new HouseholdFormViewModel
        {
            ThaliNumber = (await context.Households.Select(item => (int?)item.ThaliNumber)
                .MaxAsync(cancellationToken) ?? 0) + 1
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateHousehold(
        HouseholdFormViewModel model,
        CancellationToken cancellationToken)
    {
        NormalizeHousehold(model);
        ValidateHouseholdPassword(model, required: true);
        var nextThaliNumber = (await context.Households.Select(item => (int?)item.ThaliNumber)
            .MaxAsync(cancellationToken) ?? 0) + 1;
        if (!IsValidHouseholdStatus(model.Status))
        {
            ModelState.AddModelError(nameof(model.Status), "Choose a valid household status.");
        }

        if (await context.Households.AnyAsync(
                household => household.Id == model.Id
                    || household.TagId.Trim().ToLower() == model.TagId.ToLower(),
                cancellationToken))
        {
            ModelState.AddModelError(nameof(model.Id), "The ITS ID or RFID tag is already assigned.");
        }

        if (!await IsDistributorValidAsync(model.DistributorId, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.DistributorId), "Choose an existing distributor.");
        }

        if (!ModelState.IsValid)
        {
            model.Password = null;
            await PopulateDistributorsAsync(model.DistributorId, cancellationToken);
            return View("HouseholdForm", model);
        }

        context.Households.Add(new Household
        {
            Id = model.Id,
            ThaliNumber = nextThaliNumber,
            Name = model.Name,
            Address = model.Address,
            Zone = model.Zone,
            Phone = model.Phone,
            Size = model.Size,
            Status = model.Status,
            TagId = model.TagId,
            DistributorId = model.DistributorId,
            PasswordHash = PinCredential.Hash(model.Password!)
        });
        context.AuditLogs.Add(CreateAuditLog("Household created", model.Id, null));
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Household created.";
        return RedirectToAction(nameof(Households));
    }

    [HttpGet]
    public async Task<IActionResult> EditHousehold(string id, CancellationToken cancellationToken)
    {
        var household = await context.Households.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (household is null)
        {
            return NotFound();
        }

        await PopulateDistributorsAsync(household.DistributorId, cancellationToken);
        return View("HouseholdForm", new HouseholdFormViewModel
        {
            Id = household.Id,
            ThaliNumber = household.ThaliNumber,
            Name = household.Name,
            Address = household.Address,
            Zone = household.Zone,
            Phone = household.Phone,
            Size = household.Size,
            Status = household.Status ?? "Active",
            TagId = household.TagId,
            DistributorId = household.DistributorId
        });
    }

    [HttpPost]
    public async Task<IActionResult> EditHousehold(
        string id,
        HouseholdFormViewModel model,
        CancellationToken cancellationToken)
    {
        NormalizeHousehold(model);
        ValidateHouseholdPassword(model, required: false);
        var household = await context.Households
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (household is null)
        {
            return NotFound();
        }

        if (!IsValidHouseholdStatus(model.Status))
        {
            ModelState.AddModelError(nameof(model.Status), "Choose a valid household status.");
        }

        if (model.Id != id)
        {
            ModelState.AddModelError(nameof(model.Id), "The ITS ID cannot be changed.");
        }
        if (model.ThaliNumber != household.ThaliNumber)
        {
            ModelState.AddModelError(nameof(model.ThaliNumber), "The assigned thali number cannot be changed.");
        }
        if (await context.Households.AnyAsync(
                item => item.Id != id && item.TagId.Trim().ToLower() == model.TagId.ToLower(),
                cancellationToken))
        {
            ModelState.AddModelError(nameof(model.TagId), "The ITS ID or RFID tag is already assigned.");
        }

        if (!await IsDistributorValidAsync(model.DistributorId, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.DistributorId), "Choose an existing distributor.");
        }

        if (!ModelState.IsValid)
        {
            model.Password = null;
            await PopulateDistributorsAsync(model.DistributorId, cancellationToken);
            return View("HouseholdForm", model);
        }

        household.Name = model.Name;
        household.Address = model.Address;
        household.Zone = model.Zone;
        household.Phone = model.Phone;
        household.Size = model.Size;
        household.Status = model.Status;
        household.TagId = model.TagId;
        household.DistributorId = model.DistributorId;
        if (!string.IsNullOrWhiteSpace(model.Password))
        {
            household.PasswordHash = PinCredential.Hash(model.Password);
        }
        context.AuditLogs.Add(CreateAuditLog("Household updated", household.Id, null));
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Household updated.";
        return RedirectToAction(nameof(Households));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteHousehold(string id, CancellationToken cancellationToken)
    {
        var household = await context.Households
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (household is null)
        {
            return NotFound();
        }

        if (await context.TiffinLogs.AnyAsync(log => log.HouseholdId == id, cancellationToken))
        {
            TempData["Message"] = "A household with collection history cannot be deleted. Set its status to Discontinued instead.";
            return RedirectToAction(nameof(Households));
        }

        context.AuditLogs.Add(CreateAuditLog("Household deleted", id, null));
        context.Households.Remove(household);
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Household deleted.";
        return RedirectToAction(nameof(Households));
    }

    [HttpGet]
    public async Task<IActionResult> Distributors(CancellationToken cancellationToken)
    {
        var distributors = await context.Distributors
            .AsNoTracking()
            .Include(distributor => distributor.Households)
            .OrderBy(distributor => distributor.Id)
            .ToListAsync(cancellationToken);
        return View(distributors);
    }

    [HttpGet]
    public IActionResult CreateDistributor() => View("DistributorForm", new DistributorFormViewModel());

    [HttpPost]
    public async Task<IActionResult> CreateDistributor(
        DistributorFormViewModel model,
        CancellationToken cancellationToken)
    {
        NormalizeDistributor(model);
        if (!IsValidDistributorStatus(model.Status))
        {
            ModelState.AddModelError(nameof(model.Status), "Choose a valid distributor status.");
        }

        if (await context.Distributors.AnyAsync(item => item.Id == model.Id, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.Id), "This distributor ID already exists.");
        }

        if (!ModelState.IsValid)
        {
            return View("DistributorForm", model);
        }

        context.Distributors.Add(new Distributor
        {
            Id = model.Id,
            Name = model.Name,
            Phone = model.Phone,
            Status = model.Status
        });
        context.AuditLogs.Add(CreateAuditLog("Distributor created", model.Id, null));
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Distributor created.";
        return RedirectToAction(nameof(Distributors));
    }

    [HttpGet]
    public async Task<IActionResult> EditDistributor(string id, CancellationToken cancellationToken)
    {
        var distributor = await context.Distributors.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (distributor is null)
        {
            return NotFound();
        }

        return View("DistributorForm", new DistributorFormViewModel
        {
            Id = distributor.Id,
            Name = distributor.Name,
            Phone = distributor.Phone,
            Status = distributor.Status
        });
    }

    [HttpPost]
    public async Task<IActionResult> EditDistributor(
        string id,
        DistributorFormViewModel model,
        CancellationToken cancellationToken)
    {
        NormalizeDistributor(model);
        var distributor = await context.Distributors
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (distributor is null)
        {
            return NotFound();
        }

        if (model.Id != id)
        {
            ModelState.AddModelError(nameof(model.Id), "The distributor ID cannot be changed.");
        }

        if (!IsValidDistributorStatus(model.Status))
        {
            ModelState.AddModelError(nameof(model.Status), "Choose a valid distributor status.");
        }

        if (!ModelState.IsValid)
        {
            return View("DistributorForm", model);
        }

        distributor.Name = model.Name;
        distributor.Phone = model.Phone;
        distributor.Status = model.Status;
        context.AuditLogs.Add(CreateAuditLog("Distributor updated", id, null));
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Distributor updated.";
        return RedirectToAction(nameof(Distributors));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteDistributor(string id, CancellationToken cancellationToken)
    {
        var distributor = await context.Distributors
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (distributor is null)
        {
            return NotFound();
        }

        if (await context.Households.AnyAsync(item => item.DistributorId == id, cancellationToken))
        {
            TempData["Message"] = "This distributor has assigned households. Reassign them before deleting.";
            return RedirectToAction(nameof(Distributors));
        }

        context.AuditLogs.Add(CreateAuditLog("Distributor deleted", id, null));
        context.Distributors.Remove(distributor);
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Distributor deleted.";
        return RedirectToAction(nameof(Distributors));
    }

    [HttpGet]
    public async Task<IActionResult> Reconciliation(
        DateTime? date,
        string? search,
        string? zone,
        string? status,
        CancellationToken cancellationToken)
    {
        var selectedDate = (date ?? DateTime.Today).Date;
        var zones = await context.Households.AsNoTracking().Select(item => item.Zone)
            .Distinct().OrderBy(item => item).ToListAsync(cancellationToken);
        var households = await context.Households.AsNoTracking()
            .OrderBy(item => item.Zone)
            .ThenBy(item => item.Name)
            .ToListAsync(cancellationToken);
        var scannedLogs = await context.TiffinLogs.AsNoTracking()
            .Where(item => item.Date == selectedDate)
            .ToListAsync(cancellationToken);
        var logs = scannedLogs
            .GroupBy(item => item.HouseholdId)
            .ToDictionary(group => group.Key, group => group.First());
        var hasMenu = await context.DailyMenus.AsNoTracking()
            .AnyAsync(item => item.Date == selectedDate, cancellationToken);
        var windowClosed = await IsWindowClosedAsync(selectedDate, cancellationToken);
        var dayFinalized = await IsDayFinalizedAsync(selectedDate, cancellationToken);

        var rows = households.Select(household =>
        {
            logs.TryGetValue(household.Id, out var log);
            var status = string.Equals(household.Status?.Trim(), "On Hold", StringComparison.OrdinalIgnoreCase)
                ? "On Hold"
                : string.Equals(log?.Status, "Collected", StringComparison.OrdinalIgnoreCase)
                    ? "Collected"
                    : windowClosed ? "Not Collected" : "Pending";
            return new ReconciliationRow(
                household.Id,
                household.Name,
                household.Zone,
                household.TagId,
                status,
                log?.ScanMode,
                log?.ScanTime);
        })
            .Where(row => string.IsNullOrWhiteSpace(search)
                || row.HouseholdId.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
                || row.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
                || row.TagId.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(row => string.IsNullOrWhiteSpace(zone) || row.Zone == zone)
            .Where(row => string.IsNullOrWhiteSpace(status) || row.Status == status)
            .ToList();

        return View(new ReconciliationViewModel
        {
            Date = selectedDate,
            HasMenu = hasMenu,
            WindowClosed = windowClosed,
            DayFinalized = dayFinalized,
            Search = search,
            Zone = zone,
            StatusFilter = status,
            Zones = zones,
            Rows = rows,
            Message = TempData["Message"] as string
        });
    }

    [HttpPost]
    public async Task<IActionResult> CloseWindow(DateTime date, CancellationToken cancellationToken)
    {
        var selectedDate = date.Date;
        if (selectedDate > DateTime.Today)
        {
            TempData["Message"] = "A future distribution window cannot be closed.";
            return RedirectToAction(nameof(Reconciliation), new { date = selectedDate.ToString("yyyy-MM-dd") });
        }

        if (!await context.DailyMenus.AnyAsync(item => item.Date == selectedDate, cancellationToken))
        {
            TempData["Message"] = "Publish a menu for this date before closing its distribution window.";
            return RedirectToAction(nameof(Reconciliation), new { date = selectedDate.ToString("yyyy-MM-dd") });
        }

        if (await IsWindowClosedAsync(selectedDate, cancellationToken))
        {
            TempData["Message"] = "This distribution window is already closed.";
            return RedirectToAction(nameof(Reconciliation), new { date = selectedDate.ToString("yyyy-MM-dd") });
        }

        context.AuditLogs.Add(CreateAuditLog(
            "Distribution window closed",
            selectedDate.ToString("yyyy-MM-dd"),
            "Uncollected households are no longer pending."));
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Distribution window closed.";
        return RedirectToAction(nameof(Reconciliation), new { date = selectedDate.ToString("yyyy-MM-dd") });
    }

    [HttpPost]
    public async Task<IActionResult> FinalizeDay(DateTime date, CancellationToken cancellationToken)
    {
        var selectedDate = date.Date;
        if (!await IsWindowClosedAsync(selectedDate, cancellationToken))
        {
            TempData["Message"] = "Close the distribution window before finalizing the day.";
            return RedirectToAction(nameof(Reconciliation), new { date = selectedDate.ToString("yyyy-MM-dd") });
        }

        if (await IsDayFinalizedAsync(selectedDate, cancellationToken))
        {
            TempData["Message"] = "This day is already finalized.";
            return RedirectToAction(nameof(Reconciliation), new { date = selectedDate.ToString("yyyy-MM-dd") });
        }

        context.AuditLogs.Add(CreateAuditLog(
            "Distribution day finalized",
            selectedDate.ToString("yyyy-MM-dd"),
            "Collection records for this date are locked."));
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Distribution day finalized.";
        return RedirectToAction(nameof(Reconciliation), new { date = selectedDate.ToString("yyyy-MM-dd") });
    }

    [HttpGet]
    public async Task<IActionResult> Gate(CancellationToken cancellationToken)
    {
        var households = await context.Households.AsNoTracking()
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);
        return View(households);
    }

    [HttpPost]
    public async Task<IActionResult> MarkCollected(
        ManualCollectionViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Message"] = "A reason of at least three characters is required for a manual collection.";
            return RedirectToAction(nameof(Reconciliation));
        }

        var household = await context.Households
            .FirstOrDefaultAsync(item => item.Id == model.HouseholdId, cancellationToken);
        if (household is null)
        {
            return NotFound();
        }

        if (!string.Equals(household.Status?.Trim(), "Active", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Message"] = "Only active households can be manually marked as collected.";
            return RedirectToAction(nameof(Reconciliation));
        }

        var date = DateTime.Today;
        if (await IsDayFinalizedAsync(date, cancellationToken))
        {
            TempData["Message"] = "The day is finalized; collection records can no longer be changed.";
            return RedirectToAction(nameof(Reconciliation));
        }

        var log = await context.TiffinLogs.FirstOrDefaultAsync(
            item => item.Date == date && item.HouseholdId == household.Id,
            cancellationToken);
        if (string.Equals(log?.Status, "Collected", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Message"] = "This household is already collected today.";
            return RedirectToAction(nameof(Reconciliation));
        }

        var now = DateTimeOffset.UtcNow;
        if (log is null)
        {
            context.TiffinLogs.Add(new TiffinLog
            {
                Date = date,
                HouseholdId = household.Id,
                Status = "Collected",
                ScanMode = "manual",
                ScanTime = now.UtcDateTime,
                Remarks = model.Reason.Trim()
            });
        }
        else
        {
            log.Status = "Collected";
            log.ScanMode = "manual";
            log.ScanTime = now.UtcDateTime;
            log.Remarks = model.Reason.Trim();
        }

        context.AuditLogs.Add(CreateAuditLog(
            "Manual collection recorded",
            $"{household.Id} - {household.Name}",
            model.Reason.Trim(),
            now));
        await context.SaveChangesAsync(cancellationToken);
        TempData["Message"] = $"{household.Name} marked collected.";
        return RedirectToAction(nameof(Reconciliation));
    }

    [HttpGet]
    public async Task<IActionResult> Analytics(
        DateTime? startDate,
        DateTime? endDate,
        string? zone,
        string? status,
        string? entryType,
        CancellationToken cancellationToken)
    {
        var end = (endDate ?? DateTime.Today).Date;
        var start = (startDate ?? end.AddDays(-29)).Date;
        var zones = await context.Households.AsNoTracking()
            .Select(household => household.Zone)
            .Distinct()
            .OrderBy(value => value)
            .ToListAsync(cancellationToken);

        if (start > end || (end - start).TotalDays > 365)
        {
            return View(new AnalyticsViewModel
            {
                StartDate = start,
                EndDate = end,
                Zone = zone,
                Status = status,
                EntryType = entryType,
                Zones = zones,
                Message = "Choose a valid date range of no more than 366 days."
            });
        }

        var households = await context.Households.AsNoTracking()
            .Include(household => household.Distributor)
            .Where(household =>
                household.Status != null
                && (household.Status.Trim().ToLower() == "active"
                    || household.Status.Trim().ToLower() == "on hold")
                && (string.IsNullOrEmpty(zone) || household.Zone == zone))
            .OrderBy(household => household.Zone)
            .ThenBy(household => household.Name)
            .ToListAsync(cancellationToken);
        var logs = await context.TiffinLogs.AsNoTracking()
            .Where(log => log.Date >= start && log.Date <= end)
            .ToListAsync(cancellationToken);
        var logLookup = logs
            .GroupBy(log => (log.Date.Date, log.HouseholdId))
            .ToDictionary(group => group.Key, group => group.First());

        var expectedHouseholds = households.Count(item =>
            string.Equals(item.Status?.Trim(), "Active", StringComparison.OrdinalIgnoreCase));
        var activeHouseholds = households
            .Where(item => string.Equals(item.Status?.Trim(), "Active", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var days = Enumerable.Range(0, (end - start).Days + 1)
            .Select(offset => start.AddDays(offset))
            .ToList();
        var statusAllowsCollected = string.IsNullOrEmpty(status) || status == "Collected";
        Func<TiffinLog?, bool> entryTypeAllows = log =>
        {
            if (string.IsNullOrEmpty(entryType))
            {
                return true;
            }

            var mode = log is null
                ? null
                : string.Equals(log.ScanMode, "manual", StringComparison.OrdinalIgnoreCase)
                    ? "Manual"
                    : "Auto";
            return mode == entryType;
        };
        var collectedByZone = households.Select(item => item.Zone)
            .Distinct()
            .ToDictionary(value => value, _ => 0);
        var collectedByDistributor = households.Select(item => item.DistributorId ?? string.Empty)
            .Distinct()
            .ToDictionary(value => value, _ => 0);
        var dailyTrends = new List<DailyTrendRow>(days.Count);
        foreach (var date in days)
        {
            var dailyCollected = 0;
            if (statusAllowsCollected)
            {
                foreach (var household in activeHouseholds)
                {
                    if (!logLookup.TryGetValue((date, household.Id), out var log)
                        || !string.Equals(log.Status, "Collected", StringComparison.OrdinalIgnoreCase)
                        || !entryTypeAllows(log))
                    {
                        continue;
                    }

                    dailyCollected++;
                    collectedByZone[household.Zone]++;
                    collectedByDistributor[household.DistributorId ?? string.Empty]++;
                }
            }

            dailyTrends.Add(new DailyTrendRow(
                date,
                expectedHouseholds,
                dailyCollected,
                expectedHouseholds == 0 ? 0 : (int)Math.Round(dailyCollected * 100d / expectedHouseholds)));
        }

        var expected = expectedHouseholds * days.Count;
        var collectedCount = dailyTrends.Sum(row => row.Collected);
        var monthlyTrends = dailyTrends
            .GroupBy(row => new { row.Date.Year, row.Date.Month })
            .Select(group =>
            {
                var monthlyExpected = group.Sum(row => row.Expected);
                var monthlyCollected = group.Sum(row => row.Collected);
                return new MonthlyTrendRow(
                    new DateTime(group.Key.Year, group.Key.Month, 1).ToString("MMM yyyy"),
                    monthlyExpected,
                    monthlyCollected,
                    monthlyExpected == 0 ? 0 : (int)Math.Round(monthlyCollected * 100d / monthlyExpected));
            })
            .ToList();
        var zoneTrends = households.GroupBy(item => item.Zone).Select(group =>
        {
            var activeCount = group.Count(item =>
                string.Equals(item.Status?.Trim(), "Active", StringComparison.OrdinalIgnoreCase));
            var collected = statusAllowsCollected ? collectedByZone[group.Key] : 0;
            var expectedByZone = activeCount * days.Count;
            return new ZoneTrendRow(group.Key, expectedByZone, collected,
                expectedByZone == 0 ? 0 : (int)Math.Round(collected * 100d / expectedByZone));
        }).OrderBy(item => item.Zone).ToList();
        var distributorTrends = households
            .GroupBy(household => new
            {
                household.DistributorId,
                Name = household.Distributor?.Name ?? "Unassigned"
            })
            .Select(group =>
            {
                var activeCount = group.Count(item =>
                    string.Equals(item.Status?.Trim(), "Active", StringComparison.OrdinalIgnoreCase));
                var collected = statusAllowsCollected
                    ? collectedByDistributor[group.Key.DistributorId ?? string.Empty]
                    : 0;
                var expectedByDistributor = activeCount * days.Count;
                return new DistributorTrendRow(
                    group.Key.DistributorId,
                    group.Key.Name,
                    expectedByDistributor,
                    collected);
            })
            .OrderBy(item => item.Name)
            .ToList();
        var entryRows = new List<AnalyticsEntry>(200);
        foreach (var date in days.AsEnumerable().Reverse())
        {
            foreach (var household in households)
            {
                logLookup.TryGetValue((date, household.Id), out var log);
                var rowStatus = string.Equals(household.Status?.Trim(), "On Hold", StringComparison.OrdinalIgnoreCase)
                    ? "On Hold"
                    : string.Equals(log?.Status, "Collected", StringComparison.OrdinalIgnoreCase)
                        ? "Collected"
                        : "Not Collected";
                var rowEntryType = log is null
                    ? null
                    : string.Equals(log.ScanMode, "manual", StringComparison.OrdinalIgnoreCase)
                        ? "Manual"
                        : "Auto";

                if ((!string.IsNullOrEmpty(status) && rowStatus != status)
                    || (!string.IsNullOrEmpty(entryType) && rowEntryType != entryType))
                {
                    continue;
                }

                entryRows.Add(new AnalyticsEntry(
                    date,
                    household.Id,
                    household.Name,
                    household.Zone,
                    rowStatus,
                    rowEntryType,
                    household.DistributorId,
                    household.Distributor?.Name,
                    string.Equals(household.Status?.Trim(), "Active", StringComparison.OrdinalIgnoreCase)));
                if (entryRows.Count == 200)
                {
                    break;
                }
            }

            if (entryRows.Count == 200)
            {
                break;
            }
        }

        return View(new AnalyticsViewModel
        {
            StartDate = start,
            EndDate = end,
            Zone = zone,
            Status = status,
            EntryType = entryType,
            Expected = expected,
            Collected = collectedCount,
            CollectionRate = expected == 0 ? 0 : (int)Math.Round(collectedCount * 100d / expected),
            Zones = zones,
            DailyTrend = dailyTrends,
            MonthlyTrend = monthlyTrends,
            ZoneTrends = zoneTrends,
            DistributorTrends = distributorTrends,
            Entries = entryRows
        });
    }

    private async Task PopulateDistributorsAsync(string? selectedId, CancellationToken cancellationToken)
    {
        var distributors = await context.Distributors.AsNoTracking()
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);
        ViewBag.Distributors = new SelectList(distributors, nameof(Distributor.Id), nameof(Distributor.Name), selectedId);
    }

    private Task<bool> IsDistributorValidAsync(string? id, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(id)
            ? Task.FromResult(true)
            : context.Distributors.AnyAsync(item => item.Id == id, cancellationToken);

    private static bool IsValidHouseholdStatus(string status) =>
        HouseholdStatuses.Contains(status, StringComparer.OrdinalIgnoreCase);

    private static bool IsValidDistributorStatus(string status) =>
        DistributorStatuses.Contains(status, StringComparer.OrdinalIgnoreCase);

    private static void NormalizeHousehold(HouseholdFormViewModel model)
    {
        model.Id = model.Id?.Trim() ?? string.Empty;
        model.Name = model.Name?.Trim() ?? string.Empty;
        model.Address = model.Address?.Trim() ?? string.Empty;
        model.Zone = model.Zone?.Trim() ?? string.Empty;
        model.Phone = model.Phone?.Trim() ?? string.Empty;
        model.Status = model.Status?.Trim() ?? string.Empty;
        model.TagId = model.TagId?.Trim() ?? string.Empty;
        model.DistributorId = string.IsNullOrWhiteSpace(model.DistributorId) ? null : model.DistributorId.Trim();
    }

    private void ValidateHouseholdPassword(HouseholdFormViewModel model, bool required)
    {
        model.Password = model.Password?.Trim();
        if (string.IsNullOrEmpty(model.Password))
        {
            if (required)
            {
                ModelState.AddModelError(nameof(model.Password), "Set an initial household password.");
            }
        }
        else if (model.Password.Length < 4 || model.Password.Length > 128)
        {
            ModelState.AddModelError(nameof(model.Password), "Password must be between 4 and 128 characters.");
        }
    }

    private static void NormalizeDistributor(DistributorFormViewModel model)
    {
        model.Id = model.Id?.Trim() ?? string.Empty;
        model.Name = model.Name?.Trim() ?? string.Empty;
        model.Phone = model.Phone?.Trim() ?? string.Empty;
        model.Status = model.Status?.Trim() ?? string.Empty;
    }

    private AuditLog CreateAuditLog(
        string action,
        string target,
        string? reason,
        DateTimeOffset? timestamp = null)
    {
        var user = HttpContext.Session.GetString(SessionUserKey)?.Split('|');
        return new AuditLog
        {
            Timestamp = (timestamp ?? DateTimeOffset.UtcNow).UtcDateTime,
            PerformedBy = user is { Length: > 2 } ? user[2] : "Authenticated user",
            Action = action,
            TargetItem = target,
            TableName = action.StartsWith("Distributor", StringComparison.Ordinal)
                ? "Distributors"
                : action.StartsWith("Distribution", StringComparison.Ordinal)
                    ? "DailyOperations"
                : action.StartsWith("Manual collection", StringComparison.Ordinal)
                    ? "TiffinLogs"
                    : "Households",
            Reason = reason
        };
    }

    private Task<bool> IsWindowClosedAsync(DateTime date, CancellationToken cancellationToken) =>
        HasDailyOperationAsync("Distribution window closed", date, cancellationToken);

    private Task<bool> IsDayFinalizedAsync(DateTime date, CancellationToken cancellationToken) =>
        HasDailyOperationAsync("Distribution day finalized", date, cancellationToken);

    private Task<bool> HasDailyOperationAsync(
        string action,
        DateTime date,
        CancellationToken cancellationToken)
    {
        var target = date.ToString("yyyy-MM-dd");
        return context.AuditLogs.AsNoTracking().AnyAsync(
            item => item.TableName == "DailyOperations"
                && item.Action == action
                && item.TargetItem == target,
            cancellationToken);
    }
}

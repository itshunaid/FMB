using FMB.Application.Interfaces;
using FMB.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FMB.WebMVC.Controllers;

[AutoValidateAntiforgeryToken]
[Route("Feedback")]
public sealed class FeedbackController(ITiffinTrackerDbContext context) : Controller
{
    [HttpGet("Submit")]
    public async Task<IActionResult> Submit(DateTime? date, string? itsId, CancellationToken cancellationToken)
    {
        var selectedDate = (date ?? DateTime.Today).Date;
        var sessionUser = HttpContext.Session.GetString("FMB.Auth.User")?.Split('|');
        var householdUser = sessionUser is { Length: > 1 } && sessionUser[1] == "Household";
        if (sessionUser is { Length: > 1 } && sessionUser[1] == "StaffUser")
        {
            return Forbid();
        }

        var menu = await context.DailyMenus.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Date == selectedDate, cancellationToken);
        ViewBag.HouseholdFeedback = householdUser;
        return View(new FeedbackFormViewModel
        {
            Date = selectedDate,
            MenuName = menu?.MenuName ?? string.Empty,
            ItsId = householdUser ? sessionUser![0] : itsId ?? string.Empty
        });
    }

    [HttpPost("Submit")]
    public async Task<IActionResult> Submit(FeedbackFormViewModel model, CancellationToken cancellationToken)
    {
        model.Date = model.Date.Date;
        model.ItsId = model.ItsId?.Trim() ?? string.Empty;
        model.MenuName = model.MenuName?.Trim() ?? string.Empty;
        model.Comment = model.Comment?.Trim() ?? string.Empty;
        var sessionUser = HttpContext.Session.GetString("FMB.Auth.User")?.Split('|');
        var householdUser = sessionUser is { Length: > 1 } && sessionUser[1] == "Household";
        if (sessionUser is { Length: > 1 } && sessionUser[1] == "StaffUser")
        {
            return Forbid();
        }

        if (householdUser)
        {
            model.ItsId = sessionUser![0];
            if (model.Date != DateTime.Today)
            {
                ModelState.AddModelError(nameof(model.Date), "Household feedback is only open for today's menu.");
            }
        }

        var menu = await context.DailyMenus.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Date == model.Date, cancellationToken);
        if (menu is null || !string.Equals(menu.MenuName, model.MenuName, StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(model.Date), "The selected menu is not available for feedback.");
        }

        var household = await context.Households.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == model.ItsId, cancellationToken);
        if (household is null)
        {
            ModelState.AddModelError(nameof(model.ItsId), "Enter a registered ITS ID.");
        }
        var collection = await context.TiffinLogs.AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Date == model.Date && item.HouseholdId == model.ItsId,
                cancellationToken);
        if (!string.Equals(collection?.Status, "Collected", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(model.ItsId), "Feedback is available after your thali has been collected.");
        }
        if (await context.FeedbackRecords.AnyAsync(
                item => item.Date == model.Date && item.ItsId == model.ItsId,
                cancellationToken))
        {
            ModelState.AddModelError(nameof(model.ItsId), "Feedback has already been submitted for this menu.");
        }
        if (!new[] { "Right", "Less", "More" }.Contains(model.Quantity, StringComparer.Ordinal)
            || !new[] { "Excellent", "Good", "Average", "Poor" }.Contains(model.Quality, StringComparer.Ordinal))
        {
            ModelState.AddModelError(string.Empty, "Choose valid quantity and quality ratings.");
        }
        if (!ModelState.IsValid)
        {
            model.MenuName = menu?.MenuName ?? model.MenuName;
            ViewBag.HouseholdFeedback = householdUser;
            return View(model);
        }

        context.FeedbackRecords.Add(new FeedbackRecord
        {
            Date = model.Date,
            MenuName = menu!.MenuName,
            ItsId = household!.Id,
            MemberName = household.Name,
            Zone = household.Zone,
            Rating = model.Rating,
            Quantity = model.Quantity,
            Quality = model.Quality,
            Comment = model.Comment
        });
        context.AuditLogs.Add(new AuditLog
        {
            Timestamp = DateTime.Now,
            PerformedBy = household.Name,
            Action = "Feedback submitted",
            TargetItem = $"{household.Id} ({model.Date:yyyy-MM-dd})",
            TableName = "Feedback"
        });
        await context.SaveChangesAsync(cancellationToken);
        return View("Submitted", model);
    }
}

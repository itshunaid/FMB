using FMB.Application.Interfaces;
using FMB.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FMB.Application.Commands;

public sealed record ScanTiffinCommand(
    string TagId,
    string Source = "Auto",
    DateTimeOffset? Timestamp = null,
    string GateId = "UNKNOWN") : IRequest<ScanTiffinResult>;

public sealed record ScanTiffinResult(
    bool Success,
    string Message,
    string? HouseholdName = null,
    DateTimeOffset? Timestamp = null);

public sealed class ScanTiffinCommandHandler(
    ITiffinTrackerDbContext context,
    ILogger<ScanTiffinCommandHandler>? logger = null)
    : IRequestHandler<ScanTiffinCommand, ScanTiffinResult>
{
    public async Task<ScanTiffinResult> Handle(
        ScanTiffinCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedTagId = request.TagId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTagId))
        {
            return new ScanTiffinResult(false, "RFID Tag ID is required.");
        }

        var source = string.IsNullOrWhiteSpace(request.Source) ? "Auto" : request.Source.Trim();
        var gateId = string.IsNullOrWhiteSpace(request.GateId) ? "UNKNOWN" : request.GateId.Trim();
        var scannedAt = request.Timestamp ?? DateTimeOffset.UtcNow;
        var scanDate = scannedAt.Date;
        var finalizedDate = scanDate.ToString("yyyy-MM-dd");
        if (await context.AuditLogs.AsNoTracking().AnyAsync(
                item => item.TableName == "DailyOperations"
                    && item.Action == "Distribution day finalized"
                    && item.TargetItem == finalizedDate,
                cancellationToken))
        {
            return new ScanTiffinResult(false, "This distribution day is finalized; scans are no longer accepted.");
        }

        var household = await context.Households
            .FirstOrDefaultAsync(
                item => !string.IsNullOrWhiteSpace(item.TagId)
                    && item.TagId.Trim().ToLower() == normalizedTagId.ToLower(),
                cancellationToken);

        if (household is null)
        {
            logger?.LogWarning(
                "Unknown RFID tag {TagId} scanned at gate {GateId} from {Source}.",
                normalizedTagId,
                gateId,
                source);
            context.AuditLogs.Add(CreateAuditLog(
                scannedAt,
                source,
                "Unknown RFID Tag Scan",
                normalizedTagId,
                gateId,
                "Unknown tag"));
            await context.SaveChangesAsync(cancellationToken);
            return new ScanTiffinResult(false, $"Unknown RFID Tag ID: {normalizedTagId}");
        }

        if (string.Equals(household.Status?.Trim(), "On Hold", StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogInformation(
                "Scan rejected for on-hold household {HouseholdId} at gate {GateId}.",
                household.Id,
                gateId);
            context.AuditLogs.Add(CreateAuditLog(
                scannedAt,
                source,
                "Scan Rejected - Household On Hold",
                $"{household.Id} - {household.Name}",
                gateId,
                "Household is on hold"));
            await context.SaveChangesAsync(cancellationToken);
            return new ScanTiffinResult(false, $"Household {household.Name} is currently On Hold.", household.Name);
        }

        if (!string.Equals(household.Status?.Trim(), "Active", StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogInformation(
                "Scan rejected for inactive household {HouseholdId} at gate {GateId}.",
                household.Id,
                gateId);
            context.AuditLogs.Add(CreateAuditLog(
                scannedAt,
                source,
                "Scan Rejected - Household Inactive",
                $"{household.Id} - {household.Name}",
                gateId,
                "Household is not active"));
            await context.SaveChangesAsync(cancellationToken);
            return new ScanTiffinResult(false, $"Household {household.Name} is not Active.", household.Name);
        }

        var log = await context.TiffinLogs
            .FirstOrDefaultAsync(
                item => item.Date == scanDate && item.HouseholdId == household.Id,
                cancellationToken);
        var newLog = log is null;

        if (log is not null && string.Equals(log.Status, "Collected", StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogInformation(
                "Duplicate scan for household {HouseholdId} on {CollectionDate} at gate {GateId}.",
                household.Id,
                scanDate,
                gateId);
            context.AuditLogs.Add(CreateAuditLog(
                scannedAt,
                source,
                "Duplicate Tiffin Scan",
                $"{household.Id} - {household.Name}",
                gateId,
                "Already collected for this day"));
            await context.SaveChangesAsync(cancellationToken);

            var firstScan = log.ScanTime is null
                ? (DateTimeOffset?)null
                : new DateTimeOffset(DateTime.SpecifyKind(log.ScanTime.Value, DateTimeKind.Utc));
            return new ScanTiffinResult(
                true,
                firstScan is null
                    ? "Already collected today."
                    : $"Already collected today at {firstScan.Value:HH:mm:ss}.",
                household.Name,
                firstScan);
        }

        if (log is null)
        {
            log = new TiffinLog
            {
                Date = scanDate,
                HouseholdId = household.Id,
                Status = "Collected"
            };
            context.TiffinLogs.Add(log);
        }

        log.Status = "Collected";
        log.ScanMode = string.Equals(source, "Auto", StringComparison.OrdinalIgnoreCase) ? "auto" : "manual";
        log.ScanTime = scannedAt.UtcDateTime;

        var scanAuditLog = CreateAuditLog(
            scannedAt,
            source,
            "Tiffin Scanned",
            $"{household.Id} - {household.Name}",
            gateId,
            null);
        context.AuditLogs.Add(scanAuditLog);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (newLog)
        {
            context.TiffinLogs.Local.Remove(log!);
            context.AuditLogs.Local.Remove(scanAuditLog);
            var concurrentlyCreatedLog = await context.TiffinLogs.FirstOrDefaultAsync(
                item => item.Date == scanDate && item.HouseholdId == household.Id,
                cancellationToken);
            if (concurrentlyCreatedLog is null)
            {
                throw;
            }

            if (string.Equals(concurrentlyCreatedLog.Status, "Collected", StringComparison.OrdinalIgnoreCase))
            {
                logger?.LogInformation(
                    "Concurrent duplicate scan for household {HouseholdId} on {CollectionDate} at gate {GateId}.",
                    household.Id,
                    scanDate,
                    gateId);
                var duplicateAudit = CreateAuditLog(
                    scannedAt,
                    source,
                    "Duplicate Tiffin Scan",
                    $"{household.Id} - {household.Name}",
                    gateId,
                    "Already collected for this day");
                context.AuditLogs.Add(duplicateAudit);
                await context.SaveChangesAsync(cancellationToken);

                var firstScan = concurrentlyCreatedLog.ScanTime is null
                    ? (DateTimeOffset?)null
                    : new DateTimeOffset(DateTime.SpecifyKind(concurrentlyCreatedLog.ScanTime.Value, DateTimeKind.Utc));
                return new ScanTiffinResult(
                    true,
                    firstScan is null
                        ? "Already collected today."
                        : $"Already collected today at {firstScan.Value:HH:mm:ss}.",
                    household.Name,
                    firstScan);
            }

            concurrentlyCreatedLog.Status = "Collected";
            concurrentlyCreatedLog.ScanMode = string.Equals(source, "Auto", StringComparison.OrdinalIgnoreCase) ? "auto" : "manual";
            concurrentlyCreatedLog.ScanTime = scannedAt.UtcDateTime;
            context.AuditLogs.Add(CreateAuditLog(
                scannedAt,
                source,
                "Tiffin Scanned",
                $"{household.Id} - {household.Name}",
                gateId,
                null));
            await context.SaveChangesAsync(cancellationToken);
        }

        logger?.LogInformation(
            "Collection recorded for household {HouseholdId} on {CollectionDate} at gate {GateId} from {Source}.",
            household.Id,
            scanDate,
            gateId,
            source);
        return new ScanTiffinResult(
            true,
            "Tiffin scan recorded successfully.",
            household.Name,
            scannedAt);
    }

    private static AuditLog CreateAuditLog(
        DateTimeOffset timestamp,
        string source,
        string action,
        string target,
        string gateId,
        string? reason) => new()
    {
        Timestamp = timestamp.UtcDateTime,
        PerformedBy = $"GateScanner ({source})",
        Action = action,
        TargetItem = target,
        TableName = "TiffinLogs",
        Reason = $"Gate: {gateId}{(reason is null ? string.Empty : $" | {reason}")}"
    };
}

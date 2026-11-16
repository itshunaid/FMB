using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FMB.Application.Commands;
using FMB.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace FMB.WebAPI.Controllers;

[ApiController]
[Route("api/gate")]
public sealed class GateController(IMediator mediator) : ControllerBase
{
    /// <summary>Records a gate scan and returns the household collection result.</summary>
    [HttpPost("scan")]
    [ProducesResponseType<ScanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ScanResponse>> ScanTag(
        [FromBody] ScanRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new ScanTiffinCommand(request.TagId, request.Source, request.Timestamp, request.GateId),
            cancellationToken);

        if (!result.Success)
        {
            if (result.Message.StartsWith("Unknown RFID Tag ID:", StringComparison.Ordinal))
            {
                return NotFound(new ProblemDetails { Title = "Unknown RFID tag", Detail = result.Message });
            }

            if (result.Message.Contains("On Hold", StringComparison.OrdinalIgnoreCase)
                || result.Message.Contains("not Active", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(new ProblemDetails { Title = "Household is not active for collection", Detail = result.Message });
            }

            return BadRequest(new ProblemDetails { Title = "Invalid scan", Detail = result.Message });
        }

        return Ok(new ScanResponse(result.Success, result.Message, result.HouseholdName, result.Timestamp));
    }

    /// <summary>Returns collection statistics for the requested date, or today by default.</summary>
    [HttpGet("stats")]
    [ProducesResponseType<GateStatsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GateStatsResponse>> GetStats(
        [FromQuery] DateTimeOffset? date,
        CancellationToken cancellationToken)
    {
        var targetDate = (date?.Date ?? DateTime.Today);
        var stats = await mediator.Send(new GetDashboardStatsQuery(targetDate), cancellationToken);
        return Ok(new GateStatsResponse(
            targetDate,
            stats.TotalActive,
            stats.Collected,
            stats.Pending,
            stats.OnHold,
            stats.CollectionRatePercent,
            stats.TodayMenu));
    }
}

/// <summary>Payload submitted by an RFID gate reader.</summary>
public sealed record ScanRequest
{
    /// <summary>RFID tag identifier assigned to a household.</summary>
    [JsonPropertyName("TagID")]
    [Required, StringLength(50, MinimumLength = 1)]
    public required string TagId { get; init; }

    /// <summary>Timestamp reported by the reader, including its UTC offset.</summary>
    [Required]
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Identifier of the gate reader.</summary>
    [Required, StringLength(100, MinimumLength = 1)]
    public required string GateId { get; init; }

    /// <summary>Origin of the scan, such as Auto or Manual.</summary>
    [Required, StringLength(50, MinimumLength = 1)]
    public required string Source { get; init; }
}

/// <summary>Outcome of processing a gate scan.</summary>
public sealed record ScanResponse(
    bool Success,
    string Message,
    string? HouseholdName,
    DateTimeOffset? Timestamp);

/// <summary>Collection totals returned by the gate stats endpoint.</summary>
public sealed record GateStatsResponse(
    DateTime Date,
    int TotalActive,
    int Collected,
    int Pending,
    int OnHold,
    int CollectionRatePercent,
    string TodayMenu);

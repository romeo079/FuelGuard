using System.Security.Claims;
using FuelGuard.Application;
using FuelGuard.Contracts;
using FuelGuard.Domain;
using FuelGuard.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelGuard.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class OperationsController(
    IFuelGuardWorkflowService workflow,
    FuelGuardDbContext db,
    IStationAccessService stationAccess,
    IPOSIntegrationService pos) : ControllerBase
{
    [HttpPost("incidents")]
    [Authorize(Roles = RoleCatalog.SuperAdministrator + "," + RoleCatalog.StationOwner + "," + RoleCatalog.StationManager + "," + RoleCatalog.Supervisor + "," + RoleCatalog.FuelAttendant + "," + RoleCatalog.SecurityOperator)]
    public async Task<ActionResult<IncidentDto>> ReportIncident(ReportIncidentRequest request, CancellationToken cancellationToken)
    {
        var result = await workflow.ReportIncidentAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, request, cancellationToken);
        return Created($"/api/v1/incidents/{result.Id}", result);
    }

    [HttpGet("stations/{stationId:guid}/incidents")]
    [Authorize(Roles = RoleCatalog.SuperAdministrator + "," + RoleCatalog.StationOwner + "," + RoleCatalog.StationManager + "," + RoleCatalog.Supervisor + "," + RoleCatalog.SecurityOperator + "," + RoleCatalog.ReadOnlyAuditor)]
    public async Task<IActionResult> Incidents(Guid stationId, CancellationToken cancellationToken)
    {
        await EnsureAccess(stationId, cancellationToken);
        return Ok(await db.IncidentReports.AsNoTracking().Where(x => x.StationId == stationId).OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync(cancellationToken));
    }

    [HttpGet("stations/{stationId:guid}/audit")]
    [Authorize(Roles = RoleCatalog.SuperAdministrator + "," + RoleCatalog.StationOwner + "," + RoleCatalog.StationManager + "," + RoleCatalog.ReadOnlyAuditor)]
    public async Task<IActionResult> Audit(Guid stationId, CancellationToken cancellationToken)
    {
        await EnsureAccess(stationId, cancellationToken);
        return Ok(await db.AuditLogs.AsNoTracking().Where(x => x.StationId == stationId).OrderByDescending(x => x.CreatedAtUtc).Take(500).ToListAsync(cancellationToken));
    }

    [HttpGet("stations/{stationId:guid}/pos/health")]
    public async Task<ActionResult<PosHealthDto>> PosHealth(Guid stationId, CancellationToken cancellationToken)
    {
        await EnsureAccess(stationId, cancellationToken);
        return Ok(await pos.CheckHealthAsync(stationId, cancellationToken));
    }

    [HttpPost("gateway/events")]
    [Authorize(Roles = RoleCatalog.SuperAdministrator + "," + RoleCatalog.StationManager + "," + RoleCatalog.SecurityOperator)]
    public async Task<IActionResult> HardwareEvent(HardwareEventDto eventDto, CancellationToken cancellationToken)
    {
        await EnsureAccess(eventDto.StationId, cancellationToken);
        await workflow.RecordHardwareEventAsync(eventDto, cancellationToken);
        return Accepted();
    }

    private async Task EnsureAccess(Guid stationId, CancellationToken cancellationToken)
    {
        if (!await stationAccess.HasStationAccessAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, stationId, cancellationToken))
            throw new UnauthorizedAccessException("User is not assigned to this station.");
    }
}

public sealed record CreateStationRequest(string Code, string Name, string TimeZoneId = "Africa/Johannesburg", decimal VatRate = 0.15m);
public sealed record CreateStationExitRequest(string Code, string Name);
public sealed record CreatePumpRequest(string Number);

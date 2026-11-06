using System.Security.Claims;
using FuelGuard.Domain;
using FuelGuard.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelGuard.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/stations")]
public sealed class StationsController(FuelGuardDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var stations = await db.Stations.AsNoTracking()
            .Where(s => db.UserStationAssignments.Any(a => a.UserId == userId && a.StationId == s.Id && a.IsActive) || User.IsInRole(RoleCatalog.SuperAdministrator))
            .OrderBy(x => x.Name).Select(x => new { x.Id, x.Code, x.Name, x.TimeZoneId, x.CurrencyCode, x.VatRate, x.IsActive })
            .ToListAsync(cancellationToken);
        return Ok(stations);
    }

    [HttpPost]
    [Authorize(Roles = RoleCatalog.SuperAdministrator)]
    public async Task<IActionResult> Create(CreateStationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name) || request.VatRate is < 0 or > 1)
            return BadRequest(new ProblemDetails { Title = "A station code, name, and VAT rate between 0 and 1 are required." });
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId); }
        catch (TimeZoneNotFoundException) { return ValidationProblem("Unknown time zone identifier."); }
        if (await db.Stations.AnyAsync(x => x.Code == request.Code.Trim(), cancellationToken)) return Conflict(new ProblemDetails { Title = "Station code already exists." });
        var station = new Station { Code = request.Code.Trim().ToUpperInvariant(), Name = request.Name.Trim(), TimeZoneId = request.TimeZoneId, VatRate = request.VatRate };
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        db.Stations.Add(station);
        db.UserStationAssignments.Add(new UserStationAssignment { UserId = userId, StationId = station.Id, AssignedByUserId = userId });
        db.AuditLogs.Add(new AuditLog { StationId = station.Id, ActorUserId = userId, Action = "Station.Created", EntityType = nameof(Station), EntityId = station.Id.ToString(), DetailsJson = "{}" });
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(List), new { }, new { station.Id, station.Code, station.Name, station.TimeZoneId, station.CurrencyCode, station.VatRate });
    }

    [HttpGet("{stationId:guid}")]
    public async Task<IActionResult> Get(Guid stationId, CancellationToken cancellationToken)
    {
        if (!await HasAccess(stationId, cancellationToken)) return Forbid();
        var station = await db.Stations.AsNoTracking().Where(x => x.Id == stationId).Select(x => new { x.Id, x.Code, x.Name, x.TimeZoneId, x.CurrencyCode, x.VatRate, x.IsActive }).SingleOrDefaultAsync(cancellationToken);
        return station is null ? NotFound() : Ok(station);
    }

    [HttpPost("{stationId:guid}/exits")]
    [Authorize(Roles = RoleCatalog.SuperAdministrator + "," + RoleCatalog.StationOwner + "," + RoleCatalog.StationManager)]
    public async Task<IActionResult> AddExit(Guid stationId, CreateStationExitRequest request, CancellationToken cancellationToken)
    {
        if (!await HasAccess(stationId, cancellationToken)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new ProblemDetails { Title = "Exit code and name are required." });
        var entity = new StationExit { StationId = stationId, Code = request.Code.Trim().ToUpperInvariant(), Name = request.Name.Trim() };
        db.StationExits.Add(entity);
        db.AuditLogs.Add(new AuditLog { StationId = stationId, ActorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier), Action = "StationExit.Created", EntityType = nameof(StationExit), EntityId = entity.Id.ToString(), DetailsJson = "{}" });
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/v1/stations/{stationId}/exits/{entity.Id}", entity);
    }

    [HttpGet("{stationId:guid}/exits")]
    public async Task<IActionResult> Exits(Guid stationId, CancellationToken cancellationToken)
    {
        if (!await HasAccess(stationId, cancellationToken)) return Forbid();
        return Ok(await db.StationExits.AsNoTracking().Where(x => x.StationId == stationId).OrderBy(x => x.Code).ToListAsync(cancellationToken));
    }

    [HttpPost("{stationId:guid}/pumps")]
    [Authorize(Roles = RoleCatalog.SuperAdministrator + "," + RoleCatalog.StationOwner + "," + RoleCatalog.StationManager)]
    public async Task<IActionResult> AddPump(Guid stationId, CreatePumpRequest request, CancellationToken cancellationToken)
    {
        if (!await HasAccess(stationId, cancellationToken)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Number)) return BadRequest(new ProblemDetails { Title = "Pump number is required." });
        var pump = new Pump { StationId = stationId, Number = request.Number.Trim() };
        db.Pumps.Add(pump);
        db.AuditLogs.Add(new AuditLog { StationId = stationId, ActorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier), Action = "Pump.Created", EntityType = nameof(Pump), EntityId = pump.Id.ToString(), DetailsJson = "{}" });
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/v1/stations/{stationId}/pumps/{pump.Id}", pump);
    }

    [HttpGet("{stationId:guid}/pumps")]
    public async Task<IActionResult> Pumps(Guid stationId, CancellationToken cancellationToken)
    {
        if (!await HasAccess(stationId, cancellationToken)) return Forbid();
        return Ok(await db.Pumps.AsNoTracking().Where(x => x.StationId == stationId).OrderBy(x => x.Number).ToListAsync(cancellationToken));
    }

    private async Task<bool> HasAccess(Guid stationId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        return User.IsInRole(RoleCatalog.SuperAdministrator) ||
            await db.UserStationAssignments.AnyAsync(x => x.UserId == userId && x.StationId == stationId && x.IsActive, cancellationToken);
    }
}

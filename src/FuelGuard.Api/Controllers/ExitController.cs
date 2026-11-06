using System.Security.Claims;
using FuelGuard.Application;
using FuelGuard.Contracts;
using FuelGuard.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelGuard.Api.Controllers;

[ApiController]
[Authorize(Roles = RoleCatalog.SuperAdministrator + "," + RoleCatalog.StationManager + "," + RoleCatalog.Supervisor + "," + RoleCatalog.SecurityOperator)]
[Route("api/v1/exit")]
public sealed class ExitController(IFuelGuardWorkflowService workflow) : ControllerBase
{
    [HttpPost("scan")]
    public async Task<ActionResult<ExitScanResponse>> Scan(ExitScanRequest request, CancellationToken cancellationToken) =>
        Ok(await workflow.ScanExitAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, request, cancellationToken));
}

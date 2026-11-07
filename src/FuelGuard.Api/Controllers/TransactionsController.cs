using System.Security.Claims;
using FuelGuard.Application;
using FuelGuard.Contracts;
using FuelGuard.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelGuard.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class TransactionsController(IFuelGuardWorkflowService workflow) : ControllerBase
{
    [HttpPost("transactions")]
    [Authorize(Roles = RoleCatalog.SuperAdministrator + "," + RoleCatalog.StationOwner + "," + RoleCatalog.StationManager + "," + RoleCatalog.Supervisor + "," + RoleCatalog.FuelAttendant)]
    public async Task<ActionResult<FuelTransactionDto>> Create(CreateFuelTransactionRequest request, CancellationToken cancellationToken)
    {
        var result = await workflow.CreateTransactionAsync(UserId(), request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { stationId = result.StationId, transactionId = result.Id }, result);
    }

    [HttpGet("stations/{stationId:guid}/transactions")]
    public async Task<ActionResult<IReadOnlyList<FuelTransactionDto>>> List(Guid stationId, CancellationToken cancellationToken) =>
        Ok(await workflow.GetTransactionsAsync(UserId(), stationId, cancellationToken));

    [HttpGet("stations/{stationId:guid}/transactions/{transactionId:guid}")]
    public async Task<ActionResult<FuelTransactionDto>> Get(Guid stationId, Guid transactionId, CancellationToken cancellationToken)
    {
        var result = await workflow.GetTransactionAsync(UserId(), stationId, transactionId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("stations/{stationId:guid}/transactions/{transactionId:guid}/simulation/payment")]
    [Authorize(Roles = RoleCatalog.SuperAdministrator + "," + RoleCatalog.StationManager + "," + RoleCatalog.Supervisor)]
    public async Task<ActionResult<PaymentDto>> SimulatePayment(Guid stationId, Guid transactionId, SimulatePaymentRequest request, CancellationToken cancellationToken)
    {
        var outcome = request.Outcome ?? (request.Succeed ? FuelGuard.Domain.PaymentStatus.Paid : FuelGuard.Domain.PaymentStatus.Failed);
        var result = await workflow.SimulatePaymentAsync(UserId(), stationId, transactionId, outcome, request.FinalAmount, request.FinalLitres, cancellationToken);
        return Ok(result);
    }

    [HttpPost("stations/{stationId:guid}/transactions/{transactionId:guid}/authorisations")]
    [Authorize(Roles = RoleCatalog.SuperAdministrator + "," + RoleCatalog.StationOwner + "," + RoleCatalog.StationManager + "," + RoleCatalog.Supervisor + "," + RoleCatalog.FuelAttendant)]
    public async Task<ActionResult<AuthorizationDto>> IssueAuthorization(Guid stationId, Guid transactionId, IssueAuthorizationRequest request, CancellationToken cancellationToken)
    {
        var result = await workflow.IssueAuthorizationAsync(UserId(), stationId, transactionId, request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("stations/{stationId:guid}/transactions/{transactionId:guid}/receipt/reprint")]
    [Authorize(Roles = RoleCatalog.SuperAdministrator + "," + RoleCatalog.StationManager + "," + RoleCatalog.Supervisor + "," + RoleCatalog.FuelAttendant)]
    public async Task<ActionResult<ReceiptDto>> Reprint(Guid stationId, Guid transactionId, CancellationToken cancellationToken) =>
        Ok(await workflow.ReprintReceiptAsync(UserId(), stationId, transactionId, cancellationToken));

    [HttpGet("stations/{stationId:guid}/dashboard")]
    public async Task<ActionResult<DashboardSummaryDto>> Dashboard(Guid stationId, CancellationToken cancellationToken) =>
        Ok(await workflow.GetDashboardSummaryAsync(UserId(), stationId, cancellationToken));

    [HttpGet("stations/{stationId:guid}/devices")]
    public async Task<ActionResult<IReadOnlyList<DeviceHealthDto>>> Devices(Guid stationId, CancellationToken cancellationToken) =>
        Ok(await workflow.GetDevicesAsync(UserId(), stationId, cancellationToken));

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
}

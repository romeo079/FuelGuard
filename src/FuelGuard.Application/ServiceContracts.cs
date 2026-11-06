using FuelGuard.Contracts;
using FuelGuard.Domain;

namespace FuelGuard.Application;

public interface IFuelGuardWorkflowService
{
    Task<FuelTransactionDto> CreateTransactionAsync(string userId, CreateFuelTransactionRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<FuelTransactionDto>> GetTransactionsAsync(string userId, Guid stationId, CancellationToken cancellationToken);
    Task<FuelTransactionDto?> GetTransactionAsync(string userId, Guid stationId, Guid transactionId, CancellationToken cancellationToken);
    Task<PaymentDto> SimulatePaymentAsync(string userId, Guid stationId, Guid transactionId, bool succeed, decimal finalAmount, decimal finalLitres, CancellationToken cancellationToken);
    Task<AuthorizationDto> IssueAuthorizationAsync(string userId, Guid stationId, Guid transactionId, IssueAuthorizationRequest request, CancellationToken cancellationToken);
    Task<ExitScanResponse> ScanExitAsync(string userId, ExitScanRequest request, CancellationToken cancellationToken);
    Task<ReceiptDto> ReprintReceiptAsync(string userId, Guid stationId, Guid transactionId, CancellationToken cancellationToken);
    Task<IncidentDto> ReportIncidentAsync(string userId, ReportIncidentRequest request, CancellationToken cancellationToken);
    Task<DashboardSummaryDto> GetDashboardSummaryAsync(string userId, Guid stationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DeviceHealthDto>> GetDevicesAsync(string userId, Guid stationId, CancellationToken cancellationToken);
    Task RecordHardwareEventAsync(HardwareEventDto eventDto, CancellationToken cancellationToken);
}

public interface IPOSIntegrationService
{
    Task<PosPaymentConfirmation?> VerifyPaymentAsync(string transactionReference, CancellationToken cancellationToken);
    Task<PosHealthDto> CheckHealthAsync(Guid stationId, CancellationToken cancellationToken);
}

public interface ISimulatedPOSIntegrationService : IPOSIntegrationService
{
    Task<PosPaymentConfirmation> SimulatePaymentAsync(string transactionReference, bool succeed, decimal finalAmount, decimal finalLitres, CancellationToken cancellationToken);
}

public interface IStationAccessService
{
    Task<bool> HasStationAccessAsync(string userId, Guid stationId, CancellationToken cancellationToken);
}

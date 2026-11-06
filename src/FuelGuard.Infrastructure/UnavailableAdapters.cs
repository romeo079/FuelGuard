using FuelGuard.Application;
using FuelGuard.Contracts;
using FuelGuard.Domain;
using FuelGuard.Hardware.Abstractions;

namespace FuelGuard.Infrastructure;

public sealed class DisabledPosIntegrationService : IPOSIntegrationService
{
    public Task<PosPaymentConfirmation?> VerifyPaymentAsync(string transactionReference, CancellationToken cancellationToken) => Task.FromResult<PosPaymentConfirmation?>(null);
    public Task<PosHealthDto> CheckHealthAsync(Guid stationId, CancellationToken cancellationToken) => Task.FromResult(new PosHealthDto("NOT CONFIGURED", false, false, "POS supplier integration is not configured."));
}

public sealed class UnsupportedHardwareAdapter : IReceiptPrinterService, IExitScannerService, IANPRService, IVehicleDetectionService, IAccessControllerService, IHardwareHealthService, IPOSIntegrationService
{
    public Task<PrintResult> PrintAsync(ReceiptDto receipt, CancellationToken cancellationToken) => Task.FromResult(new PrintResult(false, "NOT CONFIGURED", "No supplier-approved printer adapter is configured."));
    public Task<string?> ReadNextTokenAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    public Task<AnprObservation?> GetLatestObservationAsync(Guid stationId, Guid exitId, CancellationToken cancellationToken) => Task.FromResult<AnprObservation?>(null);
    public Task<VehiclePresenceObservation> GetObservationAsync(Guid stationId, Guid exitId, CancellationToken cancellationToken) => Task.FromResult(new VehiclePresenceObservation(false, false, "NOT CONFIGURED", DateTimeOffset.UtcNow));
    public Task<ControllerRequestResult> RequestAuthorizationAsync(ExitControllerRequest request, CancellationToken cancellationToken) => Task.FromResult(new ControllerRequestResult(false, false, "No approved controller integration is configured.", "NOT CONFIGURED", null));
    public Task<IReadOnlyList<DeviceHealthDto>> GetHealthAsync(Guid stationId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DeviceHealthDto>>([new DeviceHealthDto(Guid.Empty, "NOT-CONFIGURED", "Hardware", "NOT CONFIGURED", DeviceStatus.Offline, null, false)]);
    public Task<PosPaymentConfirmation?> VerifyPaymentAsync(string transactionReference, CancellationToken cancellationToken) => Task.FromResult<PosPaymentConfirmation?>(null);

    Task<DeviceHealthDto> IReceiptPrinterService.CheckHealthAsync(Guid stationId, CancellationToken cancellationToken) => Task.FromResult(new DeviceHealthDto(Guid.Empty, "NOT-CONFIGURED", "Hardware", "NOT CONFIGURED", DeviceStatus.Offline, null, false));
    Task<DeviceHealthDto> IExitScannerService.CheckHealthAsync(Guid stationId, CancellationToken cancellationToken) => Task.FromResult(new DeviceHealthDto(Guid.Empty, "NOT-CONFIGURED", "Hardware", "NOT CONFIGURED", DeviceStatus.Offline, null, false));
    Task<DeviceHealthDto> IANPRService.CheckHealthAsync(Guid stationId, CancellationToken cancellationToken) => Task.FromResult(new DeviceHealthDto(Guid.Empty, "NOT-CONFIGURED", "Hardware", "NOT CONFIGURED", DeviceStatus.Offline, null, false));
    Task<DeviceHealthDto> IAccessControllerService.CheckHealthAsync(Guid stationId, CancellationToken cancellationToken) => Task.FromResult(new DeviceHealthDto(Guid.Empty, "NOT-CONFIGURED", "Hardware", "NOT CONFIGURED", DeviceStatus.Offline, null, false));
    Task<PosHealthDto> IPOSIntegrationService.CheckHealthAsync(Guid stationId, CancellationToken cancellationToken) => Task.FromResult(new PosHealthDto("NOT CONFIGURED", false, false, "POS supplier integration is not configured."));
}

public sealed class UnavailableHardwareService : UnsupportedHardwareAdapter
{
}

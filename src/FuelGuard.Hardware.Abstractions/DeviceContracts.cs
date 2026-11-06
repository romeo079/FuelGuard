using FuelGuard.Contracts;

namespace FuelGuard.Hardware.Abstractions;

public sealed record PrintResult(bool Success, string AdapterName, string? Message);
public sealed record AnprObservation(string Registration, decimal Confidence, string AdapterName, DateTimeOffset CapturedAtUtc);
public sealed record VehiclePresenceObservation(bool VehiclePresent, bool SensorHealthy, string AdapterName, DateTimeOffset ObservedAtUtc);
public sealed record ControllerRequestResult(bool AcceptedByController, bool SafetyStatusHealthy, string Status, string AdapterName, string? EventReference);
public sealed record ExitControllerRequest(Guid StationId, Guid ExitId, Guid AuthorizationId, string CorrelationId);

public interface IReceiptPrinterService
{
    Task<PrintResult> PrintAsync(ReceiptDto receipt, CancellationToken cancellationToken);
    Task<DeviceHealthDto> CheckHealthAsync(Guid stationId, CancellationToken cancellationToken);
}

public interface IExitScannerService
{
    Task<string?> ReadNextTokenAsync(CancellationToken cancellationToken);
    Task<DeviceHealthDto> CheckHealthAsync(Guid stationId, CancellationToken cancellationToken);
}

public interface IANPRService
{
    Task<AnprObservation?> GetLatestObservationAsync(Guid stationId, Guid exitId, CancellationToken cancellationToken);
    Task<DeviceHealthDto> CheckHealthAsync(Guid stationId, CancellationToken cancellationToken);
}

public interface IVehicleDetectionService
{
    Task<VehiclePresenceObservation> GetObservationAsync(Guid stationId, Guid exitId, CancellationToken cancellationToken);
}

public interface IAccessControllerService
{
    Task<ControllerRequestResult> RequestAuthorizationAsync(ExitControllerRequest request, CancellationToken cancellationToken);
    Task<DeviceHealthDto> CheckHealthAsync(Guid stationId, CancellationToken cancellationToken);
}

public interface IHardwareHealthService
{
    Task<IReadOnlyList<DeviceHealthDto>> GetHealthAsync(Guid stationId, CancellationToken cancellationToken);
}

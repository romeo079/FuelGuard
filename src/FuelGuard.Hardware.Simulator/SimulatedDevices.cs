using System.Collections.Concurrent;
using FuelGuard.Contracts;
using FuelGuard.Domain;
using FuelGuard.Hardware.Abstractions;

namespace FuelGuard.Hardware.Simulator;

public sealed class SimulatedReceiptPrinterService : IReceiptPrinterService
{
    public bool IsOnline { get; set; }
    public ConcurrentQueue<ReceiptDto> PrintedReceipts { get; } = new();

    public Task<PrintResult> PrintAsync(ReceiptDto receipt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsOnline)
            return Task.FromResult(new PrintResult(false, "SIMULATION ONLY", "Simulated printer is offline."));
        PrintedReceipts.Enqueue(receipt);
        return Task.FromResult(new PrintResult(true, "SIMULATION ONLY", "Captured by development simulator; no paper was printed."));
    }

    public Task<DeviceHealthDto> CheckHealthAsync(Guid stationId, CancellationToken cancellationToken) =>
        Task.FromResult(new DeviceHealthDto(Guid.Empty, "SIM-PRINTER", "ReceiptPrinter", "SIMULATION ONLY", IsOnline ? DeviceStatus.Online : DeviceStatus.Offline, IsOnline ? DateTimeOffset.UtcNow : null, true));
}

public sealed class SimulatedExitScannerService : IExitScannerService
{
    private readonly ConcurrentQueue<string> _tokens = new();
    public bool IsOnline { get; set; }
    public void EnqueueToken(string token) => _tokens.Enqueue(token);

    public Task<string?> ReadNextTokenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(IsOnline && _tokens.TryDequeue(out var token) ? token : null);
    }

    public Task<DeviceHealthDto> CheckHealthAsync(Guid stationId, CancellationToken cancellationToken) =>
        Task.FromResult(new DeviceHealthDto(Guid.Empty, "SIM-SCANNER", "QRScanner", "SIMULATION ONLY", IsOnline ? DeviceStatus.Online : DeviceStatus.Offline, IsOnline ? DateTimeOffset.UtcNow : null, true));
}

public sealed class SimulatedAnprService : IANPRService
{
    private readonly ConcurrentDictionary<Guid, AnprObservation> _observations = new();
    public bool IsOnline { get; set; }
    public void InjectObservation(Guid exitId, string registration, decimal confidence) =>
        _observations[exitId] = new AnprObservation(registration, Math.Clamp(confidence, 0m, 1m), "SIMULATION ONLY", DateTimeOffset.UtcNow);

    public Task<AnprObservation?> GetLatestObservationAsync(Guid stationId, Guid exitId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _observations.TryGetValue(exitId, out var observation);
        return Task.FromResult(IsOnline ? observation : null);
    }

    public Task<DeviceHealthDto> CheckHealthAsync(Guid stationId, CancellationToken cancellationToken) =>
        Task.FromResult(new DeviceHealthDto(Guid.Empty, "SIM-ANPR", "ANPR", "SIMULATION ONLY", IsOnline ? DeviceStatus.Online : DeviceStatus.Offline, IsOnline ? DateTimeOffset.UtcNow : null, true));
}

public sealed class SimulatedVehicleDetectionService : IVehicleDetectionService
{
    public bool SensorHealthy { get; set; }
    public bool VehiclePresent { get; set; }

    public Task<VehiclePresenceObservation> GetObservationAsync(Guid stationId, Guid exitId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new VehiclePresenceObservation(VehiclePresent, SensorHealthy, "SIMULATION ONLY", DateTimeOffset.UtcNow));
    }
}

public sealed class SimulatedAccessControllerService : IAccessControllerService
{
    public bool ControllerOnline { get; set; }
    public bool SafetyStatusHealthy { get; set; }

    public Task<ControllerRequestResult> RequestAuthorizationAsync(ExitControllerRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var safe = ControllerOnline && SafetyStatusHealthy;
        return Task.FromResult(new ControllerRequestResult(
            safe,
            safe,
            safe ? "SIMULATED_REQUEST_ACCEPTED_FOR_CONTROLLER_REVIEW_NO_ACTUATION" : "SIMULATED_CONTROLLER_OR_SAFETY_FAULT_NO_REQUEST_ACCEPTED",
            "SIMULATION ONLY",
            Guid.NewGuid().ToString("N")));
    }

    public Task<DeviceHealthDto> CheckHealthAsync(Guid stationId, CancellationToken cancellationToken) =>
        Task.FromResult(new DeviceHealthDto(Guid.Empty, "SIM-CONTROLLER", "AccessController", "SIMULATION ONLY", ControllerOnline ? DeviceStatus.Online : DeviceStatus.Offline, ControllerOnline ? DateTimeOffset.UtcNow : null, true));
}

public sealed class SimulatedHardwareHealthService(
    SimulatedReceiptPrinterService printer,
    SimulatedExitScannerService scanner,
    SimulatedAnprService anpr,
    SimulatedAccessControllerService controller) : IHardwareHealthService
{
    public async Task<IReadOnlyList<DeviceHealthDto>> GetHealthAsync(Guid stationId, CancellationToken cancellationToken) =>
    [
        await printer.CheckHealthAsync(stationId, cancellationToken),
        await scanner.CheckHealthAsync(stationId, cancellationToken),
        await anpr.CheckHealthAsync(stationId, cancellationToken),
        await controller.CheckHealthAsync(stationId, cancellationToken)
    ];
}

using System.Collections.Concurrent;
using FuelGuard.Application;
using FuelGuard.Contracts;
using FuelGuard.Domain;

namespace FuelGuard.Hardware.Simulator;

public sealed class SimulatedPosIntegrationService : ISimulatedPOSIntegrationService
{
    private readonly ConcurrentDictionary<string, PosPaymentConfirmation> _confirmations = new(StringComparer.Ordinal);

    public Task<PosPaymentConfirmation> SimulatePaymentAsync(string transactionReference, bool succeed, decimal finalAmount, decimal finalLitres, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = succeed ? PaymentStatus.Paid : PaymentStatus.Failed;
        var confirmation = new PosPaymentConfirmation(
            "SIMULATION ONLY",
            transactionReference,
            Guid.NewGuid().ToString("N"),
            status,
            finalAmount,
            finalLitres,
            "ZAR",
            true);
        _confirmations[transactionReference] = confirmation;
        return Task.FromResult(confirmation);
    }

    public Task<PosPaymentConfirmation?> VerifyPaymentAsync(string transactionReference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _confirmations.TryGetValue(transactionReference, out var confirmation);
        return Task.FromResult(confirmation);
    }

    public Task<PosHealthDto> CheckHealthAsync(Guid stationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PosHealthDto("SIMULATION ONLY", true, true, "Development simulator; not connected to a real POS."));
    }
}

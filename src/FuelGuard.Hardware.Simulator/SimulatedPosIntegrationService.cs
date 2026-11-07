using System.Collections.Concurrent;
using FuelGuard.Application;
using FuelGuard.Contracts;
using FuelGuard.Domain;

namespace FuelGuard.Hardware.Simulator;

public sealed class SimulatedPosIntegrationService : ISimulatedPOSIntegrationService
{
    private readonly ConcurrentDictionary<string, PosPaymentConfirmation> _confirmations = new(StringComparer.Ordinal);

    public Task<PosPaymentConfirmation> SimulatePaymentAsync(string transactionReference, bool succeed, decimal finalAmount, decimal finalLitres, CancellationToken cancellationToken)
        => SimulatePaymentAsync(transactionReference, succeed ? PaymentStatus.Paid : PaymentStatus.Failed, finalAmount, finalLitres, cancellationToken);

    public Task<PosPaymentConfirmation> SimulatePaymentAsync(string transactionReference, PaymentStatus outcome, decimal finalAmount, decimal finalLitres, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (outcome is not (PaymentStatus.Paid or PaymentStatus.Failed or PaymentStatus.Pending or PaymentStatus.Unknown))
            throw new ArgumentOutOfRangeException(nameof(outcome), "The POS simulator supports Paid, Failed, Pending, and Unknown outcomes.");
        var confirmation = new PosPaymentConfirmation(
            "SIMULATION ONLY",
            transactionReference,
            Guid.NewGuid().ToString("N"),
            outcome,
            finalAmount,
            finalLitres,
            "ZAR",
            true);
        _confirmations[transactionReference] = confirmation;
        return Task.FromResult(confirmation);
    }

    public PosPaymentConfirmation SimulatePayment(string transactionReference, PaymentStatus outcome, decimal finalAmount, decimal finalLitres)
    {
        return SimulatePaymentAsync(transactionReference, outcome, finalAmount, finalLitres, CancellationToken.None).GetAwaiter().GetResult();
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

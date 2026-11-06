using FuelGuard.Contracts;
using FuelGuard.Domain;
using FuelGuard.Hardware.Simulator;

namespace FuelGuard.IntegrationTests;

public sealed class FuelGuardSimulatorIntegrationTests
{
    [Fact]
    public async Task SimulatedPosPayment_ReturnsPaidConfirmation_WhenSucceeding()
    {
        var service = new SimulatedPosIntegrationService();

        var confirmation = await service.SimulatePaymentAsync(
            "SIM-TRANS-001",
            true,
            150.00m,
            45.5m,
            CancellationToken.None);

        Assert.Equal("SIM-TRANS-001", confirmation.TransactionReference);
        Assert.Equal(PaymentStatus.Paid, confirmation.Status);
        Assert.Equal("ZAR", confirmation.CurrencyCode);
        Assert.True(confirmation.IsSimulation);
    }

    [Fact]
    public async Task SimulatedReceiptPrinter_RequiresOnlineState_BeforePrinting()
    {
        var printer = new SimulatedReceiptPrinterService { IsOnline = false };
        var receipt = new ReceiptDto(
            "ROMVEX",
            "FuelGuard North",
            "FG-REF-1",
            "CA 123 456",
            "Pump 2",
            "Diesel 50",
            20.5m,
            580.00m,
            "Paid",
            "token",
            "base64",
            DateTimeOffset.UtcNow,
            true,
            "Simulated printer offline");

        var result = await printer.PrintAsync(receipt, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SIMULATION ONLY", result.AdapterName);
        Assert.Contains("offline", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SimulatedExitScanner_UsesQueueAndReturnsNullWhenOffline()
    {
        var scanner = new SimulatedExitScannerService { IsOnline = false };
        scanner.EnqueueToken("token-123");

        var result = scanner.ReadNextTokenAsync(CancellationToken.None).Result;

        Assert.Null(result);
    }
}

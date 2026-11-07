using FuelGuard.Contracts;
using FuelGuard.Domain;
using FuelGuard.Hardware.Simulator;

namespace FuelGuard.UnitTests;

public sealed class FuelGuardSimulatorDemoServiceTests
{
    [Fact]
    public void DemoLogin_AcceptsOnlyConfiguredDevelopmentCredentials()
    {
        var (service, _, _) = CreateService();

        Assert.NotNull(service.Authenticate("admin@fuelguard.local", "DemoPassword!123"));
        Assert.Null(service.Authenticate("admin@fuelguard.local", "wrong-password"));
        Assert.Null(service.Authenticate("unknown@fuelguard.local", "DemoPassword!123"));
    }

    [Fact]
    public void TransactionCreation_SupportsFixedAmount()
    {
        var (service, user, _) = CreateService();

        var transaction = service.CreateTransaction(user, CreateRequest(FuelTransactionType.FixedAmount, amount: 125m));

        Assert.Equal(FuelTransactionType.FixedAmount, transaction.Type);
        Assert.Equal(125m, transaction.RequestedAmount);
        Assert.Null(transaction.RequestedLitres);
        Assert.Equal(PaymentStatus.Pending, transaction.PaymentStatus);
    }

    [Fact]
    public void TransactionCreation_SupportsFixedLitres()
    {
        var (service, user, _) = CreateService();

        var transaction = service.CreateTransaction(user, CreateRequest(FuelTransactionType.FixedLitres, litres: 25m));

        Assert.Equal(FuelTransactionType.FixedLitres, transaction.Type);
        Assert.Equal(25m, transaction.RequestedLitres);
        Assert.Null(transaction.RequestedAmount);
    }

    [Fact]
    public void TransactionCreation_SupportsFillToFull()
    {
        var (service, user, _) = CreateService();

        var transaction = service.CreateTransaction(user, CreateRequest(FuelTransactionType.FillToFull));

        Assert.Equal(FuelTransactionType.FillToFull, transaction.Type);
        Assert.Null(transaction.RequestedAmount);
        Assert.Null(transaction.RequestedLitres);
    }

    [Theory]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Unknown)]
    public void NonPaidPosOutcomes_NeverGenerateExitAuthorization(PaymentStatus outcome)
    {
        var (service, user, _) = CreateService();
        var transaction = CreatePaidCandidate(service, user, outcome);

        Assert.Equal(outcome, transaction.PaymentStatus);
        Assert.Throws<InvalidOperationException>(() =>
            service.GenerateExitQr(user, transaction.Id, FuelGuardSimulatorDemoService.DemoExitOneId, 10));
        Assert.Empty(service.GetDashboard(user).ExitEvents);
    }

    [Fact]
    public void SuccessfulPayment_RecordsFinalValuesAndSimulationEvidence()
    {
        var (service, user, _) = CreateService();
        var transaction = service.CreateTransaction(user, CreateRequest(FuelTransactionType.FillToFull));

        var payment = service.SubmitPayment(user, transaction.Id, PaymentStatus.Paid, 245.75m, 32.5m);
        var updated = Assert.Single(service.GetDashboard(user).Transactions);

        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.True(payment.IsSimulation);
        Assert.Equal(FuelTransactionStatus.Completed, updated.Status);
        Assert.Equal(245.75m, updated.FinalAmount);
        Assert.Equal(32.5m, updated.FinalLitres);
    }

    [Fact]
    public void FailedPayment_IsRecordedAsFailedAndCannotIssueQr()
    {
        var (service, user, _) = CreateService();
        var transaction = service.CreateTransaction(user, CreateRequest(FuelTransactionType.FillToFull));

        var payment = service.SubmitPayment(user, transaction.Id, PaymentStatus.Failed, 80m, 10m);

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(1, service.GetDashboard(user).FailedPayments);
        Assert.Throws<InvalidOperationException>(() =>
            service.GenerateExitQr(user, transaction.Id, FuelGuardSimulatorDemoService.DemoExitOneId, 10));
    }

    [Fact]
    public void PendingAndUnknownPayments_RemainUnconfirmedAndDoNotSetFinalValues()
    {
        foreach (var outcome in new[] { PaymentStatus.Pending, PaymentStatus.Unknown })
        {
            var (service, user, _) = CreateService();
            var transaction = service.CreateTransaction(user, CreateRequest(FuelTransactionType.FillToFull));

            var payment = service.SubmitPayment(user, transaction.Id, outcome, 80m, 10m);
            var updated = Assert.Single(service.GetDashboard(user).Transactions);

            Assert.Equal(outcome, payment.Status);
            Assert.Equal(outcome, updated.PaymentStatus);
            Assert.Equal(FuelTransactionStatus.Pending, updated.Status);
            Assert.Null(updated.FinalAmount);
            Assert.Null(updated.FinalLitres);
        }
    }

    [Fact]
    public void ExitQr_IsGeneratedAsPngOnlyAfterConfirmedPayment()
    {
        var (service, user, _) = CreateService();
        var transaction = CreatePaidCandidate(service, user, PaymentStatus.Paid);

        var authorization = service.GenerateExitQr(user, transaction.Id, FuelGuardSimulatorDemoService.DemoExitOneId, 10);
        var png = Convert.FromBase64String(authorization.QrCodePngBase64);

        Assert.True(authorization.IsSimulation);
        Assert.Equal(transaction.Id, authorization.FuelTransactionId);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png.Take(8));
        Assert.Equal(1, service.GetDashboard(user).ActiveExitAuthorizations);
    }

    [Fact]
    public void ExpiredExitQr_IsRejectedAndRecorded()
    {
        var (service, user, clock) = CreateService();
        var transaction = CreatePaidCandidate(service, user, PaymentStatus.Paid);
        var authorization = service.GenerateExitQr(user, transaction.Id, FuelGuardSimulatorDemoService.DemoExitOneId, 1);
        clock.Advance(TimeSpan.FromMinutes(2));

        var result = service.ScanExit(user, ScanRequest(authorization.Token));

        Assert.False(result.Accepted);
        Assert.Equal(ExitScanStatus.Expired, result.Status);
        Assert.Contains(service.GetDashboard(user).ExitEvents, x => x.Status == ExitScanStatus.Expired);
    }

    [Fact]
    public void RevokedExitQr_IsRejected()
    {
        var (service, user, _) = CreateService();
        var transaction = CreatePaidCandidate(service, user, PaymentStatus.Paid);
        var authorization = service.GenerateExitQr(user, transaction.Id, FuelGuardSimulatorDemoService.DemoExitOneId, 10);
        service.RevokeAuthorization(user, authorization.Id);

        var result = service.ScanExit(user, ScanRequest(authorization.Token));

        Assert.False(result.Accepted);
        Assert.Equal(ExitScanStatus.Revoked, result.Status);
    }

    [Fact]
    public void ExitQr_IsSingleUseAndDuplicateScanIsRejected()
    {
        var (service, user, _) = CreateService();
        var transaction = CreatePaidCandidate(service, user, PaymentStatus.Paid);
        var authorization = service.GenerateExitQr(user, transaction.Id, FuelGuardSimulatorDemoService.DemoExitOneId, 10);

        var first = service.ScanExit(user, ScanRequest(authorization.Token));
        var duplicate = service.ScanExit(user, ScanRequest(authorization.Token));

        Assert.True(first.Accepted);
        Assert.Equal(ExitScanStatus.AlreadyUsed, duplicate.Status);
        Assert.Equal(1, service.GetDashboard(user).CompletedSimulatedExits);
    }

    [Fact]
    public async Task ConcurrentScans_ConsumeAuthorizationExactlyOnce()
    {
        var (service, user, _) = CreateService();
        var transaction = CreatePaidCandidate(service, user, PaymentStatus.Paid);
        var authorization = service.GenerateExitQr(user, transaction.Id, FuelGuardSimulatorDemoService.DemoExitOneId, 10);

        var results = await Task.WhenAll(Enumerable.Range(0, 24)
            .Select(_ => Task.Run(() => service.ScanExit(user, ScanRequest(authorization.Token)))));

        Assert.Single(results.Where(x => x.Accepted));
        Assert.Equal(23, results.Count(x => x.Status == ExitScanStatus.AlreadyUsed));
        Assert.Equal(1, service.GetDashboard(user).CompletedSimulatedExits);
    }

    [Theory]
    [InlineData(SimulatorSafetyState.VehiclePresent)]
    [InlineData(SimulatorSafetyState.ObstructionDetected)]
    [InlineData(SimulatorSafetyState.SensorFault)]
    [InlineData(SimulatorSafetyState.Unknown)]
    public void MovementRequest_IsRejectedForUnsafeOrUnknownSafetyState(SimulatorSafetyState state)
    {
        var (service, user, _) = CreateService();
        service.SetSafetyState(user, state);

        var result = service.RequestMovement(user);

        Assert.False(result.Accepted);
        Assert.Equal(state, result.SafetyState);
        Assert.Contains("rejected", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SafeMovementRequest_IsSimulatorOnlyAndDoesNotClaimActuation()
    {
        var (service, user, _) = CreateService();

        var result = service.RequestMovement(user);

        Assert.True(result.Accepted);
        Assert.Contains("No physical actuator is connected", result.Message);
    }

    [Fact]
    public void UnsafeScan_IsRejectedWithoutConsumingTheQr()
    {
        var (service, user, _) = CreateService();
        var transaction = CreatePaidCandidate(service, user, PaymentStatus.Paid);
        var authorization = service.GenerateExitQr(user, transaction.Id, FuelGuardSimulatorDemoService.DemoExitOneId, 10);
        service.SetSafetyState(user, SimulatorSafetyState.Unknown);

        var unsafeAttempt = service.ScanExit(user, ScanRequest(authorization.Token));
        service.SetSafetyState(user, SimulatorSafetyState.Safe);
        var safeAttempt = service.ScanExit(user, ScanRequest(authorization.Token));

        Assert.Equal(ExitScanStatus.SensorFault, unsafeAttempt.Status);
        Assert.True(safeAttempt.Accepted);
    }

    [Fact]
    public void TransactionPaymentAndExitWorkflow_CreateAuditEvents()
    {
        var (service, user, _) = CreateService();
        var transaction = CreatePaidCandidate(service, user, PaymentStatus.Paid);
        var authorization = service.GenerateExitQr(user, transaction.Id, FuelGuardSimulatorDemoService.DemoExitOneId, 10);
        service.ScanExit(user, ScanRequest(authorization.Token));

        var actions = service.GetDashboard(user).RecentActivity.Select(x => x.Action).ToArray();

        Assert.Contains("FuelTransaction.Created", actions);
        Assert.Contains("Payment.Simulated", actions);
        Assert.Contains("ExitAuthorization.Issued", actions);
        Assert.Contains("ExitScan.Accepted", actions);
    }

    private static (FuelGuardSimulatorDemoService Service, SimulatorDemoUser User, ManualTimeProvider Clock) CreateService()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var accounts = new[]
        {
            new SimulatorDemoAccount
            {
                Email = "admin@fuelguard.local",
                Password = "DemoPassword!123",
                DisplayName = "Demo Admin",
                Role = "Administrator"
            }
        };
        var service = new FuelGuardSimulatorDemoService(
            accounts,
            new SimulatedPosIntegrationService(),
            new SimulatedExitScannerService { IsOnline = true },
            clock,
            enabled: true);
        return (service, new SimulatorDemoUser(accounts[0].Email, accounts[0].DisplayName, accounts[0].Role), clock);
    }

    private static FuelTransactionDto CreatePaidCandidate(
        FuelGuardSimulatorDemoService service,
        SimulatorDemoUser user,
        PaymentStatus outcome)
    {
        var transaction = service.CreateTransaction(user, CreateRequest(FuelTransactionType.FillToFull));
        service.SubmitPayment(user, transaction.Id, outcome, 100m, 12m);
        return service.GetDashboard(user).Transactions.Single(x => x.Id == transaction.Id);
    }

    private static CreateFuelTransactionRequest CreateRequest(
        FuelTransactionType type,
        decimal? amount = null,
        decimal? litres = null) =>
        new()
        {
            StationId = FuelGuardSimulatorDemoService.DemoStationId,
            PumpId = FuelGuardSimulatorDemoService.DemoPumpOneId,
            FuelType = "Unleaded 95",
            Type = type,
            RequestedAmount = amount,
            RequestedLitres = litres,
            VehicleRegistration = " CA 123 456 "
        };

    private static ExitScanRequest ScanRequest(string token) =>
        new(
            FuelGuardSimulatorDemoService.DemoStationId,
            FuelGuardSimulatorDemoService.DemoExitOneId,
            token,
            null,
            "SIM-QR-SCANNER");

    private sealed class ManualTimeProvider(DateTimeOffset initialTime) : TimeProvider
    {
        private DateTimeOffset _now = initialTime;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan amount) => _now += amount;
    }
}

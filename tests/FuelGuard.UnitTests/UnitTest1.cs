using FuelGuard.Contracts;
using FuelGuard.Domain;
using FuelGuard.Infrastructure;

namespace FuelGuard.UnitTests;

public sealed class FuelGuardDomainTests
{
    [Fact]
    public void CreateFuelTransactionRequest_StoresExplicitFixedAmountRequest()
    {
        var request = new CreateFuelTransactionRequest
        {
            StationId = Guid.NewGuid(),
            PumpId = Guid.NewGuid(),
            FuelType = "Diesel 50",
            Type = FuelTransactionType.FixedAmount,
            RequestedAmount = 145.50m,
            RequestedLitres = null,
            VehicleRegistration = " CA 123 456 "
        };

        Assert.Equal("Diesel 50", request.FuelType);
        Assert.Equal(FuelTransactionType.FixedAmount, request.Type);
        Assert.Equal(145.50m, request.RequestedAmount);
        Assert.Equal(" CA 123 456 ", request.VehicleRegistration);
    }

    [Fact]
    public void Transaction_Defaults_To_Pending_Payment()
    {
        var transaction = new FuelTransaction
        {
            StationId = Guid.NewGuid(),
            PumpId = Guid.NewGuid(),
            FuelType = "Unleaded",
            Reference = "FG-TEST-001"
        };

        Assert.Equal(FuelTransactionStatus.Pending, transaction.Status);
        Assert.Equal(PaymentStatus.Pending, transaction.PaymentStatus);
        Assert.False(string.IsNullOrWhiteSpace(transaction.Reference));
    }

    [Fact]
    public void RoleCatalog_ContainsExpectedSecurityRoles()
    {
        Assert.Contains(RoleCatalog.SuperAdministrator, RoleCatalog.All);
        Assert.Contains(RoleCatalog.FuelAttendant, RoleCatalog.All);
        Assert.Contains(RoleCatalog.ReadOnlyAuditor, RoleCatalog.All);
    }
}

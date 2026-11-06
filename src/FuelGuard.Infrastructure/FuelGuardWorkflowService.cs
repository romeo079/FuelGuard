using System.Data;
using System.Security.Cryptography;
using System.Text;
using FuelGuard.Application;
using FuelGuard.Contracts;
using FuelGuard.Domain;
using FuelGuard.Hardware.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace FuelGuard.Infrastructure;

public sealed class FuelGuardWorkflowService(
    FuelGuardDbContext db,
    IStationAccessService stationAccess,
    IPOSIntegrationService pos,
    IReceiptPrinterService printer,
    IANPRService anpr,
    IVehicleDetectionService vehicleDetection,
    IAccessControllerService accessController,
    IDataProtectionProvider protectionProvider,
    IWebHostEnvironment environment) : IFuelGuardWorkflowService
{
    private readonly IDataProtector _tokenProtector = protectionProvider.CreateProtector("ROMVEX.FuelGuard.ExitToken.v1");

    public async Task<FuelTransactionDto> CreateTransactionAsync(string userId, CreateFuelTransactionRequest request, CancellationToken cancellationToken)
    {
        await EnsureStationAccess(userId, request.StationId, cancellationToken);
        ValidateRequest(request);
        var pump = await db.Pumps.SingleOrDefaultAsync(x => x.Id == request.PumpId && x.StationId == request.StationId && x.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("The selected pump is not active at this station.");
        var registration = NormalizeRegistration(request.VehicleRegistration);
        Vehicle? vehicle = null;
        if (registration.Length > 0)
        {
            vehicle = await db.Vehicles.FirstOrDefaultAsync(x => x.StationId == request.StationId && x.RegistrationNormalized == registration, cancellationToken);
            if (vehicle is null)
            {
                vehicle = new Vehicle { StationId = request.StationId, RegistrationNormalized = registration, RegistrationDisplay = request.VehicleRegistration?.Trim().ToUpperInvariant() };
                db.Vehicles.Add(vehicle);
            }
        }

        var reference = $"FG-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..32];
        var transaction = new FuelTransaction
        {
            StationId = request.StationId,
            PumpId = pump.Id,
            AttendantUserId = userId,
            VehicleId = vehicle?.Id,
            VehicleRegistrationSnapshot = vehicle?.RegistrationDisplay,
            Reference = reference,
            PosTransactionReference = $"SIM-{reference}",
            Type = request.Type,
            FuelType = request.FuelType.Trim(),
            RequestedAmount = request.RequestedAmount,
            RequestedLitres = request.RequestedLitres,
            Status = FuelTransactionStatus.Pending,
            PaymentStatus = PaymentStatus.Pending
        };
        db.FuelTransactions.Add(transaction);
        db.AuditLogs.Add(new AuditLog { StationId = request.StationId, ActorUserId = userId, Action = "FuelTransaction.Created", EntityType = nameof(FuelTransaction), EntityId = transaction.Id.ToString(), DetailsJson = "{}" });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(transaction, pump.Number);
    }

    public async Task<IReadOnlyList<FuelTransactionDto>> GetTransactionsAsync(string userId, Guid stationId, CancellationToken cancellationToken)
    {
        await EnsureStationAccess(userId, stationId, cancellationToken);
        return await db.FuelTransactions.AsNoTracking().Where(x => x.StationId == stationId)
            .OrderByDescending(x => x.CreatedAtUtc).Take(100)
            .Select(x => new FuelTransactionDto(x.Id, x.StationId, x.Reference, x.Pump!.Number, x.VehicleRegistrationSnapshot, x.Type, x.Status, x.PaymentStatus, x.FuelType, x.RequestedAmount, x.RequestedLitres, x.FinalLitres, x.FinalAmount, x.PosTransactionReference, x.CreatedAtUtc, x.CompletedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<FuelTransactionDto?> GetTransactionAsync(string userId, Guid stationId, Guid transactionId, CancellationToken cancellationToken)
    {
        await EnsureStationAccess(userId, stationId, cancellationToken);
        var transaction = await db.FuelTransactions.AsNoTracking().Include(x => x.Pump)
            .FirstOrDefaultAsync(x => x.Id == transactionId && x.StationId == stationId, cancellationToken);
        return transaction is null ? null : ToDto(transaction, transaction.Pump?.Number ?? string.Empty);
    }

    public async Task<PaymentDto> SimulatePaymentAsync(string userId, Guid stationId, Guid transactionId, bool succeed, decimal finalAmount, decimal finalLitres, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("The simulated POS is available only in the Development environment.");
        await EnsureStationAccess(userId, stationId, cancellationToken);
        if (finalAmount <= 0 || finalLitres <= 0)
            throw new ArgumentOutOfRangeException(nameof(finalAmount), "The simulator requires explicit positive POS final amount and litres.");

        var transaction = await db.FuelTransactions.Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.Id == transactionId && x.StationId == stationId, cancellationToken)
            ?? throw new KeyNotFoundException("Transaction not found.");
        if (transaction.Status == FuelTransactionStatus.Cancelled)
            throw new InvalidOperationException("Cancelled transactions cannot be paid.");
        if (transaction.Type == FuelTransactionType.FixedAmount && finalAmount > transaction.RequestedAmount)
            throw new InvalidOperationException("The POS final amount cannot exceed the requested fixed amount.");
        if (transaction.Type == FuelTransactionType.FixedLitres && finalLitres > transaction.RequestedLitres)
            throw new InvalidOperationException("The POS final litres cannot exceed the requested fixed litres.");
        if (transaction.Payments.Any(x => x.Status == PaymentStatus.Paid))
            return ToPaymentDto(transaction.Payments.OrderByDescending(x => x.CreatedAtUtc).First(x => x.Status == PaymentStatus.Paid));

        if (pos is not ISimulatedPOSIntegrationService simulatedPos)
            throw new InvalidOperationException("No simulated POS adapter is registered in this environment.");
        var confirmation = await simulatedPos.SimulatePaymentAsync(transaction.PosTransactionReference!, succeed, finalAmount, finalLitres, cancellationToken);
        return await ApplyPaymentConfirmationAsync(userId, transaction, confirmation, cancellationToken);
    }

    private async Task<PaymentDto> ApplyPaymentConfirmationAsync(string userId, FuelTransaction transaction, PosPaymentConfirmation confirmation, CancellationToken cancellationToken)
    {
        if (confirmation.TransactionReference != transaction.PosTransactionReference || confirmation.CurrencyCode != "ZAR")
            throw new InvalidOperationException("POS confirmation reference or currency does not match the transaction.");
        if (confirmation.IsSimulation && !environment.IsDevelopment())
            throw new InvalidOperationException("Simulated payment confirmation is prohibited outside Development.");
        if (confirmation.Status == PaymentStatus.Paid && (confirmation.Amount <= 0 || confirmation.FinalLitres <= 0))
            throw new InvalidOperationException("A paid POS confirmation must include final positive amount and litres.");

        var duplicate = await db.PaymentVerificationLogs.FirstOrDefaultAsync(x => x.FuelTransactionId == transaction.Id && x.ProviderEventId == confirmation.EventId, cancellationToken);
        if (duplicate is not null)
        {
            var existing = await db.PaymentRecords.FirstOrDefaultAsync(x => x.FuelTransactionId == transaction.Id && x.ProviderEventId == confirmation.EventId, cancellationToken);
            if (existing is not null) return ToPaymentDto(existing);
            throw new InvalidOperationException("Duplicate POS event was already recorded without a matching payment record.");
        }

        var payment = new PaymentRecord
        {
            FuelTransactionId = transaction.Id,
            Provider = confirmation.Provider,
            ProviderTransactionReference = confirmation.TransactionReference,
            ProviderEventId = confirmation.EventId,
            Status = confirmation.Status,
            Amount = confirmation.Amount,
            CurrencyCode = confirmation.CurrencyCode,
            IsSimulation = confirmation.IsSimulation,
            VerifiedAtUtc = DateTimeOffset.UtcNow
        };
        transaction.PaymentStatus = confirmation.Status;
        if (confirmation.Status is PaymentStatus.Paid or PaymentStatus.Failed or PaymentStatus.Cancelled or PaymentStatus.Refunded or PaymentStatus.Reversed)
        {
            transaction.FinalAmount = confirmation.Amount;
            transaction.FinalLitres = confirmation.FinalLitres;
            transaction.Status = FuelTransactionStatus.Completed;
            transaction.CompletedAtUtc = DateTimeOffset.UtcNow;
        }
        db.PaymentRecords.Add(payment);
        db.PaymentVerificationLogs.Add(new PaymentVerificationLog
        {
            FuelTransactionId = transaction.Id,
            PaymentRecordId = payment.Id,
            ProviderEventId = confirmation.EventId,
            Outcome = confirmation.Status.ToString(),
            SafeMessage = confirmation.IsSimulation ? "SIMULATION ONLY" : "Supplier-confirmed status"
        });
        db.POSIntegrationLogs.Add(new POSIntegrationLog
        {
            StationId = transaction.StationId,
            Operation = "VerifyPayment",
            ExternalReference = confirmation.TransactionReference,
            Outcome = confirmation.Status.ToString(),
            SafeMessage = confirmation.IsSimulation ? "SIMULATION ONLY" : "Supplier response recorded"
        });
        if (confirmation.Status is PaymentStatus.Refunded or PaymentStatus.Reversed or PaymentStatus.Cancelled)
        {
            var activeAuthorizations = await db.ExitAuthorisations.Where(x => x.FuelTransactionId == transaction.Id && x.ConsumedAtUtc == null && x.RevokedAtUtc == null).ToListAsync(cancellationToken);
            foreach (var authorization in activeAuthorizations) authorization.RevokedAtUtc = DateTimeOffset.UtcNow;
        }
        db.AuditLogs.Add(new AuditLog { StationId = transaction.StationId, ActorUserId = userId, Action = "Payment.Verified", EntityType = nameof(FuelTransaction), EntityId = transaction.Id.ToString(), DetailsJson = $"{{\"status\":\"{confirmation.Status}\",\"simulation\":{confirmation.IsSimulation.ToString().ToLowerInvariant()}}}" });
        await db.SaveChangesAsync(cancellationToken);
        return ToPaymentDto(payment);
    }

    public async Task<AuthorizationDto> IssueAuthorizationAsync(string userId, Guid stationId, Guid transactionId, IssueAuthorizationRequest request, CancellationToken cancellationToken)
    {
        await EnsureStationAccess(userId, stationId, cancellationToken);
        var transaction = await db.FuelTransactions.Include(x => x.Station).Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.Id == transactionId && x.StationId == stationId, cancellationToken)
            ?? throw new KeyNotFoundException("Transaction not found.");
        if (transaction.Status != FuelTransactionStatus.Completed || transaction.PaymentStatus != PaymentStatus.Paid || transaction.FinalAmount is null || transaction.FinalLitres is null)
            throw new InvalidOperationException("Exit authorization requires a completed transaction and verified POS-paid final amount and litres.");
        var paidEvidence = transaction.Payments.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault(x => x.Status == PaymentStatus.Paid);
        if (paidEvidence is null || (paidEvidence.IsSimulation && !environment.IsDevelopment()))
            throw new InvalidOperationException("No eligible independently verified payment evidence exists.");
        var exit = await db.StationExits.SingleOrDefaultAsync(x => x.Id == request.StationExitId && x.StationId == stationId && x.IsEnabled, cancellationToken)
            ?? throw new KeyNotFoundException("The selected exit is not enabled at this station.");
        var active = await db.ExitAuthorisations.FirstOrDefaultAsync(x => x.FuelTransactionId == transactionId && x.ConsumedAtUtc == null && x.RevokedAtUtc == null && x.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken);
        if (active is not null)
            throw new InvalidOperationException("A valid authorization already exists. Reprint its receipt instead of creating another token.");

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var authorization = new ExitAuthorisation
        {
            FuelTransactionId = transactionId,
            StationId = stationId,
            StationExitId = exit.Id,
            TokenHash = HashToken(token),
            ProtectedToken = _tokenProtector.Protect(token),
            VehicleRegistrationSnapshot = transaction.VehicleRegistrationSnapshot ?? string.Empty,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(request.ExpiryMinutes)
        };
        db.ExitAuthorisations.Add(authorization);
        db.AuditLogs.Add(new AuditLog { StationId = stationId, ActorUserId = userId, Action = "ExitAuthorization.Issued", EntityType = nameof(ExitAuthorisation), EntityId = authorization.Id.ToString(), DetailsJson = "{\"tokenStoredAsHashAndProtectedValue\":true}" });
        await db.SaveChangesAsync(cancellationToken);
        return ToAuthorizationDto(authorization, token, transaction.PaymentStatus == PaymentStatus.Paid && paidEvidence.IsSimulation);
    }

    public async Task<ExitScanResponse> ScanExitAsync(string userId, ExitScanRequest request, CancellationToken cancellationToken)
    {
        await EnsureStationAccess(userId, request.StationId, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 200)
            return await RecordRejectedScan(request, null, ExitScanStatus.Invalid, "Invalid authorization token.", cancellationToken);
        var hash = HashToken(request.Token);
        var candidate = await db.ExitAuthorisations.Include(x => x.FuelTransaction).ThenInclude(x => x!.Payments)
            .FirstOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        var rejection = ValidateAuthorization(candidate, request, hash);
        if (rejection is not null)
            return await RecordRejectedScan(request, candidate, rejection.Value.Status, rejection.Value.Message, cancellationToken);

        var observation = await anpr.GetLatestObservationAsync(request.StationId, request.StationExitId, cancellationToken);
        var detectedRegistration = NormalizeRegistration(observation?.Registration ?? request.ObservedRegistration);
        var expectedRegistration = NormalizeRegistration(candidate!.VehicleRegistrationSnapshot);
        if (observation is { Confidence: >= 0.85m } && expectedRegistration.Length > 0 && detectedRegistration.Length > 0 && detectedRegistration != expectedRegistration)
            return await RecordRejectedScan(request, candidate, ExitScanStatus.VehicleMismatch, "High-confidence registration mismatch requires supervisor review.", cancellationToken, detectedRegistration);

        var vehicle = await vehicleDetection.GetObservationAsync(request.StationId, request.StationExitId, cancellationToken);
        if (!vehicle.SensorHealthy || !vehicle.VehiclePresent)
            return await RecordRejectedScan(request, candidate, ExitScanStatus.SensorFault, "Vehicle presence is absent or uncertain; no controller request was sent.", cancellationToken, detectedRegistration);
        var controllerHealth = await accessController.CheckHealthAsync(request.StationId, cancellationToken);
        if (controllerHealth.Status != DeviceStatus.Online)
            return await RecordRejectedScan(request, candidate, ExitScanStatus.ControllerUnavailable, "Approved controller is unavailable; no authorization request was sent.", cancellationToken, detectedRegistration);

        await using var dbTransaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var current = await db.ExitAuthorisations.Include(x => x.FuelTransaction).ThenInclude(x => x!.Payments)
            .FirstOrDefaultAsync(x => x.Id == candidate.Id, cancellationToken);
        rejection = ValidateAuthorization(current, request, hash);
        if (rejection is not null)
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            return await RecordRejectedScan(request, current, rejection.Value.Status, rejection.Value.Message, cancellationToken, detectedRegistration);
        }
        current!.ConsumedAtUtc = DateTimeOffset.UtcNow;
        var scan = new ExitScanRecord
        {
            StationId = request.StationId,
            StationExitId = request.StationExitId,
            ExitAuthorisationId = current.Id,
            TokenFingerprint = hash[..16],
            Status = ExitScanStatus.Accepted,
            ObservedRegistration = detectedRegistration,
            DeviceReference = request.DeviceReference
        };
        db.ExitScanRecords.Add(scan);
        db.AuditLogs.Add(new AuditLog { StationId = request.StationId, ActorUserId = userId, Action = "ExitAuthorization.Consumed", EntityType = nameof(ExitAuthorisation), EntityId = current.Id.ToString(), DetailsJson = "{}" });
        await db.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);

        var controllerResult = await accessController.RequestAuthorizationAsync(
            new ExitControllerRequest(request.StationId, request.StationExitId, current.Id, current.Id.ToString("N")), cancellationToken);
        var exitEvent = new ExitEvent
        {
            StationId = request.StationId,
            StationExitId = request.StationExitId,
            FuelTransactionId = current.FuelTransactionId,
            ExitAuthorisationId = current.Id,
            EventType = "ControllerAuthorizationRequest",
            ControllerStatus = controllerResult.Status,
            SensorReferences = $"{vehicle.AdapterName};{controllerResult.AdapterName}",
            Completed = false
        };
        db.ExitEvents.Add(exitEvent);
        if (!controllerResult.AcceptedByController || !controllerResult.SafetyStatusHealthy)
        {
            db.IncidentReports.Add(new IncidentReport { StationId = request.StationId, ReportedByUserId = userId, FuelTransactionId = current.FuelTransactionId, Category = "CONTROLLER_OR_SAFETY_FAULT", Description = "Controller rejected or reported an unhealthy safety status. No software retry or interlock override was attempted.", Severity = IncidentSeverity.High });
            await db.SaveChangesAsync(cancellationToken);
            return new ExitScanResponse(false, ExitScanStatus.ControllerUnavailable, "Controller did not accept the request. The authorization was consumed; supervisor assistance is required. No physical exit is confirmed.", exitEvent.Id);
        }
        await db.SaveChangesAsync(cancellationToken);
        var simulation = controllerResult.AdapterName.Contains("SIMULATION", StringComparison.OrdinalIgnoreCase);
        return new ExitScanResponse(true, ExitScanStatus.Accepted,
            simulation ? "SIMULATION ONLY: controller accepted an authorization request; no hardware actuation or physical exit is confirmed." : "Controller accepted the request; physical exit completion is not confirmed.", exitEvent.Id);
    }

    private (ExitScanStatus Status, string Message)? ValidateAuthorization(ExitAuthorisation? authorization, ExitScanRequest request, string tokenHash)
    {
        if (authorization is null) return (ExitScanStatus.Invalid, "Authorization was not found.");
        if (authorization.StationId != request.StationId) return (ExitScanStatus.StationMismatch, "Authorization is not valid for this station.");
        if (authorization.StationExitId != request.StationExitId) return (ExitScanStatus.ExitMismatch, "Authorization is not valid for this exit.");
        if (authorization.RevokedAtUtc is not null) return (ExitScanStatus.Revoked, "Authorization has been revoked.");
        if (authorization.ExpiresAtUtc <= DateTimeOffset.UtcNow) return (ExitScanStatus.Expired, "Authorization has expired.");
        if (authorization.ConsumedAtUtc is not null) return (ExitScanStatus.AlreadyUsed, "Authorization has already been used.");
        var transaction = authorization.FuelTransaction;
        if (transaction is null || transaction.Status != FuelTransactionStatus.Completed || transaction.PaymentStatus != PaymentStatus.Paid || transaction.FinalAmount is null || transaction.FinalLitres is null)
            return (ExitScanStatus.PaymentIneligible, "Transaction does not have verified eligible payment and final POS values.");
        if (transaction.Payments.All(x => x.Status != PaymentStatus.Paid || (!environment.IsDevelopment() && x.IsSimulation)))
            return (ExitScanStatus.PaymentIneligible, "No eligible POS payment evidence exists.");
        return null;
    }

    private async Task<ExitScanResponse> RecordRejectedScan(ExitScanRequest request, ExitAuthorisation? authorization, ExitScanStatus status, string message, CancellationToken cancellationToken, string? registration = null)
    {
        var fingerprint = string.IsNullOrWhiteSpace(request.Token) ? "" : HashToken(request.Token)[..16];
        db.ExitScanRecords.Add(new ExitScanRecord
        {
            StationId = request.StationId,
            StationExitId = request.StationExitId,
            ExitAuthorisationId = authorization?.Id,
            TokenFingerprint = fingerprint,
            Status = status,
            ObservedRegistration = registration,
            DeviceReference = request.DeviceReference
        });
        db.AuditLogs.Add(new AuditLog { StationId = request.StationId, ActorUserId = null, Action = "ExitScan.Rejected", EntityType = nameof(ExitScanRecord), DetailsJson = $"{{\"status\":\"{status}\"}}" });
        if (status is ExitScanStatus.SensorFault or ExitScanStatus.ControllerUnavailable or ExitScanStatus.VehicleMismatch)
            db.IncidentReports.Add(new IncidentReport { StationId = request.StationId, ReportedByUserId = "system", FuelTransactionId = authorization?.FuelTransactionId, Category = status.ToString(), Description = message, Severity = status == ExitScanStatus.VehicleMismatch ? IncidentSeverity.Medium : IncidentSeverity.High });
        await db.SaveChangesAsync(cancellationToken);
        return new ExitScanResponse(false, status, message, null);
    }

    public async Task<ReceiptDto> ReprintReceiptAsync(string userId, Guid stationId, Guid transactionId, CancellationToken cancellationToken)
    {
        await EnsureStationAccess(userId, stationId, cancellationToken);
        var authorization = await db.ExitAuthorisations.Include(x => x.FuelTransaction).ThenInclude(x => x!.Station)
            .Include(x => x.FuelTransaction).ThenInclude(x => x!.Pump)
            .FirstOrDefaultAsync(x => x.FuelTransactionId == transactionId && x.StationId == stationId && x.ConsumedAtUtc == null && x.RevokedAtUtc == null && x.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken)
            ?? throw new KeyNotFoundException("No active unexpired authorization exists for reprinting.");
        if (authorization.FuelTransaction?.PaymentStatus != PaymentStatus.Paid)
            throw new InvalidOperationException("Receipt cannot be reprinted without verified paid status.");
        var token = _tokenProtector.Unprotect(authorization.ProtectedToken);
        var transaction = authorization.FuelTransaction;
        var qr = CreateQrBase64(token);
        var receipt = new ReceiptDto("ROMVEX TECHNOLOGIES – FUELGUARD", transaction.Station?.Name ?? "", transaction.Reference,
            transaction.VehicleRegistrationSnapshot ?? "Not recorded", transaction.Pump?.Number ?? "", transaction.FuelType,
            transaction.FinalLitres ?? 0m, transaction.FinalAmount ?? 0m, transaction.PaymentStatus.ToString(), token, qr,
            DateTimeOffset.UtcNow, transaction.Payments.Any(x => x.IsSimulation), "Printer status pending.");
        var result = await printer.PrintAsync(receipt, cancellationToken);
        var updatedReceipt = receipt with { PrintMessage = result.Message ?? result.AdapterName };
        if (!result.Success)
        {
            db.IncidentReports.Add(new IncidentReport { StationId = stationId, ReportedByUserId = userId, FuelTransactionId = transactionId, Category = "PRINTER_FAILURE", Description = result.Message ?? "Printer failed.", Severity = IncidentSeverity.Medium });
            db.AuditLogs.Add(new AuditLog { StationId = stationId, ActorUserId = userId, Action = "Receipt.ReprintFailed", EntityType = nameof(FuelTransaction), EntityId = transactionId.ToString(), DetailsJson = "{}" });
        }
        else
        {
            db.AuditLogs.Add(new AuditLog { StationId = stationId, ActorUserId = userId, Action = "Receipt.Reprinted", EntityType = nameof(FuelTransaction), EntityId = transactionId.ToString(), DetailsJson = $"{{\"adapter\":\"{result.AdapterName}\"}}" });
        }
        await db.SaveChangesAsync(cancellationToken);
        return updatedReceipt;
    }

    public async Task<IncidentDto> ReportIncidentAsync(string userId, ReportIncidentRequest request, CancellationToken cancellationToken)
    {
        await EnsureStationAccess(userId, request.StationId, cancellationToken);
        if (request.FuelTransactionId is Guid transactionId && !await db.FuelTransactions.AnyAsync(x => x.Id == transactionId && x.StationId == request.StationId, cancellationToken))
            throw new KeyNotFoundException("Related transaction was not found at this station.");
        var incident = new IncidentReport { StationId = request.StationId, ReportedByUserId = userId, FuelTransactionId = request.FuelTransactionId, Category = request.Category.Trim(), Description = request.Description.Trim(), Severity = request.Severity };
        db.IncidentReports.Add(incident);
        db.AuditLogs.Add(new AuditLog { StationId = request.StationId, ActorUserId = userId, Action = "Incident.Reported", EntityType = nameof(IncidentReport), EntityId = incident.Id.ToString(), DetailsJson = "{}" });
        await db.SaveChangesAsync(cancellationToken);
        return new IncidentDto(incident.Id, incident.Category, incident.Description, incident.Severity, incident.Status, incident.CreatedAtUtc);
    }

    public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(string userId, Guid stationId, CancellationToken cancellationToken)
    {
        await EnsureStationAccess(userId, stationId, cancellationToken);
        var station = await db.Stations.AsNoTracking().SingleAsync(x => x.Id == stationId, cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(station.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        var localStart = new DateTimeOffset(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, localNow.Offset).ToUniversalTime();
        var nextDay = localStart.AddDays(1);
        var today = db.FuelTransactions.Where(x => x.StationId == stationId && x.CreatedAtUtc >= localStart && x.CreatedAtUtc < nextDay);
        var devices = await db.HardwareDevices.Where(x => x.StationId == stationId).ToListAsync(cancellationToken);
        var pos = await db.POSConfigurations.AsNoTracking().FirstOrDefaultAsync(x => x.StationId == stationId, cancellationToken);
        return new DashboardSummaryDto(
            await today.CountAsync(cancellationToken),
            await today.CountAsync(x => x.PaymentStatus == PaymentStatus.Paid, cancellationToken),
            await today.CountAsync(x => x.PaymentStatus == PaymentStatus.Pending, cancellationToken),
            await today.CountAsync(x => x.PaymentStatus == PaymentStatus.Failed, cancellationToken),
            await db.ExitAuthorisations.CountAsync(x => x.StationId == stationId && x.ConsumedAtUtc == null && x.RevokedAtUtc == null && x.ExpiresAtUtc > DateTimeOffset.UtcNow, cancellationToken),
            await db.ExitEvents.CountAsync(x => x.StationId == stationId && x.Completed, cancellationToken),
            await db.IncidentReports.CountAsync(x => x.StationId == stationId && x.Status != IncidentStatus.Closed, cancellationToken),
            devices.Count(x => x.Status == DeviceStatus.Online), devices.Count,
            pos?.Mode == PaymentProviderMode.Simulation);
    }

    public async Task<IReadOnlyList<DeviceHealthDto>> GetDevicesAsync(string userId, Guid stationId, CancellationToken cancellationToken)
    {
        await EnsureStationAccess(userId, stationId, cancellationToken);
        return await db.HardwareDevices.AsNoTracking().Where(x => x.StationId == stationId)
            .Select(x => new DeviceHealthDto(x.Id, x.DeviceCode, x.DeviceType, x.AdapterName, x.Status, x.LastSeenAtUtc, x.AdapterName.Contains("SIMULATION")))
            .ToListAsync(cancellationToken);
    }

    public async Task RecordHardwareEventAsync(HardwareEventDto eventDto, CancellationToken cancellationToken)
    {
        if (eventDto.IsSimulation && !environment.IsDevelopment())
            throw new InvalidOperationException("Simulated hardware events are prohibited outside Development.");
        if (!await db.Stations.AnyAsync(x => x.Id == eventDto.StationId, cancellationToken))
            throw new KeyNotFoundException("Station not found.");
        var device = await db.HardwareDevices.FirstOrDefaultAsync(x => x.StationId == eventDto.StationId && x.DeviceCode == eventDto.DeviceCode, cancellationToken);
        if (device is null)
        {
            device = new HardwareDevice { StationId = eventDto.StationId, DeviceCode = eventDto.DeviceCode, DeviceType = eventDto.Kind.ToString(), AdapterName = eventDto.IsSimulation ? "SIMULATION ONLY" : "SUPPLIER ADAPTER" };
            db.HardwareDevices.Add(device);
        }
        device.Status = eventDto.Status;
        device.LastSeenAtUtc = DateTimeOffset.UtcNow;
        db.HardwareStatusLogs.Add(new HardwareStatusLog { StationId = eventDto.StationId, HardwareDeviceId = device.Id, Kind = eventDto.Kind, Status = eventDto.Status, EventReference = eventDto.EventReference, SafeDetails = eventDto.SafeDetails });
        if (eventDto.Status is DeviceStatus.Fault or DeviceStatus.Offline)
            db.IncidentReports.Add(new IncidentReport { StationId = eventDto.StationId, ReportedByUserId = "gateway", Category = "HARDWARE_FAULT", Description = $"Device {eventDto.DeviceCode} reported {eventDto.Status}.", Severity = IncidentSeverity.High });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureStationAccess(string userId, Guid stationId, CancellationToken cancellationToken)
    {
        if (!await stationAccess.HasStationAccessAsync(userId, stationId, cancellationToken))
            throw new UnauthorizedAccessException("User is not assigned to this station.");
    }

    private static void ValidateRequest(CreateFuelTransactionRequest request)
    {
        var isValid = request.Type switch
        {
            FuelTransactionType.FixedAmount => request.RequestedAmount > 0 && request.RequestedLitres is null,
            FuelTransactionType.FixedLitres => request.RequestedLitres > 0 && request.RequestedAmount is null,
            FuelTransactionType.FillToFull => request.RequestedAmount is null && request.RequestedLitres is null,
            _ => false
        };
        if (!isValid) throw new ArgumentException("Supply only the requested value required by the selected transaction type.");
    }

    private static string NormalizeRegistration(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : new string(value.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string CreateQrBase64(string token)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(token, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        return Convert.ToBase64String(png.GetGraphic(8));
    }

    private static AuthorizationDto ToAuthorizationDto(ExitAuthorisation authorization, string token, bool isSimulation) =>
        new(authorization.Id, authorization.FuelTransactionId, authorization.StationId, authorization.StationExitId, token,
            CreateQrBase64(token), authorization.VehicleRegistrationSnapshot, authorization.ExpiresAtUtc, isSimulation);

    private static FuelTransactionDto ToDto(FuelTransaction transaction, string pumpNumber) =>
        new(transaction.Id, transaction.StationId, transaction.Reference, pumpNumber, transaction.VehicleRegistrationSnapshot, transaction.Type, transaction.Status, transaction.PaymentStatus, transaction.FuelType, transaction.RequestedAmount, transaction.RequestedLitres, transaction.FinalLitres, transaction.FinalAmount, transaction.PosTransactionReference, transaction.CreatedAtUtc, transaction.CompletedAtUtc);

    private static PaymentDto ToPaymentDto(PaymentRecord payment) =>
        new(payment.Id, payment.FuelTransactionId, payment.Provider, payment.ProviderTransactionReference, payment.Status, payment.Amount, payment.CurrencyCode, payment.IsSimulation, payment.VerifiedAtUtc);
}

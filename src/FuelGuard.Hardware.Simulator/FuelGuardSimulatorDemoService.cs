using System.Security.Cryptography;
using System.Text;
using FuelGuard.Contracts;
using FuelGuard.Domain;
using QRCoder;

namespace FuelGuard.Hardware.Simulator;

public sealed class SimulatorDemoAccount
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public sealed record SimulatorDemoUser(string Email, string DisplayName, string Role);

public enum SimulatorSafetyState
{
    Safe,
    VehiclePresent,
    ObstructionDetected,
    SensorFault,
    Unknown
}

public sealed record SimulatorAuditEntry(DateTimeOffset OccurredAtUtc, string Actor, string Action, string Entity, Guid? EntityId, string Summary);
public sealed record SimulatorExitWorkflowEvent(Guid Id, DateTimeOffset OccurredAtUtc, Guid? AuthorizationId, ExitScanStatus Status, bool Completed, string Message);
public sealed record SimulatorMovementResult(bool Accepted, SimulatorSafetyState SafetyState, string Message);

public sealed record SimulatorDashboardSnapshot(
    int TotalTransactions,
    int PaidTransactions,
    int FailedPayments,
    int PendingPayments,
    int ActiveExitAuthorizations,
    int CompletedSimulatedExits,
    IReadOnlyList<FuelTransactionDto> Transactions,
    IReadOnlyList<FuelTransactionDto> RecentTransactions,
    IReadOnlyList<SimulatorAuditEntry> RecentActivity,
    IReadOnlyList<SimulatorExitWorkflowEvent> ExitEvents,
    IReadOnlyList<DeviceHealthDto> Hardware,
    PosHealthDto Pos,
    SimulatorSafetyState SafetyState);

public interface IFuelGuardSimulatorDemoService
{
    SimulatorDemoUser? Authenticate(string email, string password);
    SimulatorDashboardSnapshot GetDashboard(SimulatorDemoUser user);
    FuelTransactionDto CreateTransaction(SimulatorDemoUser user, CreateFuelTransactionRequest request);
    PaymentDto SubmitPayment(SimulatorDemoUser user, Guid transactionId, PaymentStatus outcome, decimal finalAmount, decimal finalLitres);
    AuthorizationDto GenerateExitQr(SimulatorDemoUser user, Guid transactionId, Guid stationExitId, int expiryMinutes);
    ExitScanResponse ScanExit(SimulatorDemoUser user, ExitScanRequest request);
    void RevokeAuthorization(SimulatorDemoUser user, Guid authorizationId);
    void SetSafetyState(SimulatorDemoUser user, SimulatorSafetyState state);
    SimulatorMovementResult RequestMovement(SimulatorDemoUser user);
    void SetScannerOnline(SimulatorDemoUser user, bool online);
}

public sealed class FuelGuardSimulatorDemoService : IFuelGuardSimulatorDemoService
{
    public static readonly Guid DemoStationId = Guid.Parse("a2186af4-6a71-47be-9104-5c0ba1eb5021");
    public static readonly Guid DemoPumpOneId = Guid.Parse("ad36d151-473c-4c3b-8ec4-6a51ae4f5ef1");
    public static readonly Guid DemoPumpTwoId = Guid.Parse("4ba1e712-8a5a-4a64-a8e2-247e0f6a4b60");
    public static readonly Guid DemoExitOneId = Guid.Parse("31237340-68d9-4eba-b749-b061ae3dc4a1");
    public static readonly Guid DemoExitTwoId = Guid.Parse("4d71ac5a-274d-4c9d-b2f4-13ec9b63b90f");

    public static IReadOnlyList<(Guid Id, string Number)> Pumps { get; } =
    [
        (DemoPumpOneId, "01"),
        (DemoPumpTwoId, "02")
    ];

    public static IReadOnlyList<(Guid Id, string Name)> Exits { get; } =
    [
        (DemoExitOneId, "North exit"),
        (DemoExitTwoId, "South exit")
    ];

    private readonly object _gate = new();
    private readonly IReadOnlyList<SimulatorDemoAccount> _accounts;
    private readonly SimulatedPosIntegrationService _pos;
    private readonly SimulatedExitScannerService _scanner;
    private readonly TimeProvider _clock;
    private readonly bool _enabled;
    private readonly List<FuelTransaction> _transactions = [];
    private readonly List<PaymentRecord> _payments = [];
    private readonly Dictionary<Guid, ExitAuthorisation> _authorizations = [];
    private readonly Dictionary<string, ExitAuthorisation> _authorizationsByTokenHash = new(StringComparer.Ordinal);
    private readonly List<SimulatorAuditEntry> _audit = [];
    private readonly List<SimulatorExitWorkflowEvent> _exitEvents = [];
    private SimulatorSafetyState _safetyState = SimulatorSafetyState.Safe;

    public FuelGuardSimulatorDemoService(
        IReadOnlyList<SimulatorDemoAccount> accounts,
        SimulatedPosIntegrationService pos,
        SimulatedExitScannerService scanner,
        TimeProvider clock,
        bool enabled)
    {
        _accounts = accounts;
        _pos = pos;
        _scanner = scanner;
        _clock = clock;
        _enabled = enabled;
    }

    public SimulatorDemoUser? Authenticate(string email, string password)
    {
        EnsureEnabled();
        if (string.IsNullOrWhiteSpace(email) || password is null)
            return null;

        var account = _accounts.FirstOrDefault(x => string.Equals(x.Email, email.Trim(), StringComparison.OrdinalIgnoreCase));
        if (account is null || account.Password.Length == 0)
            return null;

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(account.Password));
        return CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash)
            ? new SimulatorDemoUser(account.Email, account.DisplayName, account.Role)
            : null;
    }

    public SimulatorDashboardSnapshot GetDashboard(SimulatorDemoUser user)
    {
        EnsureUser(user);
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            var transactions = _transactions
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(ToDto)
                .ToArray();
            var hardware = new[]
            {
                new DeviceHealthDto(Guid.Empty, "SIM-POS", "PointOfSale", "SIMULATION ONLY", DeviceStatus.Online, now, true),
                new DeviceHealthDto(Guid.Empty, "SIM-QR-SCANNER", "QRScanner", "SIMULATION ONLY", _scanner.IsOnline ? DeviceStatus.Online : DeviceStatus.Offline, _scanner.IsOnline ? now : null, true),
                new DeviceHealthDto(Guid.Empty, "SIM-SAFETY-CONTROLLER", "SafetyController", "SIMULATION ONLY", GetSafetyDeviceStatus(), now, true)
            };
            var posHealth = _pos.CheckHealthAsync(DemoStationId, CancellationToken.None).GetAwaiter().GetResult();
            return new SimulatorDashboardSnapshot(
                _transactions.Count,
                _transactions.Count(x => x.PaymentStatus == PaymentStatus.Paid),
                _transactions.Count(x => x.PaymentStatus == PaymentStatus.Failed),
                _transactions.Count(x => x.PaymentStatus is PaymentStatus.Pending or PaymentStatus.Unknown),
                _authorizations.Values.Count(x => IsActive(x, now)),
                _exitEvents.Count(x => x.Completed),
                transactions,
                transactions.Take(8).ToArray(),
                _audit.OrderByDescending(x => x.OccurredAtUtc).Take(20).ToArray(),
                _exitEvents.OrderByDescending(x => x.OccurredAtUtc).Take(20).ToArray(),
                hardware,
                posHealth,
                _safetyState);
        }
    }

    public FuelTransactionDto CreateTransaction(SimulatorDemoUser user, CreateFuelTransactionRequest request)
    {
        EnsureUser(user);
        ArgumentNullException.ThrowIfNull(request);
        ValidateTransactionRequest(request);
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            var pumpNumber = Pumps.Single(x => x.Id == request.PumpId).Number;
            var registration = NormalizeRegistration(request.VehicleRegistration);
            var transaction = new FuelTransaction
            {
                StationId = DemoStationId,
                PumpId = request.PumpId,
                AttendantUserId = user.Email,
                VehicleRegistrationSnapshot = registration,
                Reference = $"FG-DEMO-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
                PosTransactionReference = $"SIM-{Guid.NewGuid():N}",
                Type = request.Type,
                FuelType = request.FuelType.Trim(),
                RequestedAmount = request.RequestedAmount,
                RequestedLitres = request.RequestedLitres,
                Status = FuelTransactionStatus.Pending,
                PaymentStatus = PaymentStatus.Pending,
                CreatedAtUtc = now
            };
            _transactions.Add(transaction);
            RecordAudit(user, "FuelTransaction.Created", nameof(FuelTransaction), transaction.Id, $"{transaction.Reference}; {transaction.Type}; pump {pumpNumber}.");
            return ToDto(transaction, pumpNumber);
        }
    }

    public PaymentDto SubmitPayment(SimulatorDemoUser user, Guid transactionId, PaymentStatus outcome, decimal finalAmount, decimal finalLitres)
    {
        EnsureUser(user);
        if (outcome is not (PaymentStatus.Paid or PaymentStatus.Failed or PaymentStatus.Pending or PaymentStatus.Unknown))
            throw new ArgumentOutOfRangeException(nameof(outcome), "The POS simulator supports Paid, Failed, Pending, and Unknown outcomes.");
        if (finalAmount is <= 0 or > 1_000_000m || finalLitres is <= 0 or > 10_000m)
            throw new ArgumentOutOfRangeException(nameof(finalAmount), "The simulated POS requires positive final amount and litres within the demo limits.");

        lock (_gate)
        {
            var transaction = FindTransaction(transactionId);
            if (transaction.PaymentStatus == PaymentStatus.Paid)
                throw new InvalidOperationException("A confirmed paid transaction cannot be resubmitted.");
            if (transaction.Status == FuelTransactionStatus.Cancelled)
                throw new InvalidOperationException("Cancelled transactions cannot be paid.");
            if (transaction.Type == FuelTransactionType.FixedAmount && finalAmount > transaction.RequestedAmount)
                throw new InvalidOperationException("The POS final amount cannot exceed the requested fixed amount.");
            if (transaction.Type == FuelTransactionType.FixedLitres && finalLitres > transaction.RequestedLitres)
                throw new InvalidOperationException("The POS final litres cannot exceed the requested fixed litres.");

            var confirmation = _pos.SimulatePayment(transaction.PosTransactionReference!, outcome, finalAmount, finalLitres);
            var now = _clock.GetUtcNow();
            var payment = new PaymentRecord
            {
                FuelTransactionId = transaction.Id,
                Provider = confirmation.Provider,
                ProviderTransactionReference = confirmation.TransactionReference,
                ProviderEventId = confirmation.EventId,
                Status = confirmation.Status,
                Amount = confirmation.Amount,
                CurrencyCode = confirmation.CurrencyCode,
                IsSimulation = true,
                VerifiedAtUtc = now,
                CreatedAtUtc = now
            };
            _payments.Add(payment);
            transaction.PaymentStatus = confirmation.Status;
            if (confirmation.Status is PaymentStatus.Paid or PaymentStatus.Failed)
            {
                transaction.FinalAmount = confirmation.Amount;
                transaction.FinalLitres = confirmation.FinalLitres;
                transaction.Status = FuelTransactionStatus.Completed;
                transaction.CompletedAtUtc = now;
            }
            transaction.UpdatedAtUtc = now;
            RecordAudit(user, "Payment.Simulated", nameof(FuelTransaction), transaction.Id, $"SIMULATION ONLY; POS outcome {confirmation.Status}; event {confirmation.EventId}.");
            return ToPaymentDto(payment);
        }
    }

    public AuthorizationDto GenerateExitQr(SimulatorDemoUser user, Guid transactionId, Guid stationExitId, int expiryMinutes)
    {
        EnsureUser(user);
        if (expiryMinutes is < 1 or > 120)
            throw new ArgumentOutOfRangeException(nameof(expiryMinutes), "Exit QR expiry must be between 1 and 120 minutes.");
        if (!Exits.Any(x => x.Id == stationExitId))
            throw new ArgumentException("Select a configured simulator exit.", nameof(stationExitId));

        lock (_gate)
        {
            var transaction = FindTransaction(transactionId);
            if (transaction.Status != FuelTransactionStatus.Completed ||
                transaction.PaymentStatus != PaymentStatus.Paid ||
                transaction.FinalAmount is null ||
                transaction.FinalLitres is null ||
                !_payments.Any(x => x.FuelTransactionId == transactionId && x.Status == PaymentStatus.Paid && x.IsSimulation))
                throw new InvalidOperationException("An exit QR can only be generated after the POS simulator confirms payment.");

            var now = _clock.GetUtcNow();
            if (_authorizations.Values.Any(x => x.FuelTransactionId == transactionId && IsActive(x, now)))
                throw new InvalidOperationException("An active exit QR already exists for this transaction.");

            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            var authorization = new ExitAuthorisation
            {
                FuelTransactionId = transactionId,
                StationId = DemoStationId,
                StationExitId = stationExitId,
                TokenHash = HashToken(token),
                VehicleRegistrationSnapshot = transaction.VehicleRegistrationSnapshot ?? string.Empty,
                ExpiresAtUtc = now.AddMinutes(expiryMinutes),
                CreatedAtUtc = now
            };
            _authorizations.Add(authorization.Id, authorization);
            _authorizationsByTokenHash.Add(authorization.TokenHash, authorization);
            RecordAudit(user, "ExitAuthorization.Issued", nameof(ExitAuthorisation), authorization.Id, $"SIMULATION ONLY; expires {authorization.ExpiresAtUtc:O}.");
            return new AuthorizationDto(
                authorization.Id,
                transactionId,
                DemoStationId,
                stationExitId,
                token,
                CreateQrBase64(token),
                authorization.VehicleRegistrationSnapshot,
                authorization.ExpiresAtUtc,
                true);
        }
    }

    public ExitScanResponse ScanExit(SimulatorDemoUser user, ExitScanRequest request)
    {
        EnsureUser(user);
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var eventId = Guid.NewGuid();
            if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 200)
                return RecordScan(user, eventId, null, ExitScanStatus.Invalid, "Invalid QR token.");
            var scannedToken = _scanner.SimulateScan(request.Token ?? string.Empty);
            if (scannedToken is null)
                return RecordScan(user, eventId, null, ExitScanStatus.ControllerUnavailable, "SIMULATION ONLY: QR scanner is offline; no authorization was consumed.");
            if (request.StationId != DemoStationId)
                return RecordScan(user, eventId, null, ExitScanStatus.StationMismatch, "QR is not valid for this simulator station.");
            if (!Exits.Any(x => x.Id == request.StationExitId))
                return RecordScan(user, eventId, null, ExitScanStatus.ExitMismatch, "QR is not valid for this simulator exit.");
            _authorizationsByTokenHash.TryGetValue(HashToken(scannedToken), out var authorization);
            if (authorization is null)
                return RecordScan(user, eventId, null, ExitScanStatus.Invalid, "QR authorization was not found.");
            if (authorization.StationId != request.StationId)
                return RecordScan(user, eventId, authorization, ExitScanStatus.StationMismatch, "QR is not valid for this simulator station.");
            if (authorization.StationExitId != request.StationExitId)
                return RecordScan(user, eventId, authorization, ExitScanStatus.ExitMismatch, "QR is not valid for this simulator exit.");
            if (authorization.RevokedAtUtc is not null)
                return RecordScan(user, eventId, authorization, ExitScanStatus.Revoked, "QR authorization has been revoked.");
            if (authorization.ExpiresAtUtc <= _clock.GetUtcNow())
                return RecordScan(user, eventId, authorization, ExitScanStatus.Expired, "QR authorization has expired.");
            if (authorization.ConsumedAtUtc is not null)
                return RecordScan(user, eventId, authorization, ExitScanStatus.AlreadyUsed, "QR authorization has already been consumed.");

            var transaction = FindTransaction(authorization.FuelTransactionId);
            if (transaction.Status != FuelTransactionStatus.Completed ||
                transaction.PaymentStatus != PaymentStatus.Paid ||
                !_payments.Any(x => x.FuelTransactionId == transaction.Id && x.Status == PaymentStatus.Paid && x.IsSimulation))
                return RecordScan(user, eventId, authorization, ExitScanStatus.PaymentIneligible, "No confirmed simulated payment is recorded.");
            if (_safetyState != SimulatorSafetyState.Safe)
                return RecordScan(user, eventId, authorization, ExitScanStatus.SensorFault, $"Safety interlock rejected the scan while state is {_safetyState}; no movement request was made.");

            var now = _clock.GetUtcNow();
            authorization.ConsumedAtUtc = now;
            var message = "SIMULATION ONLY: QR accepted and simulated exit workflow completed; no physical gate or tyre-spike actuation occurred.";
            _exitEvents.Add(new SimulatorExitWorkflowEvent(eventId, now, authorization.Id, ExitScanStatus.Accepted, true, message));
            RecordAudit(user, "ExitScan.Accepted", nameof(ExitAuthorisation), authorization.Id, message);
            return new ExitScanResponse(true, ExitScanStatus.Accepted, message, eventId);
        }
    }

    public void RevokeAuthorization(SimulatorDemoUser user, Guid authorizationId)
    {
        EnsureUser(user);
        lock (_gate)
        {
            if (!_authorizations.TryGetValue(authorizationId, out var authorization))
                throw new KeyNotFoundException("Exit authorization was not found.");
            if (authorization.ConsumedAtUtc is not null)
                throw new InvalidOperationException("A consumed exit authorization cannot be revoked.");
            if (authorization.RevokedAtUtc is not null)
                return;
            authorization.RevokedAtUtc = _clock.GetUtcNow();
            RecordAudit(user, "ExitAuthorization.Revoked", nameof(ExitAuthorisation), authorization.Id, "SIMULATION ONLY; authorization revoked.");
        }
    }

    public void SetSafetyState(SimulatorDemoUser user, SimulatorSafetyState state)
    {
        EnsureUser(user);
        if (!Enum.IsDefined(state))
            throw new ArgumentOutOfRangeException(nameof(state));
        lock (_gate)
        {
            _safetyState = state;
            RecordAudit(user, "SafetyController.StateChanged", "SafetyController", null, $"SIMULATION ONLY; state is {state}.");
        }
    }

    public SimulatorMovementResult RequestMovement(SimulatorDemoUser user)
    {
        EnsureUser(user);
        lock (_gate)
        {
            var accepted = _safetyState == SimulatorSafetyState.Safe;
            var message = accepted
                ? "SIMULATION ONLY: safety state is Safe; request accepted for demonstration only. No physical actuator is connected."
                : $"SIMULATION ONLY: request rejected because safety state is {_safetyState}. No movement or actuator request was sent.";
            RecordAudit(user, "SafetyController.MovementRequest", "SafetyController", null, message);
            return new SimulatorMovementResult(accepted, _safetyState, message);
        }
    }

    public void SetScannerOnline(SimulatorDemoUser user, bool online)
    {
        EnsureUser(user);
        lock (_gate)
        {
            _scanner.IsOnline = online;
            RecordAudit(user, "Scanner.StatusChanged", "QRScanner", null, $"SIMULATION ONLY; scanner is {(online ? "Online" : "Offline")}.");
        }
    }

    private ExitScanResponse RecordScan(SimulatorDemoUser user, Guid eventId, ExitAuthorisation? authorization, ExitScanStatus status, string message)
    {
        var now = _clock.GetUtcNow();
        _exitEvents.Add(new SimulatorExitWorkflowEvent(eventId, now, authorization?.Id, status, false, message));
        RecordAudit(user, "ExitScan.Rejected", nameof(ExitScanRecord), eventId, $"{status}; {message}");
        return new ExitScanResponse(false, status, message, eventId);
    }

    private void ValidateTransactionRequest(CreateFuelTransactionRequest request)
    {
        if (request.StationId != DemoStationId)
            throw new ArgumentException("Select the configured simulator station.", nameof(request));
        if (!Pumps.Any(x => x.Id == request.PumpId))
            throw new ArgumentException("Select a configured simulator pump.", nameof(request));
        if (!Enum.IsDefined(request.Type))
            throw new ArgumentOutOfRangeException(nameof(request.Type));
        if (string.IsNullOrWhiteSpace(request.FuelType) || request.FuelType.Length > 40)
            throw new ArgumentException("Select a valid fuel type.", nameof(request));
        if (request.VehicleRegistration?.Length > 16)
            throw new ArgumentException("Vehicle registration must not exceed 16 characters.", nameof(request));

        var valid = request.Type switch
        {
            FuelTransactionType.FixedAmount => request.RequestedAmount is > 0 and <= 1_000_000m && request.RequestedLitres is null,
            FuelTransactionType.FixedLitres => request.RequestedLitres is > 0 and <= 10_000m && request.RequestedAmount is null,
            FuelTransactionType.FillToFull => request.RequestedAmount is null && request.RequestedLitres is null,
            _ => false
        };
        if (!valid)
            throw new ArgumentException("Provide only the requested value required by the selected transaction type.", nameof(request));
    }

    private FuelTransaction FindTransaction(Guid transactionId) =>
        _transactions.SingleOrDefault(x => x.Id == transactionId)
        ?? throw new KeyNotFoundException("Fuel transaction was not found.");

    private void EnsureUser(SimulatorDemoUser user)
    {
        EnsureEnabled();
        ArgumentNullException.ThrowIfNull(user);
        if (!_accounts.Any(x => string.Equals(x.Email, user.Email, StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("A valid development demo login is required.");
    }

    private void EnsureEnabled()
    {
        if (!_enabled)
            throw new InvalidOperationException("FuelGuard simulator operations are available only in the Development environment.");
    }

    private DeviceStatus GetSafetyDeviceStatus() => _safetyState switch
    {
        SimulatorSafetyState.Safe or SimulatorSafetyState.VehiclePresent => DeviceStatus.Online,
        SimulatorSafetyState.Unknown => DeviceStatus.Unknown,
        _ => DeviceStatus.Fault
    };

    private bool IsActive(ExitAuthorisation authorization, DateTimeOffset now) =>
        authorization.ConsumedAtUtc is null &&
        authorization.RevokedAtUtc is null &&
        authorization.ExpiresAtUtc > now;

    private void RecordAudit(SimulatorDemoUser user, string action, string entity, Guid? entityId, string summary) =>
        _audit.Add(new SimulatorAuditEntry(_clock.GetUtcNow(), user.Email, action, entity, entityId, summary));

    private static FuelTransactionDto ToDto(FuelTransaction transaction) =>
        ToDto(transaction, Pumps.Single(x => x.Id == transaction.PumpId).Number);

    private static FuelTransactionDto ToDto(FuelTransaction transaction, string pumpNumber) =>
        new(
            transaction.Id,
            transaction.StationId,
            transaction.Reference,
            pumpNumber,
            transaction.VehicleRegistrationSnapshot,
            transaction.Type,
            transaction.Status,
            transaction.PaymentStatus,
            transaction.FuelType,
            transaction.RequestedAmount,
            transaction.RequestedLitres,
            transaction.FinalLitres,
            transaction.FinalAmount,
            transaction.PosTransactionReference,
            transaction.CreatedAtUtc,
            transaction.CompletedAtUtc);

    private static PaymentDto ToPaymentDto(PaymentRecord payment) =>
        new(
            payment.Id,
            payment.FuelTransactionId,
            payment.Provider,
            payment.ProviderTransactionReference,
            payment.Status,
            payment.Amount,
            payment.CurrencyCode,
            payment.IsSimulation,
            payment.VerifiedAtUtc);

    private static string NormalizeRegistration(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string CreateQrBase64(string token)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(token, QRCodeGenerator.ECCLevel.Q);
        return Convert.ToBase64String(new PngByteQRCode(data).GetGraphic(8));
    }
}

using System.ComponentModel.DataAnnotations;
using FuelGuard.Domain;

namespace FuelGuard.Contracts;

public sealed record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);
public sealed record LoginResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAtUtc);
public sealed record RefreshRequest([Required] string RefreshToken);

public sealed class CreateFuelTransactionRequest
{
    [Required] public Guid StationId { get; init; }
    [Required] public Guid PumpId { get; init; }
    [Required, StringLength(40)] public string FuelType { get; init; } = string.Empty;
    [Required] public FuelTransactionType Type { get; init; }
    [Range(typeof(decimal), "0.01", "1000000")] public decimal? RequestedAmount { get; init; }
    [Range(typeof(decimal), "0.01", "10000")] public decimal? RequestedLitres { get; init; }
    [StringLength(16)] public string? VehicleRegistration { get; init; }
}

public sealed record FuelTransactionDto(
    Guid Id,
    Guid StationId,
    string Reference,
    string PumpNumber,
    string? VehicleRegistration,
    FuelTransactionType Type,
    FuelTransactionStatus Status,
    PaymentStatus PaymentStatus,
    string FuelType,
    decimal? RequestedAmount,
    decimal? RequestedLitres,
    decimal? FinalLitres,
    decimal? FinalAmount,
    string? PosTransactionReference,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record SimulatePaymentRequest([Required] bool Succeed, [Range(typeof(decimal), "0.01", "1000000")] decimal FinalAmount, [Range(typeof(decimal), "0.001", "10000")] decimal FinalLitres);
public sealed record PaymentDto(Guid Id, Guid FuelTransactionId, string Provider, string ProviderTransactionReference, PaymentStatus Status, decimal Amount, string CurrencyCode, bool IsSimulation, DateTimeOffset? VerifiedAtUtc);
public sealed record IssueAuthorizationRequest([Required] Guid StationExitId, [Range(1, 120)] int ExpiryMinutes = 10);
public sealed record AuthorizationDto(Guid Id, Guid FuelTransactionId, Guid StationId, Guid StationExitId, string Token, string QrCodePngBase64, string VehicleRegistration, DateTimeOffset ExpiresAtUtc, bool IsSimulation);
public sealed record ExitScanRequest([Required] Guid StationId, [Required] Guid StationExitId, [Required] string Token, string? ObservedRegistration, string? DeviceReference);
public sealed record ExitScanResponse(bool Accepted, ExitScanStatus Status, string Message, Guid? ExitEventId);
public sealed record ReportIncidentRequest([Required] Guid StationId, [Required, StringLength(50)] string Category, [Required, StringLength(2000)] string Description, IncidentSeverity Severity, Guid? FuelTransactionId);
public sealed record IncidentDto(Guid Id, string Category, string Description, IncidentSeverity Severity, IncidentStatus Status, DateTimeOffset CreatedAtUtc);
public sealed record DeviceHealthDto(Guid Id, string Code, string Type, string AdapterName, DeviceStatus Status, DateTimeOffset? LastSeenAtUtc, bool IsSimulation);
public sealed record DashboardSummaryDto(int TransactionsToday, int PaidTransactions, int PendingPayments, int FailedPayments, int ActiveAuthorizations, int CompletedExits, int OpenIncidents, int OnlineDevices, int TotalDevices, bool PosSimulationMode);
public sealed record ReceiptDto(string Branding, string StationName, string TransactionReference, string VehicleRegistration, string PumpNumber, string FuelType, decimal Litres, decimal Amount, string PaymentStatus, string QrToken, string QrCodePngBase64, DateTimeOffset IssuedAtUtc, bool IsSimulation, string PrintMessage);
public sealed record PosPaymentConfirmation(string Provider, string TransactionReference, string EventId, PaymentStatus Status, decimal Amount, decimal FinalLitres, string CurrencyCode, bool IsSimulation);
public sealed record PosHealthDto(string Provider, bool IsAvailable, bool IsSimulation, string Message);
public sealed record HardwareEventDto(Guid StationId, string DeviceCode, HardwareEventKind Kind, DeviceStatus Status, string EventReference, string? SafeDetails, DateTimeOffset OccurredAtUtc, bool IsSimulation);

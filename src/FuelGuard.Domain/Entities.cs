namespace FuelGuard.Domain;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}

public sealed class Station : Entity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "Africa/Johannesburg";
    public string CurrencyCode { get; set; } = "ZAR";
    public decimal VatRate { get; set; } = 0.15m;
    public bool IsActive { get; set; } = true;
    public ICollection<StationExit> Exits { get; set; } = new List<StationExit>();
    public ICollection<Pump> Pumps { get; set; } = new List<Pump>();
}

public sealed class StationExit : Entity
{
    public Guid StationId { get; set; }
    public Station? Station { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}

public sealed class UserStationAssignment : Entity
{
    public string UserId { get; set; } = string.Empty;
    public Guid StationId { get; set; }
    public Station? Station { get; set; }
    public string AssignedByUserId { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class Attendant : Entity
{
    public Guid StationId { get; set; }
    public Station? Station { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string EmployeeNumber { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class Vehicle : Entity
{
    public Guid StationId { get; set; }
    public Station? Station { get; set; }
    public string RegistrationNormalized { get; set; } = string.Empty;
    public string? RegistrationDisplay { get; set; }
}

public sealed class Pump : Entity
{
    public Guid StationId { get; set; }
    public Station? Station { get; set; }
    public string Number { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class FuelTransaction : Entity
{
    public Guid StationId { get; set; }
    public Station? Station { get; set; }
    public Guid PumpId { get; set; }
    public Pump? Pump { get; set; }
    public string AttendantUserId { get; set; } = string.Empty;
    public Guid? VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public string? VehicleRegistrationSnapshot { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string? PosTransactionReference { get; set; }
    public FuelTransactionType Type { get; set; }
    public FuelTransactionStatus Status { get; set; } = FuelTransactionStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public string FuelType { get; set; } = string.Empty;
    public decimal? RequestedAmount { get; set; }
    public decimal? RequestedLitres { get; set; }
    public decimal? FinalLitres { get; set; }
    public decimal? FinalAmount { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public ICollection<FuelTransactionItem> Items { get; set; } = new List<FuelTransactionItem>();
    public ICollection<PaymentRecord> Payments { get; set; } = new List<PaymentRecord>();
}

public sealed class FuelTransactionItem : Entity
{
    public Guid FuelTransactionId { get; set; }
    public FuelTransaction? FuelTransaction { get; set; }
    public string FuelType { get; set; } = string.Empty;
    public decimal Litres { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
}

public sealed class PaymentRecord : Entity
{
    public Guid FuelTransactionId { get; set; }
    public FuelTransaction? FuelTransaction { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderTransactionReference { get; set; } = string.Empty;
    public string? ProviderEventId { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "ZAR";
    public bool IsSimulation { get; set; }
    public DateTimeOffset? VerifiedAtUtc { get; set; }
}

public sealed class PaymentVerificationLog : Entity
{
    public Guid FuelTransactionId { get; set; }
    public Guid? PaymentRecordId { get; set; }
    public string ProviderEventId { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string? SafeMessage { get; set; }
}

public sealed class ExitAuthorisation : Entity
{
    public Guid FuelTransactionId { get; set; }
    public FuelTransaction? FuelTransaction { get; set; }
    public Guid StationId { get; set; }
    public Guid StationExitId { get; set; }
    public StationExit? StationExit { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string ProtectedToken { get; set; } = string.Empty;
    public string VehicleRegistrationSnapshot { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}

public sealed class ExitScanRecord : Entity
{
    public Guid StationId { get; set; }
    public Guid StationExitId { get; set; }
    public Guid? ExitAuthorisationId { get; set; }
    public string TokenFingerprint { get; set; } = string.Empty;
    public ExitScanStatus Status { get; set; }
    public string? ObservedRegistration { get; set; }
    public string? DeviceReference { get; set; }
}

public sealed class ExitEvent : Entity
{
    public Guid StationId { get; set; }
    public Guid StationExitId { get; set; }
    public Guid? FuelTransactionId { get; set; }
    public Guid? ExitAuthorisationId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? ControllerStatus { get; set; }
    public string? SensorReferences { get; set; }
    public bool Completed { get; set; }
}

public sealed class HardwareDevice : Entity
{
    public Guid StationId { get; set; }
    public string DeviceCode { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string AdapterName { get; set; } = "SIMULATION";
    public DeviceStatus Status { get; set; } = DeviceStatus.Unknown;
    public DateTimeOffset? LastSeenAtUtc { get; set; }
}

public sealed class HardwareStatusLog : Entity
{
    public Guid StationId { get; set; }
    public Guid HardwareDeviceId { get; set; }
    public HardwareEventKind Kind { get; set; }
    public DeviceStatus Status { get; set; }
    public string? EventReference { get; set; }
    public string? SafeDetails { get; set; }
}

public sealed class POSConfiguration : Entity
{
    public Guid StationId { get; set; }
    public string ProviderName { get; set; } = "Disabled";
    public PaymentProviderMode Mode { get; set; } = PaymentProviderMode.Disabled;
    public bool IsEnabled { get; set; }
}

public sealed class POSIntegrationLog : Entity
{
    public Guid StationId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string? ExternalReference { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? SafeMessage { get; set; }
}

public sealed class PrinterConfiguration : Entity
{
    public Guid StationId { get; set; }
    public string PrinterName { get; set; } = string.Empty;
    public string AdapterName { get; set; } = "SIMULATION";
    public bool IsEnabled { get; set; }
}

public sealed class ANPRRecord : Entity
{
    public Guid StationId { get; set; }
    public Guid? FuelTransactionId { get; set; }
    public Guid? StationExitId { get; set; }
    public string RegistrationNormalized { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public bool RequiresManualReview { get; set; }
    public string AdapterName { get; set; } = "SIMULATION";
}

public sealed class IncidentReport : Entity
{
    public Guid StationId { get; set; }
    public string ReportedByUserId { get; set; } = string.Empty;
    public Guid? FuelTransactionId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IncidentSeverity Severity { get; set; }
    public IncidentStatus Status { get; set; } = IncidentStatus.Open;
    public string? Resolution { get; set; }
}

public sealed class SupervisorOverride : Entity
{
    public Guid StationId { get; set; }
    public string SupervisorUserId { get; set; } = string.Empty;
    public Guid? FuelTransactionId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
}

public sealed class AuditLog : Entity
{
    public Guid StationId { get; set; }
    public string? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? CorrelationId { get; set; }
    public string? DetailsJson { get; set; }
}

public sealed class SystemSetting : Entity
{
    public Guid StationId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsSecretReference { get; set; }
}

public sealed class Notification : Entity
{
    public Guid StationId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset? ReadAtUtc { get; set; }
}

public sealed class MaintenanceRecord : Entity
{
    public Guid StationId { get; set; }
    public Guid? HardwareDeviceId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed class RefreshToken : Entity
{
    public string UserId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}

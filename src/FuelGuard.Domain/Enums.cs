namespace FuelGuard.Domain;

public enum FuelTransactionType { FixedAmount, FixedLitres, FillToFull }
public enum FuelTransactionStatus { Pending, Completed, Cancelled }
public enum PaymentStatus { Pending, Paid, Failed, Cancelled, Refunded, Reversed }
public enum ExitAuthorizationStatus { Active, Consumed, Expired, Revoked }
public enum ExitScanStatus { Accepted, Invalid, Expired, AlreadyUsed, PaymentIneligible, VehicleMismatch, StationMismatch, ExitMismatch, Revoked, SensorFault, ControllerUnavailable }
public enum DeviceStatus { Unknown, Online, Offline, Fault, Maintenance }
public enum IncidentSeverity { Low, Medium, High, Critical }
public enum IncidentStatus { Open, UnderReview, Resolved, Closed }
public enum HardwareEventKind { Health, Fault, VehiclePresence, EmergencyStop, ControllerResponse, ScannerEvent, PrinterEvent, CameraEvent }
public enum PaymentProviderMode { Disabled, Simulation, Supplier }

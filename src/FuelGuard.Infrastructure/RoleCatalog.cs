namespace FuelGuard.Infrastructure;

public static class RoleCatalog
{
    public const string SuperAdministrator = "Super Administrator";
    public const string RomvexSupportAdministrator = "ROMVEX Support Administrator";
    public const string StationOwner = "Petrol Station Owner";
    public const string StationManager = "Station Manager";
    public const string Supervisor = "Supervisor";
    public const string FuelAttendant = "Fuel Attendant";
    public const string SecurityOperator = "Security Operator";
    public const string ReadOnlyAuditor = "Read-Only Auditor";

    public static readonly string[] All =
    [
        SuperAdministrator, RomvexSupportAdministrator, StationOwner, StationManager,
        Supervisor, FuelAttendant, SecurityOperator, ReadOnlyAuditor
    ];
}

# ROMVEX TECHNOLOGIES – FuelGuard Project Plan

**Status:** Implementation baseline; supplier-dependent production integration is explicitly pending.  
**Workspace inspection:** `C:\Users\ASUS\source\repos` contained an unrelated `NuvoraMarket` project and prior archives. It has not been modified. FuelGuard is being created as a separate sibling directory. A timestamped pre-implementation archive is `FuelGuard-preimplementation-workspace-backup-20261106-104005.zip`.  
**Environment:** Visual Studio 2026 18.10; .NET SDK 10.0.401 (runtime 10.0.12); SQL Server 2025 Developer instance `localhost` is running. `ROMVEX_FuelGuard_DB` did not exist during read-only inspection. SQLCMD requires `-C` in this local development environment because the local SQL Server certificate is not trusted. Do not copy that setting into production.

## Goals and boundaries

Create a multi-station-ready ASP.NET Core and Blazor solution with persistent SQL Server data, station-scoped authorization, a POS simulator, secure payment-derived exit authorizations, receipt/scanner/ANPR/controller abstractions, a local gateway simulator, tests, and operational documentation. The first release is a controlled supplier-integration baseline, not a claim of production readiness. No physical actuator control is implemented. No real POS, printer, camera, or controller integration is claimed without supplier documentation and approved hardware.

## Modules and solution structure

- `FuelGuard.Domain`: station, user/station assignment, pump/exit, vehicle, transaction, payment, authorization, event, incident, device, and audit concepts and invariants.
- `FuelGuard.Application`: validation, transaction/payment/authorization workflows, station access policies, service interfaces, and integration orchestration.
- `FuelGuard.Contracts`: versioned API DTOs and shared client contracts.
- `FuelGuard.Infrastructure`: EF Core SQL Server persistence, Identity stores, migrations, audit implementation, POS simulator, and configuration-backed integration services.
- `FuelGuard.Api`: versioned REST API, authentication, rate limiting, OpenAPI, health endpoints, and structured logging.
- `FuelGuard.AdminWeb`: Blazor Web App for station administration, transaction/payment/exit views, device status, incidents, and audit records.
- `FuelGuard.LocalGateway`: .NET Worker Service for authenticated device health/event delivery and safe local queueing.
- `FuelGuard.Hardware.Abstractions`: printer/scanner/ANPR/vehicle detection/access controller/health interfaces; no actuator commands.
- `FuelGuard.Hardware.Simulator`: development-only, prominently identified simulators for POS, printer, scanner, ANPR, vehicle presence, controller status, and faults.
- `FuelGuard.UnitTests` and `FuelGuard.IntegrationTests`: business invariants, API authorization, persistence, duplicate delivery, fault and recovery scenarios.
- `docs`, `scripts`, and `deployment`: integration contracts, safety/security requirements, setup, backups, operations and handover.

## Architecture and data flow

Browser and gateway clients communicate with the versioned API over HTTPS. Application services enforce business rules and station scope. EF Core persists to SQL Server. POS integrations implement a documented interface; only authenticated supplier evidence can establish a real payment. The development adapter is explicitly SIMULATION and cannot be selected as a production provider. An exit token is a cryptographically random opaque value; persist only its hash, bind it to a station, exit and completed paid transaction, set an expiry, and consume it atomically once. Gateway requests are advisory requests to an independently safety-rated controller; controller faults, unknown vehicle presence, or lost safety communications must never be overridden.

Database timestamps are UTC. Display/report conversion uses `Africa/Johannesburg` (UTC+02:00). Currency is ZAR; money uses `decimal`, SQL `decimal(18,2)`, and VAT is configurable (initial planning rate 15%). Do not store card numbers/CVV. No automatic production data seeding.

## Database entities and relationships

Implement the required entities: Stations, StationExits, Users (ASP.NET Core Identity), Roles (Identity roles), UserStationAssignments, Attendants, Vehicles, FuelTransactions, FuelTransactionItems, Pumps, PaymentRecords, PaymentVerificationLogs, ExitAuthorisations, ExitScanRecords, ExitEvents, HardwareDevices, HardwareStatusLogs, POSConfigurations, POSIntegrationLogs, PrinterConfigurations, ANPRRecords, IncidentReports, SupervisorOverrides, AuditLogs, SystemSettings, Notifications, and MaintenanceRecords.

Station has many exits, pumps, assignments, vehicles, transactions, integrations, devices, incidents, settings, and audit records. A transaction belongs to one station, pump, attendant/user and optional vehicle; it has items and payment history. An authorization belongs to a paid transaction, station and exit; scans and exit events refer to the authorization. Devices and integration logs belong to a station. Supervisor overrides, alerts, maintenance and audit entries carry station and actor references where applicable. Add unique station/reference and token-hash constraints, foreign keys, query indexes, UTC audit columns and concurrency protection. Hard-delete is not used for operational/audit records; retention is policy-driven.

## API endpoint inventory (v1)

- Auth: `POST /api/v1/auth/login`, logout, refresh, password reset initiation/completion, profile.
- Stations/assignments: list/read/update station, exits, pumps, user assignments (authorized administrative roles only).
- Users/roles: user lifecycle, assignments, role/permission inspection and administration.
- Vehicles: register/update/search/history, with audited registration correction.
- Transactions: create/search/read, permitted pending-field corrections, transaction items, status/history.
- POS/payment: supplier webhook intake where documented, simulated payment callback in development only, reconciliation, payment history, POS health.
- Authorizations: issue for independently verified eligible payment, validate/consume once, revoke, reprint existing valid receipt authorization.
- Exit/gateway: submit scan, request approved controller authorization, record response/completion, hardware health and fault events.
- Operations: incidents, supervisor overrides, notifications, reports, audit queries, maintenance and system settings.

Every request has validation, consistent problem responses, authentication/role checks and station-scope checks. Webhooks require supplier-supported authentication/signature validation and idempotency; no undocumented endpoint contract is assumed.

## Roles and access model

Super Administrator (platform-wide configuration); ROMVEX Support Administrator (limited support scope); Petrol Station Owner (assigned station administration); Station Manager (station operations/users/reports); Supervisor (operational review and reasoned override); Fuel Attendant (transactions and eligible receipt reprints); Security Operator (exit/device status and incident reporting); Read-Only Auditor (assigned-scope read-only records). Enforce assigned station scope in application queries and authorization policies; never trust a client-supplied station ID alone. Every privileged change is audited. No manual attendant payment override.

## POS, receipts, and hardware integration

POS contract separates transaction lookup/payment confirmation, health checks, callbacks/polling capability and reconciliation. Required supplier inputs: provider/product/version, interface documents, auth/signing, transaction/reference and final-settlement semantics, callback/polling support, sandbox, rate limits, test credentials, vendor approval, and support arrangements. Until delivered, use a clearly labeled simulator only.

Printer/scanner/ANPR/device providers are interfaces plus simulators. ESC/POS is enabled only after printer model/protocol compatibility is confirmed. ANPR confidence is retained and low-confidence/mismatch results require human review. Gateway authenticates to backend, reports health/events, queues non-authoritative events with deduplication, and never treats cached unknown payment as paid. Access-controller interface only supports a supplier-documented authorization request/status response; no direct spike actuation or software interlocks.

## Security, privacy, and safety requirements

Use ASP.NET Core Identity password hashing, secure access/refresh token handling, lockout/rate limiting, HTTPS, restrictive CORS/security headers, secret stores/environment configuration, EF parameterization, input validation, redacted structured logs, least privilege, audit trails, database backup/restore, dependency scanning and retention rules. Protect tokens against replay and brute force. Treat registration, CCTV and identifiable user data as personal information; restrict access and configure retention/deletion. POPIA legal review is required; no compliance claim is made.

Emergency and pedestrian egress remains available regardless of payment, internet or application state. Independent certified controller and supplier/qualified engineer own physical movement and safety interlocks. Fault, obstruction, uncertain occupancy, emergency-stop state, or lost safety communication means no authorization request that could cause hazardous motion; report and follow approved manual procedure. Tailgating creates an alert for human investigation only.

## Testing and acceptance

Unit tests cover fixed amount/litres/fill-to-full final-value rules, payment status transitions, payment event idempotency, token secrecy/expiry/reuse, station isolation, roles and audit. Integration tests use isolated test databases/provider test doubles for EF/API behavior, callbacks, reconciliation, receipt/scanner/ANPR/gateway faults, offline replay and controller safety faults. Simulator tests must clearly distinguish simulated from real devices/payments. No production database or real public equipment in automated tests. Build the full solution and execute tests before claiming a phase complete.

## Deployment and operations

Target Windows Server/IIS and SQL Server, with HTTPS certificates, environment-based secrets, least-privilege service identities, firewall allowlists, gateway service installation, health monitoring, log rotation/retention, tested backup/restore, migration review, staged updates and rollback procedures. Development setup may use local SQL Server/LocalDB; production migrations are reviewed and applied explicitly, never automatically as a destructive operation. No production deployment is performed by this project work.

## Estimated implementation sequence

1. Workspace inventory, isolated timestamped checkpoint, SDK/database/tool assessment.
2. Architecture, schema/API contracts, risk/dependency register (this document).
3. Solution/projects, build conventions, Git, configuration templates.
4. Domain model, EF Core context/configurations and reviewed initial migration.
5. Identity, station assignments, policies, audit, login/lockout.
6. Transactions and final POS settlement semantics.
7. POS abstraction/simulator, idempotent payment evidence and reconciliation.
8. Token hash lifecycle and atomic QR authorization consumption.
9. Receipt generation/printer abstraction and simulator.
10. Scanner, ANPR, sensor and controller abstractions/simulators.
11. Local gateway health/event queue and safe synchronization.
12. Blazor operations and attendant workflows with persisted data.
13. Incidents, alerts, reports, supervisor workflow.
14. Security/offline hardening, automated tests, recovery checks.
15. Documentation, installation, deployment and supplier handover.

Each phase requires build/test, checkpoint, changed-file/dependency report and review; no phase is called complete with failing required tests.

## Open external dependencies and limits

The existing POS supplier/product/API and credentials are not supplied; real POS integration remains blocked. Printer model, scanner, ANPR/CCTV, sensors, controller make/model/protocol/certification, site layout, network/firewall, supplier approval, safety engineering and commissioning are unknown. No production credentials, real customer seed data, real device adapter or physical commissioning can responsibly be supplied. Budget R250,000 incl. VAT is a planning target only; supplier quotations/site survey determine feasibility. Monthly support, hosting, hardware, civil/electrical work and independent safety costs need quotes. Do not reduce mandatory safety to meet budget.

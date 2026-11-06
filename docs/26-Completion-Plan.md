# Completion Plan

## Priority 1 — Application startup and solution integrity

- Restore the API startup entry point and dependency registration
- Ensure all project references and target frameworks align with the available SDK
- Validate the solution can start in development mode without hidden template scaffolding issues

Status: Partially complete in this task; full build/test validation remains blocked by the host sandbox.

## Priority 2 — Database configuration and persistence

- Verify EF Core model and relationships
- Generate a migration set on a valid SQL Server machine
- Configure the connection string and SQL Server deployment requirements
- Validate audit timestamps and concurrency tokens

Status: Partially complete.

## Priority 3 — Authentication and role permissions

- Validate the JWT configuration and station-scoped claims
- Ensure the role catalog is enforced consistently in endpoints
- Review bootstrap and refresh token handling before production use

Status: Partially complete.

## Priority 4 — Fuel transaction lifecycle

- Verify creation, state transitions, and cancellation handling
- Enforce payment requirement before authorisation
- Maintain decimal-based monetary calculations

Status: Partially complete in service logic, not fully validated.

## Priority 5 — POS payment verification

- Keep supplier integration behind explicit interfaces
- Keep the simulator clearly separate from production adapters
- Validate idempotency and duplicate payment handling

Status: Partially complete; the simulator is in place, but supplier integration is still required.

## Priority 6 — QR exit authorisation

- Require payment before issuing tokens
- Enforce expiry, single-use, and revocation
- Reject duplicate scans and concurrent exit processing

Status: Service logic is scaffolded but not fully validated under test coverage.

## Priority 7 — Hardware abstractions and simulators

- Keep safety logic separate from software timers or payment status
- Ensure hardware adapters remain simulation-only outside approved dev/test environments
- Document remaining physical system dependencies

Status: Complete as a simulation layer; not yet production approved.

## Priority 8 — Local Gateway

- Add durable local queueing and health reporting
- Connect station-scoped local events to the central API
- Review emergency and safety interactions

Status: Minimal placeholder only.

## Priority 9 — Administration portal

- Replace default Razor template pages with real application screens
- Wire actual station, user, payment, and incident data to the API
- Validate forms and action flows

Status: Missing real operational UI.

## Priority 10 — Fuel attendant interface

- Complete attendant login and pump selection
- Add vehicle registration capture and transaction creation
- Provide payment status and print flows

Status: Not fully implemented.

## Priority 11 — Reporting and incident management

- Add incident and reporting workflows
- Connect monitoring and supervision flows
- Review operational escalation and override patterns

Status: Scaffold exists but not complete.

## Priority 12 — Security and audit logging

- Review secrets management
- Review SQL injection and validation coverage
- Review CORS, rate limiting, and POPIA concerns
- Ensure every privileged operation is audited

Status: Partial baseline; production review still required.

## Priority 13 — Automated tests

- Replace placeholder tests with real assertions
- Cover auth, payments, QR expiry, duplicate scans, concurrency, hardware faults, and persistence
- Distinguish passed, failed, skipped, and blocked tests clearly

Status: Missing.

## Priority 14 — Deployment documentation

- Add SQL Server deployment instructions
- Document supplier integration guidance
- Document physical safety approvals and operational controls

Status: Missing / incomplete.

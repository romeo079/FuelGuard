# Production Readiness Checklist

## Required before production

- [ ] Install a working .NET SDK on the target build machine and ensure WorkloadResolver can access the OS service layer.
- [ ] Provision and validate a dedicated SQL Server instance for FuelGuard.
- [ ] Create a real `ConnectionStrings:FuelGuard` connection string with least-privilege database access.
- [ ] Configure `Jwt:SigningKey`, `Jwt:Issuer`, and `Jwt:Audience` with a production secret.
- [ ] Replace simulator-only hardware and POS adapters with approved supplier integrations.
- [ ] Validate the physical retractable tyre-spike barrier and independent safety controller with manufacturer-approved interfaces.
- [ ] Confirm emergency egress remains independent of payment, internet availability, and central API access.
- [ ] Add production logging, alerting, and retention policies.
- [ ] Complete penetration testing for auth, API, and CORS policy.
- [ ] Complete POPIA and data minimisation review.
- [ ] Run the full regression suite against SQL Server.

## Current status

- API startup scaffolding: implemented and documented for local development
- Database layer: EF model and DbContext present, but no migration execution could be validated here
- Authentication: JWT issuer/scaffolding implemented; production signing key must be rotated before deployment
- Hardware and POS: explicitly simulation-only and not supplier-approved
- Safety: no physical barrier or safety interlock hardware was verified in this environment

## Handover note

The application is not production-ready. It is a usable baseline for continuation work on a properly provisioned environment with real SQL Server and supplier hardware documentation.

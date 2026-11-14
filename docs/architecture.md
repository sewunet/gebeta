# Architecture

## 1. Shape: a modular monolith

One ASP.NET Core application (one Docker image), one PostgreSQL database, split into strict
modules that talk to each other only through RabbitMQ events.

```
              React app (web/)
                    │  HTTPS, JSON (OpenAPI)
                    ▼
          ┌──────────────────────────┐
          │       Gebeta.Api         │  auth, routing, OpenAPI, SignalR
          ├──────────────────────────┤
          │ Platform │ Accounting │ Stock │ Manufacturing │ Hr │ Payroll │ CashMgmt │
          ├──────────────────────────┤
          │ SharedKernel (Money, DocumentBase, events)                       │
          └──────────────────────────┘
                    │
     PostgreSQL (one schema per module)   RabbitMQ (events)   Redis (cache, locks)   Hangfire (jobs)
                         all running as Docker containers (docs/deployment.md)
```

**Every business module runs on its own and adds features when plugged in next to others.**
The full design is in [plugin-architecture.md](plugin-architecture.md) ([ADR 0006](adr/0006-independent-pluggable-modules.md)).
The default deployment is one host with the chosen modules ([ADR 0001](adr/0001-modular-monolith.md));
the same modules can also run as separate services.

## 2. Module rules

1. **Core (Platform) is the only required module.** Any business module can be installed alone
   on top of Core, or with any combination of other modules.
2. **Each module owns its tables**, in its own PostgreSQL schema (`accounting.*`, `stock.*`, ...).
3. A module never reads or writes another module's tables, and never references another module's
   code. It connects only through:
   - **integration events** (outbox/inbox), for "something happened" (for example `SalarySlipSubmitted`);
   - **capabilities** defined in Core, each with a fallback inside the asking module
     (for example `IPaymentDaysProvider`: HR when installed, manual entry otherwise);
   - **extension tables** keyed by another record's id (Payroll's `employee_profile`);
   - **UI slots** in the React shell (Payroll adds a Salary tab to the Employee form).
4. Dependencies:

```
          Core (Platform + shared Contracts)
        ▲     ▲      ▲       ▲       ▲       ▲
       HR  Payroll  Cash  Accounting Stock  Manufacturing     ← no arrows between modules
        └──────── integration events and capabilities ────────┘
```

5. Architecture tests (NetArchTest) fail the build if a module references another module's
   code (only `*.Contracts` packages are allowed).

### Project layout per module (clean architecture, [ADR 0008](adr/0008-clean-architecture.md))

```
Modules/Payroll/
├─ Gebeta.Payroll.Domain/          # entities, value objects, rules, repository interfaces. No EF, no HTTP.
├─ Gebeta.Payroll.Application/     # commands, queries, handlers, unit-of-work interface
├─ Gebeta.Payroll.Presentation/    # HTTP endpoints
├─ Gebeta.Payroll.Infrastructure/  # EF Core, repositories, event handlers, PayrollModule (composition root)
└─ Gebeta.Payroll.Contracts/       # DTOs, integration events, capability interfaces (no dependencies)
```

Dependencies point inwards (Presentation → Application → Domain → SharedKernel) and are checked by
`tests/Gebeta.ArchitectureTests` on every build.

## 3. Documents and their lifecycle

Most business records are **documents** with the same lifecycle that ERPNext users know:

```
 Draft ──submit──► Submitted ──cancel──► Cancelled
   │                                         │
   └──delete (draft only)                    └──amend──► new Draft (name-1)
```

`DocumentBase` (SharedKernel) gives every document:

| Field | Meaning |
|---|---|
| `Id` (UUID v7) | Internal key |
| `Name` | Human number from a naming series, e.g. `PCR-2026-00012` |
| `CompanyId` | Every transaction belongs to one company |
| `Status` / `DocStatus` | Draft, Submitted, Cancelled (plus a business status, e.g. Pending, Paid) |
| `PostingDate` | Accounting date, checked against closed periods |
| `AmendedFromId` | The cancelled document this one replaces |
| `Version` | Optimistic concurrency (PostgreSQL `xmin`) |
| `CreatedBy/At`, `ModifiedBy/At` | Audit |

Rules:
- Submitted documents are **immutable**, except for fields marked "allow on submit".
- `Submit` and `Cancel` run inside **one database transaction**, together with every posting to
  the module's **own** ledgers (stock ledger, cashier ledger, leave ledger, GL inside Accounting).
- Effects in **other** modules (for example the GL journal of a salary slip) follow through
  integration events, with a visible status (Pending, Posted, Failed) and retries.
- Cancel never deletes ledger rows; it writes reversal rows ([ADR 0004](adr/0004-append-only-ledgers.md)).

## 4. Request flow

```
HTTP endpoint
  → authorization (role + record permission, see platform.md)
  → command handler (Application)
      → load aggregate (Domain), run rules
      → post to the module's own ledgers (same transaction)
      → save; outbox stores integration events (e.g. JournalRequested)
  → commit
  → outbox dispatcher publishes events to RabbitMQ (docs/messaging.md)
  → other installed modules handle them through their inbox (idempotent)
```

- **Outbox pattern**: events are saved in the same transaction as the data and dispatched after
  commit, so an event is never lost and never sent for a rolled-back change.
- Slow work (stock reposting, payroll runs, revaluation, report exports) runs as **background
  jobs** with per-key locks (for example one reposting job per item + warehouse).

## 5. Multi-company and branches

- Every transaction and ledger row has `CompanyId`. Branches (`branch_office` in the current
  system) and cost centres are dimensions on ledger rows.
- Users get access per company (and optionally per branch) through Platform permissions.
- One database for all companies. Separate databases per customer only if Abay Systems later sells it as SaaS.

## 6. Reporting

- Transactional reads go through EF Core.
- Heavy reports (trial balance, stock balance, payroll register) use **Dapper over SQL views**
  and, where needed, materialized views refreshed by jobs.
- Exports: Excel (ClosedXML) and PDF (QuestPDF).

## 7. Cross-cutting

| Concern | Approach |
|---|---|
| Auth | OIDC (Keycloak), JWT access tokens, refresh in the React app |
| Realtime | SignalR hubs for notifications and job progress |
| Files | S3-compatible storage (any S3 service; a local volume in development) |
| Logging | Serilog → Seq or Grafana Loki; OpenTelemetry traces |
| Config | appsettings + environment variables; secrets never in git |
| Localisation | English and Amharic UI strings; Ethiopian calendar display option |

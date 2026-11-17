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

The list of modules lives in one place, `src/Gebeta.Composition` (`ModuleCatalogue.All()`). The API
and the tools under `src/Tools` (the ERPNext migration) load the same modules from it, so a tool
can never run with a different set of rules than the API.

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

## 8. Engineering review (step 34) and known technical debt

A review of steps 25–34 with a senior engineer's eye led to these changes:

| Finding | Fix |
|---|---|
| Stock posting, cancelling and reposting loaded the **whole history** of each item and warehouse to find one state, so every posting got slower as history grew. | Bounded, indexed queries: the last row at or before a point, and the rows after it (`IStockRepository.LastAtOrBeforeAsync`, `RowsAfterAsync`, `TransferInAsync`). Cost now follows what changed, not the item's age. |
| Reposting looked up the entry once per changed transfer row. | The transfer-in row is found directly by entry and line. |
| Each API test class started its own PostgreSQL container and server (~20); the suite took over ten minutes. | One assembly fixture for all API tests (`AssemblyInfo.cs`); tests isolate by company. The whole suite runs in about 2.5 minutes. |
| Sharing the server exposed a **real bug**: a new user's parallel first requests (the web app loads several at once) could insert the user twice and fail. | `IUserRepository.AddOrGetExistingAsync` handles the unique-key race and returns the winner; regression test with eight parallel first requests. |
| Accounting had four copies of "post a journal another module asked for". | One `ModuleJournalPoster` (idempotent by source, chart ensured, codes mapped, numbered and posted); consumers are a few lines each. |
| Selling was generated from Buying, duplicating the order-progress rules. | `OrderProgress` in SharedKernel holds the rules both use. Module plumbing (contracts, schema, endpoints) stays per module on purpose: each module owns its data and API. |
| Screens imported other screens' page files for data hooks. | Data hooks live in `web/src/lib/queries.ts`; only the router imports pages (lazily). |
| No CI. | `.github/workflows/ci.yml`: build with warnings as errors, pending-migration check per module, all tests, web type check, build and unit tests. |

**Debt kept deliberately, and what would trigger paying it:**
- *Reposting runs inline* in the posting transaction. Fine while a backdated entry touches hundreds of rows; move to a background job with a reposting page (as designed in docs/modules/stock.md) when an item's later history reaches tens of thousands of rows or postings start waiting on locks.
- *Account codes are conventions* shared by modules (1300, 2150, 5000, 5100…). Fine for the template chart; build the account-mapping screen (docs/modules/accounting.md) before a customer with its own chart goes live.
- *Order fulfilment is computed on read* by asking Stock and Accounting. Fine for lists of hundreds of orders per period; add a read model fed by events if order lists grow into thousands per page view.
- *The advisory lock key* for stock postings is a 64-bit hash of item and warehouse; a collision only serialises two unrelated postings, it never corrupts data.

### Stock ledger order (step 42)

The stock ledger's `sequence` gives the posting order within a day, and readers take the last row
of an item and warehouse as its stock. The database used to number new rows as it received them,
but EF Core sends the inserts of one save sorted by primary key (a GUID), not in the order the code
added them. `StockDbContext.SaveChangesAsync` now takes the numbers from the column's own sequence,
in one round trip, and gives them to the rows in the order they were added; the column is identity
"by default" so explicit numbers are allowed.

The same pitfall applies anywhere the order of rows written in one save matters. Do not rely on an
identity column for it; give the order explicitly.

Rows written before the fix keep their old numbers. That only affects test data on development
databases.


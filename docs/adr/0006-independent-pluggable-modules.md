# ADR 0006: Independent, pluggable modules

- Status: Accepted
- Date: 2026-11-14
- Amends: [ADR 0001](0001-modular-monolith.md)

## Context

Each ERP module must run on its own, so a customer can use only HR, only Payroll, or only Stock.
When more modules are installed together, they should add features to each other.
ADR 0001 assumed that a document and all its ledger postings in other modules are saved in one
database transaction, which ties modules together.

## Decision

- **Core** is the only required module. Business modules depend on Core and on other modules'
  `*.Contracts` packages only, never on their code or tables.
- Modules connect through integration events (outbox/inbox), capabilities with fallbacks,
  extension tables, and UI slots ([plugin-architecture.md](../plugin-architecture.md)).
- Cross-module effects (for example payroll → GL journal) are **eventually consistent**, visible
  to the user with a status, retried on failure, and checked by reconciliation jobs.
- Rules that need an immediate answer stay inside one module (for example the cashier overdraw
  check uses Cash Management's own cashier ledger).
- The default deployment is still one host and one database with the chosen modules
  (ADR 0001 stays valid for that). The same modules can also be deployed as separate services.
  Events always go through RabbitMQ ([ADR 0007](0007-rabbitmq-and-docker.md)).

## Consequences

- Modules can be sold, installed, tested and released on their own.
- Each module needs fallbacks for missing partners, which adds some work per module.
- A few things ERPNext does in one transaction (stock value → GL, payroll → GL) now take a
  moment and need reconciliation. Month-end close waits until no journals are pending.
- CI must test each module alone, with partners, and with all modules.

## How it is checked (2026-11-18)

- `tests/Gebeta.ArchitectureTests`: modules reference each other only through `*.Contracts`.
- `tests/Gebeta.Api.Tests/ModuleIndependenceTests.cs` checks the runtime. It starts the real API
  on a fresh database for Core alone, for Core with each business module alone, and for all
  modules. Each time:
  - services are validated strictly (`ValidateOnBuild`), and every command handler, query handler
    and event consumer is resolved;
  - migrations run, the module's main endpoint answers, and other modules' endpoints are absent;
  - flows that would use a partner work without it: a purchase order with no Stock or Accounting,
    a salary slip with no HR or Accounting.

  Removing a fallback (for example Buying's `IPurchaseReceiptLookup`) makes these tests fail.
- `web/src/app/apps.test.ts`: every screen of a business module is tied to that module, so it only
  shows when the module is installed.
- The migration tool takes `--modules`. It loads only those modules, skips the steps of the others
  (reported as skipped), and adapts the steps that touch a partner (item valuation needs Stock;
  orders need Buying or Selling; stock accounts go through 3900 only with Stock).


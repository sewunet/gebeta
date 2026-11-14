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

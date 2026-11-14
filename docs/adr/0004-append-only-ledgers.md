# ADR 0004: Append-only ledgers, reversal on cancel

- Status: Accepted
- Date: 2026-11-14

## Decision

GL, payment ledger, stock ledger movements, leave ledger and cashier movements are never updated or
deleted. Cancelling a document writes reversal rows. The database role used by the application has
no `UPDATE`/`DELETE` rights on these tables (stock valuation columns are the documented exception,
see [stock.md](../modules/stock.md)).

## Consequences

- A full audit trail; any balance at any date can be rebuilt.
- Reports must net original and reversal rows (or filter both out).
- Tables grow faster; partition by fiscal year when needed.

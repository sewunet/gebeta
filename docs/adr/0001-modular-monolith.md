# ADR 0001: Modular monolith, not microservices

- Status: Accepted, amended by [ADR 0006](0006-independent-pluggable-modules.md)
  (modules are independent; cross-module effects go through events, not one shared transaction)
- Date: 2026-11-14

## Context

ERP modules are tightly linked: submitting a stock entry must post to the stock ledger and the
general ledger in one transaction; a payroll run posts to accounting; cash checks run on GL posting.
The team is small and on-premises deployments are common.

## Decision

Build one deployable ASP.NET Core application with one PostgreSQL database, split into modules with
strict boundaries (own schema, public contracts, domain events, architecture tests).

## Consequences

- One transaction can cover a document and all its ledger postings: no distributed transactions,
  no sagas for normal business flows.
- Simple deployment and debugging.
- Boundaries must be enforced by tests and reviews, or the code becomes a tangle.
- A module can be extracted into a service later if a real need appears (for example an
  attendance ingestion service for many devices).

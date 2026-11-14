# ADR 0005: Replace ERPNext module by module

- Status: Accepted
- Date: 2026-11-14

## Decision

Replace ERPNext one module at a time (strangler pattern), starting with Payroll, while ERPNext
stays the system of record for everything not yet moved. Each record type has one master system
at a time ([erpnext-integration.md](../erpnext-integration.md)). Each module goes live only after
parallel runs match ERPNext's numbers ([roadmap.md](../roadmap.md)).

## Why

A big-bang switch of accounting, stock, HR and manufacturing at once has a high risk of
failure and leaves no way back. Module-by-module gives value early, limits risk, and lets the
team learn on a module with clear, testable results (payslips).

## Consequences

- An integration layer with ERPNext must be built and later removed.
- Some data is duplicated for a while; reconciliation jobs are required.

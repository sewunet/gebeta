# ADR 0009: Ethiopia first

- Status: Accepted
- Date: 2026-11-14

## Context

Gebeta is built for Ethiopian businesses and public enterprises. Generic ERPs treat Ethiopia as a
localisation add-on, which leaves gaps: the Ethiopian calendar and fiscal year, Ethiopian income tax
and pension, withholding receipts, VAT and e-invoicing rules, labour law leave and overtime, Amharic.

## Decision

- Ethiopian rules are the **defaults**: ETB, the Hamle–Sene fiscal year, `Africa/Addis_Ababa`,
  Ethiopian tax and pension tables, Labour Proclamation 1156/2019 leave and overtime, English and
  Amharic UI.
- The Ethiopian calendar, fiscal year and TIN live in the shared kernel
  (`Gebeta.SharedKernel.Ethiopia`) because every module needs them.
- Legal values are **data with effective dates**, never constants in code, each with its legal
  source and a reviewer ([ethiopia.md](../ethiopia.md#how-legal-rules-are-implemented)).
- Other countries are possible later by adding rule tables, not by changing the modules.

## Consequences

- Faster fit for Ethiopian customers; fewer customisations per customer.
- The team must track proclamations and directives and update rule tables; each change needs a
  qualified review before release.

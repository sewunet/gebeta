# ADR 0003: Exact decimals for all money and quantities

- Status: Accepted
- Date: 2026-11-14

## Decision

- C#: `decimal`, wrapped in `Money` and `Quantity` value objects.
- PostgreSQL: `numeric` with fixed precision (see [conventions](../conventions.md#money-quantities-rates)).
- JSON: numbers sent as strings.
- One rounding helper, `MidpointRounding.AwayFromZero`, applied at defined points only.
- `double`/`float` forbidden for money, quantity and rates (an analyzer rule fails the build).

## Why

Binary floating point cannot represent values like 0.1 exactly; sums drift, and ledgers stop
balancing by a cent. Auditors and tax reports need exact figures.

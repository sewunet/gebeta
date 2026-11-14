# ADR 0002: .NET 10 and PostgreSQL

- Status: Accepted
- Date: 2026-11-14

## Context

Options considered: keep Python/Frappe, Node.js (TypeScript), Java/Kotlin, .NET, Go.
Needs: exact decimal arithmetic, strong typing for a large codebase, mature ORM and transactions,
long-term support, good hiring pool.

## Decision

- Backend in **C# on .NET 10 (LTS)**, ASP.NET Core, EF Core.
- Database **PostgreSQL**.
- Frontend in **TypeScript** with React.

## Reasons

- `decimal` is built into C#: exact money arithmetic without a library.
- Strong static typing and refactoring tools suit a codebase that will grow large.
- EF Core + Npgsql are mature; PostgreSQL has `numeric`, JSONB (custom fields), schemas per module,
  row locking, materialized views, and good backup tooling.
- LTS releases with three years of support.

## Consequences

- The team needs C# skills; current Frappe knowledge (Python) still matters during coexistence.
- MariaDB from ERPNext is not reused; data moves through the migration tool.

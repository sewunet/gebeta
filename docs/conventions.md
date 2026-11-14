# Conventions

## Money, quantities, rates

- Use `decimal` in C#, `numeric` in PostgreSQL, and `string` in JSON. Never `double`/`float`.
- Value objects in SharedKernel:
  - `Money(decimal Amount, string Currency)`: arithmetic only between the same currency.
  - `Quantity(decimal Value, string Uom)`.
  - `ExchangeRate(string From, string To, decimal Rate, DateOnly Date)`.
- Database precision:

| Kind | Column type |
|---|---|
| Money | `numeric(21,6)` stored, rounded to currency precision (ETB: 2) when posted |
| Quantity | `numeric(21,6)` |
| Rate / price | `numeric(21,9)` |
| Exchange rate | `numeric(18,9)` |
| Percent | `numeric(9,6)` |

- Rounding: `MidpointRounding.AwayFromZero` (matches ERPNext and Ethiopian practice), done in one
  helper (`Rounding.ToCurrency`). Document totals are rounded once, then the difference goes to a
  rounding-adjustment line so the ledger balances to the cent.
- Frontend: money arrives as a string and is shown with `Intl.NumberFormat`. Calculations that
  matter happen on the server; the client may preview totals with `decimal.js`.

## Dates and time

- `DateOnly` for posting dates, `DateTimeOffset` (UTC in the database) for timestamps.
- Company time zone: `Africa/Addis_Ababa`.
- The Ethiopian calendar is a **display** option in the UI; everything is stored Gregorian.

## Naming

| Thing | Style | Example |
|---|---|---|
| C# types, methods | PascalCase | `SalarySlip`, `SubmitAsync` |
| Database tables, columns | snake_case (EF naming convention plugin) | `payroll.salary_slip`, `gross_pay` |
| API routes | kebab-case plural nouns | `/api/payroll/salary-slips/{id}/submit` |
| TypeScript | camelCase variables, PascalCase components | |
| Document numbers | Naming series per doctype and company | `SAL-SLP-2026-00001` |

## API

- REST over JSON, versioned by URL prefix only when a breaking change is needed (`/api/v2/...`).
- Actions on documents are explicit endpoints: `POST .../{id}/submit`, `.../cancel`, `.../amend`.
- Lists: `?filter=`, `?sort=`, `?page=`, `?pageSize=` (max 500). Return `{ items, total }`.
- Errors: RFC 9457 Problem Details. Validation errors list each field.
- Concurrency: send `version` back on update; mismatch returns `409 Conflict`.
- Idempotency: `Idempotency-Key` header on submit and payment endpoints.

## Code

- Nullable reference types on, warnings as errors.
- Domain classes have no EF or HTTP dependencies. Rules live in the domain, not in handlers.
- One command or query per handler file.
- No static state. Time comes from `TimeProvider`, so tests can fix the date.
- Every bug fix comes with a test that fails before the fix.

## Database

- Every table: `id uuid` (v7), `company_id` where it applies, `created_at/by`, `modified_at/by`.
- Ledger tables are **append-only**: the application role has no `UPDATE`/`DELETE` grant on them.
- Foreign keys always on. Indexes for every foreign key and for common filters
  (`company_id, posting_date`).
- Migrations are reviewed like code; destructive changes need a two-step release.

## Git

- `main` is always deployable. Feature branches, pull requests with one review minimum.
- Conventional commit messages (`feat(payroll): ...`, `fix(stock): ...`).

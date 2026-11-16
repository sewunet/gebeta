# Data migration

Tool: `Gebeta.Migration`, a console app that reads ERPNext (MariaDB read replica, or the REST
API for small sets) and writes through the **application layer** of Gebeta, so every rule and
validation runs. No direct inserts into ledger tables, except opening balances through the
dedicated opening-balance import.

> **Built so far** (`src/Tools/Gebeta.Migration`): the company (part of step 1), customers and
> suppliers (masters for step 8), and items and warehouses (masters for step 9). Not yet built:
> opening invoices, opening stock and opening balances. Each run prints, for every step, how many
> records were created, adopted (an existing Gebeta record with the same code was used) or already
> migrated, plus warnings, failures and the count check.
>
> ```
> dotnet run --project src/Tools/Gebeta.Migration -- --company "Arjo Dedessa Sugar Factory" --dry-run
>     ERPNext: --source "<MariaDB connection string>" or ERPNEXT_DB
>     Gebeta:  --target "<PostgreSQL connection string>" or ConnectionStrings__Main
>     --target-company <id>   load into a company already set up in Gebeta
>     --tin <TIN>             when ERPNext has no valid TIN for the company
>     --steps parties,items   only these steps (company always runs first)
> ```
>
> How it works:
> - The tool loads the same modules as the API (`Gebeta.Composition.ModuleCatalogue`) and sends
>   the same commands, signed in as the `gebeta.migration` user. Every rule runs, nothing is
>   inserted directly.
> - ERPNext names are cleaned into Gebeta codes the same way on every run (`Codes.Normalise`:
>   `Cement 50 kg` → `CEMENT-50-KG`, and `Stores - ADSF` → `STORES`). Two records that end up
>   with the same code are reported as a failure, not merged.
> - Data problems are reported per record and the run carries on: an invalid TIN is a warning and
>   the party comes in without one. Disabled ERPNext records are left behind, and a name that is
>   both a customer and a supplier becomes one party of type Both.
> - A dry run checks every record against the real rules and writes nothing.
> - Exit code 0 when every step is OK, 1 when any step failed, 2 for wrong arguments.
>
> Debt: each record is its own command and transaction, which is fine for thousands of masters.
> Opening stock and invoices (tens of thousands of rows) will need batching. The ERPNext source
> reads all records at once with no paging, which is fine for masters only.

## Principles

1. **Repeatable.** Every run can start from an empty database. Mappings (ERPNext name → Gebeta id) are
   stored in `migration.id_map`.
2. **Per module, at its phase.** Masters first, then open documents, then balances.
3. **Proved by numbers.** Each step has a check that compares totals with ERPNext; a step fails if
   they differ.
4. **Dry runs** on a copy of production, at least three times before each cut-over.

## Order

| Step | Data | Check |
|---|---|---|
| 1 | Companies, branches, departments, cost centres, fiscal years | Counts |
| 2 | Users, roles, record restrictions | Counts, sample logins |
| 3 | Employees and HR masters (grades, steps, levels) | Counts, sample comparison |
| 4 | Salary components, structures, assignments; formulas copied as they are (same syntax) | Recalculate last month's slips in Gebeta = ERPNext slips |
| 5 | Salary slip history (read-only, for back payment and reports) | Sum of net pay per month |
| 6 | Leave ledger | Balance per employee per leave type |
| 7 | Chart of accounts, opening balances per company at cut-over | Trial balance at cut-over date |
| 8 | Open invoices and payments (payment ledger) | AR/AP ageing totals |
| 9 | Items, warehouses, opening stock with valuation (FIFO queues) | Stock value per warehouse = GL stock account |
| 10 | Open orders, BOMs, work orders, assets | Counts and values |

## Formulas

Gebeta's formula engine reads the ERPNext (Python) formula syntax, so salary formulas are copied
unchanged. The migration tool:
- parses every formula and condition and lists the ones that use anything outside the allowed names
  and functions (for example attribute access such as `doc.x`), for manual rewrite;
- recalculates real slips in Gebeta and compares them with ERPNext (step 4).

## Cut-over checklist (per module)

- [ ] Final dry run signed off by finance or HR.
- [ ] ERPNext documents for the module locked (role permissions set to read-only).
- [ ] Final migration run and checks green.
- [ ] Users trained; support rota for the first two weeks.
- [ ] Rollback plan: ERPNext unlocked again, Gebeta entries since cut-over listed for re-entry.

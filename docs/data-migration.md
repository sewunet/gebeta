# Data migration

Tool: `Gebeta.Migration`, a console app that reads ERPNext (MariaDB read replica, or the REST
API for small sets) and writes through the **application layer** of Gebeta, so every rule and
validation runs. No direct inserts into ledger tables, except opening balances through the
dedicated opening-balance import.

> **Built so far** (`src/Tools/Gebeta.Migration`):
> - masters: the company (part of step 1), customers and suppliers, items with their valuation method, warehouses;
> - cut-over balances, with `--cutover`: open invoices (step 8), opening stock (step 9) and the opening trial balance (step 7).
>
> Not yet built: HR, payroll, open orders and assets. Each run prints, for every step, how many records were created,
> adopted (an existing Gebeta record with the same code was used) or already migrated, plus warnings, failures and the
> step's check.
>
> ```
> dotnet run --project src/Tools/Gebeta.Migration -- --company "Arjo Dedessa Sugar Factory" --dry-run
>     ERPNext: --source "<MariaDB connection string>" or ERPNEXT_DB
>     Gebeta:  --target "<PostgreSQL connection string>" or ConnectionStrings__Main
>     --target-company <id>    load into a company already set up in Gebeta
>     --tin <TIN>              when ERPNext has no valid TIN for the company
>     --cutover 2026-07-07     also bring the balances at the end of that day (the last day kept in ERPNext)
>     --account-map file.csv   ERPNext account → Gebeta account; written with suggestions when missing
>     --steps parties,items    only these steps (company always runs first):
>                              parties, items, warehouses, invoices, stock, opening
> ```
>
> How the masters come over:
> - The tool loads the same modules as the API (`Gebeta.Composition.ModuleCatalogue`) and sends
>   the same commands, signed in as the `gebeta.migration` user. Every rule runs, nothing is
>   inserted directly.
> - ERPNext names are cleaned into Gebeta codes the same way on every run (`Codes.Normalise`:
>   `Cement 50 kg` → `CEMENT-50-KG`, and `Stores - ADSF` → `STORES`). Two records that end up
>   with the same code are reported as a failure, not merged.
> - Data problems are reported per record and the run carries on: an invalid TIN is a warning and
>   the party comes in without one. Disabled ERPNext records are left behind, and a name that is
>   both a customer and a supplier becomes one party of type Both.
> - Each item's valuation method comes along (FIFO or moving average). LIFO, which IFRS does not
>   allow, becomes moving average with a warning.
>
> How the cut-over balances come over (all dated the cut-over day):
> - **Open invoices** come from ERPNext's Payment Ledger as at the cut-over day. Each becomes a
>   Gebeta opening invoice for its outstanding amount, with its original date, due date and number
>   as the reference, so ageing carries on. Opening invoices post nothing to the GL; the receivable
>   and payable accounts come from the trial balance. Unallocated advances (negative balances) are
>   not invoices: a warning gives their total, and they stay in the control account.
> - **Opening stock** is the last stock ledger row per item and warehouse on or before the cut-over
>   day. Each warehouse becomes one receipt against **3900 Opening Balance Clearing**. FIFO queues
>   are kept as separate lines, and the last layer takes the rounding, so each warehouse's value is
>   ERPNext's to the cent. Negative stock, or stock held by a disabled item or warehouse, stops
>   that warehouse.
> - **The opening trial balance** is one journal (`PostOpeningBalancesCommand`):
>   - Balance-sheet accounts bring their whole balance. Income and expense accounts bring only
>     this fiscal year's; earlier years' profit or loss goes to 3200 Retained Earnings.
>   - ERPNext stock accounts post to 3900, which meets the opening stock, so 3900 ends at zero
>     exactly when stock value and stock accounts agree.
>   - The account map is a CSV for finance to review and sign off. The first run (a dry run is
>     enough) writes it with suggestions. Loans, depreciation and other unclear balance-sheet
>     accounts are left blank on purpose. An account Gebeta lacks can be added under a group with
>     `POST /api/accounting/accounts`.
> - **The proof** is the opening step's check: Gebeta's trial balance on the cut-over day equals
>   ERPNext's, account by account, with 3900 at zero. The opening stock's journals reach the
>   ledger through the running Gebeta server (outbox → RabbitMQ → Accounting), so the check waits
>   up to a minute for them. If the server was not running, start it and run `--steps opening`
>   again; a step already done only re-checks.
> - Nothing the tool writes into Gebeta mentions ERPNext. Remarks read "Opening balance" and
>   "Opening stock at cut-over <date>", and old document numbers are kept as references.
>
> Debt:
> - Each record is its own command and transaction. That's fine for thousands of masters and open
>   invoices, but tens of thousands of open invoices would want batching.
> - The ERPNext source reads each list at once, with no paging.
> - Advances are not carried as unallocated payments, so the receivables subledger and the GL
>   control account differ by the advances until they are allocated.

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

# Data migration

Tool: `Gebeta.Migration`, a console app that reads ERPNext (MariaDB read replica, or the REST
API for small sets) and writes through the **application layer** of Gebeta, so every rule and
validation runs. No direct inserts into ledger tables, except opening balances through the
dedicated opening-balance import.

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

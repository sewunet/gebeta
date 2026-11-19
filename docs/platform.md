# Core (Platform) module

The framework part: everything Frappe gave for free and every other module needs.
Core is the **only required module**; every business module runs on Core alone
(see [plugin-architecture.md](plugin-architecture.md)).

## Shared directory (small on purpose)

So that modules can run alone, Core keeps a minimal shared record for things several modules
refer to. Each module adds its own details in its own extension tables.

| Record | Core keeps | Added by modules |
|---|---|---|
| Employee | Name, code, company, branch, department, user, status | HR: personal data, grade, step, history. Payroll: salary profile. Cash: cashier. |
| Party | Name, type (customer / supplier / other), tax ID, contacts | Accounting: receivable/payable accounts. Buying/Selling: terms. |
| Item | Code, name, group, UOM | Stock: valuation, batches, serials. Manufacturing: BOM. Accounting: income/expense accounts. |

## Capabilities and module registry

- Core defines the capability interfaces (`IPaymentDaysProvider`, `IJournalSink`, ...) and the
  integration event contracts.
- The module registry knows which modules are installed and which module provides each
  capability; `/api/modules` gives this to the frontend.
- Core provides the outbox/inbox and the RabbitMQ event transport ([messaging.md](messaging.md)).

## Companies and organisation

| Entity | Notes |
|---|---|
| Company | Default currency, chart of accounts, fiscal year, time zone, abbreviation (e.g. `ESIG`) |
| Branch | Branch offices; a dimension on transactions |
| Department | Tree per company |
| Cost Centre | Tree per company; a dimension on ledger rows |
| Fiscal Year | Per company; Ethiopian fiscal year (Hamle 1 – Sene 30) supported |
| Currency, Exchange Rate | Daily rates; manual or imported |

## Users, roles, permissions

- Identities live in **Keycloak**; the Platform keeps a `user` table (profile, default company,
  language) linked by the Keycloak subject ID.
- **Roles** (e.g. Accounts User, Accounts Manager, HR Manager, Cashier, Stock User).
- **Permission rules**, per role and document type:

| Right | Meaning |
|---|---|
| Read, Create, Write, Delete | Normal rights |
| Submit, Cancel, Amend | Document lifecycle rights |
| Report, Export, Print | Output rights |
| Field groups | Sensitive fields (salary, bank account) visible only to some roles |

- **Record restrictions** (like ERPNext User Permissions): limit a user to certain companies,
  branches, departments, warehouses or cashiers. Applied as query filters in every list and as a
  check on every load.
- Checks happen in one place: an `IAuthorizationService` used by every endpoint, and EF global
  query filters for record restrictions. Never rely on the UI hiding a button.

## Naming series

- Pattern per document type and company, e.g. `PCR-.YYYY.-.#####`.
- Counters kept in a table and incremented with `SELECT ... FOR UPDATE` inside the submit or
  insert transaction, so there are no gaps from concurrent users and no duplicates.

## Workflow (approvals)

- Configurable states and transitions per document type: state, allowed role, condition, next
  state, optional "on transition" action (submit, cancel).
- Conditions are simple expressions over document fields (same safe engine as payroll formulas).
- Every transition is logged (who, when, comment).
- Notifications on transition (in-app via SignalR, email, optional SMS/Telegram).

## Audit

- Every change to a document stores a row in `platform.audit_log`: document, field, old value,
  new value, user, time.
- Submitted documents and ledgers are append-only, so history can always be rebuilt.

## Attachments and comments

- Files in S3-compatible storage; private by default; access checked against the parent document.
  Core keeps the files; each module answers which permissions guard its records (`IRecordPermissions`), and an unclaimed record is refused.
- Comments and a timeline on every document.

## Notifications

- Core keeps each user's in-app inbox (step 62). Any module sends to it through `INotifier` (a user by username, or every holder of a
  role, in a company); the user reads only their own (`/api/notifications`). See `docs/requirements/sap-features.md` (9, Alerts) for what is
  and is not built.

## Custom fields

Customers (Sugar, FHC, others) need extra fields without code changes:
- Each document table has a `custom jsonb` column.
- A `custom_field` definition (doctype, key, label, type, options, required, permission group)
  drives validation on the server and rendering in the React form.
- Custom fields can be filtered and shown in lists through generated JSONB indexes.
- Business logic on custom fields is not allowed. If logic is needed, it becomes real code in a
  module.

## Settings and branding

- Per-company settings (accounts defaults, HR settings, etc.) as typed settings records.
- Branding settings (colours, logos, login texts), the same idea as the current `brand_theme` app,
  delivered to the React app at login.

## Background jobs and scheduling

- Hangfire for queued and recurring jobs (daily leave accrual, exchange rate import, reminders).
- A job page in the UI shows progress and failures.

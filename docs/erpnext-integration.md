# ERPNext coexistence

During the move both systems run. Rule: **each kind of record has exactly one master system at a
time.** The other system only reads or receives copies.

## Connector

The connector is **just another plugin** (`erpnext-connector`). It provides the same capabilities
as the Accounting module (`IJournalSink`, `IAccountDirectory`), so Payroll and Cash Management
post to ERPNext without knowing it. When the Accounting module goes live, the connector is
switched off and Accounting provides those capabilities instead, with no change to the other
modules. Installing both at the same time is refused.

`Gebeta.Integrations.ErpNext`, a typed client over the Frappe REST API:
- Auth: API key and secret of a dedicated ERPNext integration user with only the roles it needs.
- Reads: `/api/resource/{doctype}` with filters and paging; `/api/method/...` for whitelisted methods.
- Writes: create and submit Journal Entries (payroll, petty cash), with an idempotency key stored
  in a custom field on the ERPNext side (`gebeta_ref`), so a retry never posts twice.
- Every call logged; failures retried by Hangfire with backoff; a dead-letter page for manual review.

## Who is master, per phase

| Data | Phase 1–3 | From Phase 4 | From Phase 5 |
|---|---|---|---|
| Company, cost centre, accounts | ERPNext → copied to Gebeta | Gebeta | Gebeta |
| Employee | Gebeta (after initial import) → copied to ERPNext | Gebeta | Gebeta |
| Payroll, slips, back payment | Gebeta | Gebeta | Gebeta |
| Journal entries from payroll and cash | Gebeta creates them **in ERPNext** | Gebeta ledger | Gebeta ledger |
| Cashier balances | Gebeta cashier ledger (checked against ERPNext GL daily) | Gebeta cashier ledger | Gebeta cashier ledger |
| Items, stock | ERPNext | ERPNext | Gebeta |

## Sync

- Masters copied from ERPNext: nightly full sync + Frappe Webhooks on change (webhook →
  Gebeta endpoint → queued job that fetches the record).
- Employees copied back to ERPNext as long as ERPNext modules still need them (e.g. expense
  claims before Phase 2 is done).
- A daily reconciliation job compares counts and totals and reports differences.

## Users

Keycloak can be the login for both: ERPNext supports OAuth/Social Login, so users sign in once.

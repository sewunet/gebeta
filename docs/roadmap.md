# Roadmap

ERPNext is replaced **module by module**, never in one big switch ([ADR 0005](adr/0005-incremental-replacement.md)).
Until a module moves, ERPNext stays the system of record for it.

Because every module runs on its own ([plugin-architecture.md](plugin-architecture.md)), each
phase ends with a module that can also be **released and sold on its own** (on Core), not only
used together with ERPNext. Each phase's "done" also requires the module's tests to pass alone,
with its partners, and with all modules.

## Phase 0: foundation

- Repository, CI building Docker images, Docker Compose (PostgreSQL, RabbitMQ, Redis, Keycloak, Seq).
- SharedKernel: `Money`, `Quantity`, `DocumentBase`, results, outbox, handler interfaces.
- Plugin system: module manifest and loader, capability registry with fallbacks, integration
  events (outbox/inbox over RabbitMQ, dead-letter page), outbound journal list,
  React shell slots and `/api/modules`.
- Core (Platform): shared employee / party / item directory, companies, branches, departments, cost centres, users and roles, permissions,
  naming series, audit, attachments, workflow engine, branding.
- React shell: login, layout, `DataTable`, `DocumentForm`, `LinkField`, theming, i18n.
- ERPNext connector (read masters, post journals), see [erpnext-integration.md](erpnext-integration.md).

**Done when:** a sample document can be created, submitted, cancelled and amended with
permissions, audit and naming series, end to end in the React app.

## Phase 1: HR and Payroll (first live module)

- Employee master synced from ERPNext, then mastered in Gebeta.
- Salary components, structures, assignments, tax slabs, formula engine.
- Payroll entry and salary slips; payslip PDF; bank file.
- Salary back payment.
- Payroll journal posted **to ERPNext** through the connector (accounting stays in ERPNext).
- Employee self-service: payslips, leave balance. *(Built: "For me" — my payslips (printable), my leave and applying for it; a user is linked to their employee record and given the Employee role.)*

**Done when:** three payroll months run in parallel with ERPNext give the same net pay, tax and
pension for every employee to the cent, and the payroll journal matches.

## Phase 2: rest of HR

Attendance and devices, leave ledger, promotion/transfer/separation/clearance, training,
appraisal, per diem and expense requests, employee advances.

## Phase 3: Cash Management

Cashiers, the cashier ledger, petty cash requests, automatic top-ups. Balances come from the
module's own cashier ledger from day one. Until Accounting moves, journals go to ERPNext through
the connector plugin; after Phase 4 the Accounting module takes over without changes to Cash
Management.

## Phase 4: Accounting

Chart of accounts, GL, payment ledger, journals, payments, invoices, tax, VAT closing, banking,
budgets, period closing, revaluation, financial reports.

**Cut-over per company** at a fiscal period boundary: opening balances migrated, previous
periods stay in ERPNext (read-only) or are migrated for history.

**Done when:** one company closes a month in Gebeta and the trial balance is signed off.

## Phase 5: Stock, Buying, Selling

Items, warehouses, stock ledger and valuation, reposting, material requests, purchase orders and
receipts, landed costs (built, step 59), framework agreements with suppliers (built, step 61), notification inbox (built, step 62) and alerts from saved conditions (built, step 63; stock below reorder level waits for a reorder-level field), LC, tender and supplier scoring, sales orders and delivery, transport orders.

## Phase 6: Manufacturing, Assets, Projects

BOM, work orders, job cards, workstations (including parallel workstations), maintenance;
assets (borrowing, disposal, sub-assets); project advances and contracts; property sales
agreements.

## Phase 7: switch off ERPNext

Archive ERPNext read-only for history (or fully migrate history), then shut it down.

## Indicative timeline

For a team of about 4–6 developers plus a QA/business analyst. These are planning estimates,
not commitments.

| Phase | Duration |
|---|---|
| 0 Foundation | 3–4 months |
| 1 Payroll | 3–4 months |
| 2 HR | 3–4 months |
| 3 Cash Management | 1–2 months |
| 4 Accounting | 6–9 months |
| 5 Stock, Buying, Selling | 6–9 months |
| 6 Manufacturing, Assets, Projects | 4–6 months |

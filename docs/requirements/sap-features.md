# What to take from SAP: simple features Gebeta lacks

*Business developer and product analyst, 18 November 2026 (Hidar 9, 2019 E.C.). Status: proposal for the product owner.
Follows `competitive-features.md` (Business Central, Odoo, Zoho), whose items 1–5 are built.*

## Recommendation

SAP Business One (for small and mid-size companies) and S/4HANA (for large enterprises) were checked for
features that are **simple, used every month, and missing in Gebeta**. Items already on the earlier list are
left out: recurring journals, credit limits, three-way match, budgets, supplier payment proposals, approval
rules and dunning.

1. **Payroll checks before submit** (Payroll, S). This is SAP's Payroll Control Center idea in small form: a
   fixed set of checks on the month's slips that flags every problem by employee before submit. Examples are
   net pay below zero or below a share of gross, a missing bank account or pension number, and two employees
   with the same bank account. Loans and standing deductions (step 52) make low net pay a real risk.
   The checks sit next to the run comparison (#1 of the earlier list), and the run's Submit asks for a
   confirmation while anything is flagged. That closes the "flagged count on submit" left open there.
2. **Cost centres on postings, and profit and loss by cost centre** (Accounting, Payroll, M). These are SAP's
   distribution rules in their simplest form: one cost centre per posting line, with a default per employee
   and per account. A sugar company runs its estate, factory, workshop and head office as separate cost
   centres, and the current system already records one on every ledger row. Without cost centres, migrated
   history loses that split. The field is already planned in `docs/modules/accounting.md` but not built.
3. **Change history** (Platform, M). This is SAP's Change Log: on employees, salary assignments, parties,
   items, accounts and every document, it shows who changed which field, when, and the old and new value.
   Auditors of a public enterprise ask for it, and the roadmap lists "audit" in Phase 0, but it is not built.

Next: **document flow** on every document (S), then **attachments** (M).

**Not now:**
- **Alerts** (B1 Alerts Management): the notification inbox they need is built (step 62); the alert rules are not.
- **Foreign-currency invoices and revaluation** are a large change (L). They matter once an importing customer
  goes live.
- **Framework agreements** are built (step 61, Buying); tenders (Phase 5) can issue them later. Landed costs are built (step 59).
- **Internal reconciliation** of open items on accounts such as 1400 Employee Advances is worth doing after
  cost centres.

---

## How the candidates were chosen

The method and scale are the same as in `competitive-features.md`:
- **Value** is 1–5.
- **Effort:** S is up to a week, M is one to three weeks, L is more.
- **Score** is value ÷ effort, where S = 1, M = 2 and L = 3.

"Planned" means the design docs describe the feature, but no code exists yet. This was checked against
`src/` on 18 November 2026: there are no cost centres, attachments or change log, and there is one currency.

## Ranked list

| # | Feature | SAP source | Module(s) | Value | Effort | Score | Status in Gebeta |
|---|---|---|---|---|---|---|---|
| 1 | Payroll checks before submit | Payroll Control Center validation rules | Payroll | 5 | S | 5.0 | New |
| 2 | Document flow on every document | S/4HANA process flow; B1 relationship map | All | 3 | S | 3.0 | New |
| 3 | Cost centres on postings, P&L by cost centre | B1 cost accounting, distribution rules | Accounting, Payroll, Cash | 5 | M | 2.5 | Planned |
| 4 | Change history (who changed what) | B1 Change Log | Platform | 5 | M | 2.5 | Planned |
| 5 | Attachments on documents and master data | B1 Attachments tab | Platform | 4 | M | 2.0 | Planned |
| 6 | Landed costs on goods received | B1 Landed Costs | Stock, Buying | 4 | M | 2.0 | Built (step 59), in Stock |
| 7 | Framework (blanket) agreements with suppliers | B1 Blanket Agreements | Buying | 3 | M | 1.5 | Built (step 61), in Buying |
| 8 | Foreign-currency invoices and month-end revaluation | B1 exchange rate differences | Accounting | 4 | L | 1.3 | Planned |
| 9 | Alerts from saved conditions | B1 Alerts Management | Platform | 3 | M | 1.5 | Inbox built (step 62); rules not built |

---

## 1. Payroll checks before submit (S, value 5)

> **Built (step 53).** `GET /api/payroll/runs/{id}/checks` (CSV with `?format=csv`) and a **Checks before
> submit** section on the run page, above the comparison, grouped by check, with each employee linking to
> the slip. Submit asks "N employees flagged; submit anyway?" while anything is flagged, and the API still
> accepts it.
>
> Differences from this note:
> - **No "bank transfer file used" condition:** an employee paid by bank transfer (the default when there is
>   no payroll profile) needs an account. One paid in cash does not.
> - **Low net pay names its cause:** the message lists the deductions other than tax and pension, with the
>   loan or deduction number, for example `Court order (LOAN-2019-00002) 3,500.00`.

**In one line:** after slips are generated, the run page lists every employee who fails a check, grouped by
check. Submit asks for a confirmation while anything is flagged.

**Who has it:** SAP Payroll Control Center runs validation rules over the payroll before it is released. Its
predefined rules include negative gross pay, negative deductions, net pay below a set amount, and employees
without main bank details
([SAP Community: Payroll Control Center best practice](https://community.sap.com/t5/enterprise-resource-planning-blog-posts-by-sap/a-best-practice-to-implement-sap-payroll-control-center-add-on/ba-p/13093487);
[SAP Learning: managing payroll alerts](https://learning.sap.com/courses/sap-successfactors-employee-central-payroll-project-team-orientation/managing-payroll-alerts_e5bf8722-e322-4280-bcca-7efbfedd5e4f)).
Gebeta needs only a fixed set of checks, not a rule designer.

**Why here:**
- The run comparison (#1, built) shows *changes* against last month. It does not catch a problem that was
  already there last month, such as a missing bank account, or a brand-new one, such as a new loan pushing net
  pay to near zero.
- The bank transfer file fails, or pays the wrong person, when bank data is wrong, and a duplicate account
  number is the classic payroll fraud signal.
- The cap on deductions in the Labour Proclamation (believed to be one third of the wage; still to VERIFY) can
  start as a warning before it ever becomes a block.

**Builds on:** `PayrollComparison` (the same run, slips and CSV), `EmployeePayrollProfile` (bank account,
pension number), `EmployeeInfo.Tin`, and slip lines.

**Acceptance criteria**

1. `GET /api/payroll/runs/{id}/checks` (CSV with `?format=csv`) returns each failed check with the employee,
   slip, check, figure and a short message. Checks run live, on the run's draft or submitted slips; nothing is
   stored.
2. The checks, each with a severity:
   - **Error:** net pay below zero.
   - **Warning:** net pay below `Payroll:Checks:MinimumNetShare` of gross (default 33.33%, labelled "VERIFY:
     Labour Proclamation 1156/2019"). The message names the deductions that are not tax or pension, so the
     loan or court order behind it is visible.
   - **Warning:** no bank account on the payroll profile, when the bank transfer file is used.
   - **Warning:** the same bank account number on two or more employees (all of them are listed).
   - **Warning:** no pension number for an employee whose slip has employee pension.
   - **Warning:** no TIN for an employee whose slip has income tax.
   - **Warning:** a base salary of zero, or payment days of zero with gross pay above zero.
3. The run page shows the checks next to the comparison: a count per check, with the employees under it, each
   linking to the slip.
4. Submitting a run with errors or warnings needs a second click: "N employees flagged; submit anyway?". The
   API still accepts the submit, because the checks only advise and blocking is a later customer setting.
5. A test covers every check with one employee who fails it, and an employee who fails none.

**Worked example (illustrative):** run for Hidar 2019, 412 slips.

| Check | Employees | Example |
|---|---|---|
| Net pay below a third of gross | 3 | E-0217: gross 6,000, net 1,450 (Salary advance LOAN-2019-00031: 2,500) |
| Same bank account | 2 | E-0102 and E-0388: 1000123456789 |
| No pension number | 1 | E-0411 (joined this month) |

---

## 2. Document flow on every document (S, value 3)

> **Built (step 56).** `GET /api/documents/{id}/related?companyId&number` asks every installed module
> (`IRelatedDocuments`), and a **Related documents** section appears on 8 document pages.
> - **What each module answers:**
>   - **Accounting:** an invoice's order, its original or returns, and its payments; a payment's invoices; a
>     journal's source; the journal any document posted; the invoices that name an order number.
>   - **Payroll:** a salary slip's additional salaries, loans and back pay; the slips that took a loan's or an
>     additional salary's amount.
>   - **Stock:** a stock entry's order; the receipts and deliveries that name an order number.
>   - **Buying and Selling:** they recognise their order numbers, so a reference becomes a link.
> - **Who sees what:** each related document shows only to a user allowed to read that kind of document.
> - **What is stored:** nothing new. Each module reads the links it already keeps.
>
> Not yet:
> - **Cash movements and bank statement lines:** they have no page of their own, so a journal's cash or bank
>   source is not listed.
> - **Back pay:** it is listed, but without a link.

**In one line:** each document page gets a "Related documents" section that lists the documents before and
after it in the chain, with their status. Examples:
- purchase order → goods received → purchase invoice → payment → journal;
- additional salary or loan → salary slip → payroll journal.

**Who has it:** S/4HANA's process flow shows the link between all the documents of a sales process
([SAP Learning: summarizing the sales and distribution process](https://learning.sap.com/courses/exploring-sap-s-4hana-sales-essentials/summarizing-the-sales-and-distribution-process_e2a16426-665b-47ab-aea4-ea7e8c5f1a6b)).
Business One has the relationship map.

**Why here:**
- Auditors and accountants trace a payment back to its order daily.
- Today that means searching each screen by number.

**Builds on:** the links already stored:
- `order_reference` on invoices, and return-against links;
- payment allocations;
- journal `source_id`;
- additional pay `source_id` and `salary_slip_id`.

**Effort: S.** It needs one read endpoint per module that returns links by document id, and one shared
component on the object page. No new data is stored. Links across modules use capabilities with fallbacks
(ADR 0006), so a missing module simply shows fewer links.

---

## 3. Cost centres on postings and profit and loss by cost centre (M, value 5)

> **Built (step 54).**
> - **Core:** a cost-centre list per company under **Settings → Cost centres**, at `/api/core/cost-centers`.
>   Every company gets **Main** when it is created, and the migration adds Main to existing companies. Main
>   cannot be deactivated. Other modules read the list through `ICostCenterDirectory`.
> - **Ledger:** journal lines and ledger rows have an optional `cost_center_id`. A row that names none belongs
>   to Main, so postings from invoices, cash, stock, banking and opening balances stay valid unchanged and
>   land in Main.
> - **Manual journals:** a cost centre per line. The journal form shows that column once the company has more
>   than Main, and the API refuses another company's cost centre or an inactive one.
> - **Payroll:** the employee's payroll profile has a cost centre. The payroll journal puts salary and
>   employer pension on it, and the payables on none. The employee page has a new **Payment** section for the
>   payment method, bank account, pension number and cost centre. Until now these could only be set through
>   the API.
> - **Reports:** profit and loss and the comparative profit and loss take `?costCenterId=`, and the financial
>   reports screen has a filter. A test checks that the cost centres add up to the whole.
>
> Differences from this note, and what is left:
> - **Not required on every income and expense line:** an empty line counts as Main instead, so no existing
>   posting path had to change.
> - **No account default, and no cost centre yet on invoice lines or cashier movements:** both land in Main
>   until they are added on the same pattern.
> - **No "by cost centre" columns layout** yet: one cost centre at a time, through the filter.
> - **Payroll looks up the cost centre when a slip is submitted:** a profile changed between creating and
>   submitting a slip uses the new cost centre.
> - **Migration:** the tool maps the old cost centres and each employee's payroll cost centre, with the bank details (steps `costcenters` and `profiles`, step 57).

**In one line:**
- **The cost centre list:** each company keeps a short list of cost centres, as a tree. For example: Estate,
  Factory, Workshop, Head office.
- **On postings:** every journal line, invoice line, salary slip and cashier movement carries one.
- **Reports:** profit and loss and the trial balance can be filtered or split by cost centre.

**Who has it:** in SAP Business One, profit centres or departments are defined as cost centres. Distribution
rules then allocate amounts across them, by fixed percentages or by up to five dimensions
([SAP Learning: managing cost accounting in SAP Business One](https://learning.sap.com/courses/handling-accounting-in-sap-business-one/managing-cost-accounting-in-sap-business-one-1)).
Gebeta needs the simplest form: **one** cost centre per line, with a default, and **no** percentage
distribution yet.

**Why here:**
- A sugar company manages its estate (cane growing) and its factory as separate cost pools. Management
  reports and the cost of sugar per tonne depend on that split.
- The current system stores a cost centre on every ledger row, so migrated history must keep it
  (`docs/data-migration.md`).
- `docs/modules/accounting.md` already lists `cost_center_id` on GL rows and "payroll cost by cost centre".

**Defaults:**
- The employee's cost centre (on the payroll profile) for payroll.
- The cashier's default cost centre (already in `docs/modules/cash-management.md`) for cash.
- A default per expense or income account for everything else.
- A line may override the default.

**Acceptance criteria (summary):**
- **Required:** income and expense lines need a cost centre, from the line itself or its default. Balance sheet
  lines do not.
- **Reports:** P&L and the comparative P&L (#3, built) take `?costCenter=`, and P&L gets a "by cost centre"
  layout with one column per cost centre.
- **Payroll journal:** it splits salary expense by the employees' cost centres.
- **Migration:** the migration tool maps the old cost centres and keeps them on history.

**Effort: M.** It adds a column on GL entries and on the lines of each posting document, a master list, and
report filters. Percentage distribution and further dimensions (branch, project) come later, on the same column
pattern.

---

## 4. Change history (M, value 5)

> **Built (step 55).** Every save through a module's database context writes an `audit_change` row, in that
> module's own schema and in the same transaction, for each record marked `IAudited`.
> - **What is recorded:**
>   - "Created" or "Deleted" for a whole record;
>   - for a change, one row per field, with the old and new value;
>   - for a list column, such as lines, only that it changed;
>   - in every case, who did it, taken from the signed-in user, or "system" for event consumers.
> - **What is left out:** the bookkeeping fields (CreatedAt, ModifiedAt and so on).
> - **What is audited:**
>   - every document, through `DocumentBase`, so submit and cancel show as a status change;
>   - companies, employees, parties, items, cost centres, users, accounts and bank accounts;
>   - payroll profiles and salary assignments, which show **in the employee's history**.
> - **Reading it:** `GET /api/history/{id}` collects the rows from every installed module (`IChangeHistory`).
>   It needs `core.history.read`, which Core Administrator, Accountant, Payroll Officer and HR Officer have,
>   and it returns 404 for a record of a company the user cannot access.
> - **Screens:** a **History** section on 13 object pages.
>
> Not yet:
> - **Cashiers** are not audited: their balance changes with every movement and would bury the real changes.
>   This needs a per-field exclusion first.
> - **Field names** show as words built from the code ("Bank account number"), not translated labels.
> - **No retention limit:** rows are kept for good.

**In one line:** a "History" section on every object page lists each change, newest first: date and time, user,
field, old value and new value. Submit and cancel also appear, with the user.

**Who has it:** the SAP Business One Change Log shows, for master records (items, business partners, G/L
accounts) and documents, the date of each change, the changed field, the value before, and the user who made
it ([SAP Business One help: Change Log](https://help.sap.com/saphelp_sbo92/helpdata/en/45/06b9767d720487e10000000a155369/content.htm)).

**Why here:**
- **Auditors:** they of a public enterprise ask "who changed this employee's salary or bank account, and when?"
- **Fraud:** a bank account changed the day before payroll is the classic fraud.
- **Roadmap:** it lists "audit" under the Phase 0 platform.

**Design note:**
- **Where the log is written:** one EF Core save interceptor in the BuildingBlocks writes an `audit_change`
  row per changed property for entities that opt in.
- **Who is recorded:** `ICurrentUser`, already present.
- **Storage:** each module schema has its own table, to keep modules independent.
- **What is read:** `GET .../{id}/history`.
- **What is left out:** secrets and large JSON columns. A list column (lines) is recorded as "lines changed",
  with a count.

**Link to #1:** a payroll check "bank account changed since last month" becomes cheap once this exists.

---

## 5. Attachments (M, value 4)

> **Built (step 58).** Core keeps files for records of every module (`/api/attachments`), with an
> **Attachments** section on 13 object pages.
> - **Storage:** the bytes are in their own table (`core.attachment_content`), so lists never load them
>   and database backups include them with their records.
> - **What is accepted:** PDF, PNG, JPG, WebP, XLSX, DOCX, CSV and TXT, up to 10 MB.
>   - The first bytes must match the type, so a program renamed to `.pdf` is refused.
>   - The content type is Gebeta's own, never the browser's.
>   - Downloads are always served as a download with `nosniff`, never shown inside Gebeta.
> - **History:** adding and removing a file show in the record's history.
> - **Permissions:** `core.attachments.read` and `core.attachments.manage`, granted to the roles that keep
>   records.
>
> - **A file is as private as its record:** every module answers which permissions guard its records
>   (`IRecordPermissions`, like related documents). Listing and downloading need the record's read
>   permission, adding and removing its write permission (an invoice's or an order's create, a master's
>   manage), on top of the attachments permission, and the record must belong to the company named.
>   - A record no installed module claims (or one the user may not reach) is refused as not found.
>   - A new kind of record gets attachments by answering it in its module.
>
> Not yet:
> - **No virus scanning.**
> - **No object storage:** files live in the database, which is fine at a few GB.

**In one line:** files (PDF, images) can be attached to any document or master record. Examples: the supplier's
invoice scan, the court order behind a standing deduction, a contract, an ID copy.

**Who has it:** SAP Business One has an Attachments tab on marketing documents, business partners and items.
Files are kept in a folder that the administrator sets.

**Why here:**
- Auditors check supporting documents behind each voucher.
- Today the paper sits in a binder, separate from the record.

**Effort: M.** It needs file storage behind an interface (a local volume first, then object storage), size and
type limits, a virus scan hook, and permission checks that follow the record's own permission. Printed
vouchers (#2, built) can list their attachments.

---

## 6–9 in brief

- **Landed costs (M, value 4):**

  > **Built (step 59).** Stock keeps a **Landed Cost** document (`LCV-<FY>-#####`, `/api/stock/landed-costs`:
  > create, get, list by receipt, submit, cancel) against one submitted stock receipt, shown as a **Landed costs**
  > section on the receipt's page.
  > - **Charges:** each has a description, an amount, a basis (value, quantity, weight or volume) and a credit
  >   account. Each charge is shared across the receipt's lines by its own basis, to the cent; the rounding
  >   leftover goes to the line with the largest share, so the shares always add up to the charge.
  >   Weight and volume are typed on the landed cost for each line (any one unit; only the shares matter). Value
  >   is the value the line was received at.
  > - **Submitting** adds each line's share to its receipt ledger row and to the receipt line's value, then
  >   reposts everything after it with the existing reposting (step 30): goods already issued or transferred are
  >   repriced, each repriced entry gets its adjustment journal, FIFO layers and the moving average follow. All in
  >   one transaction under the item and warehouse locks. Accounting gets one journal for the charges: debit the
  >   stock account, credit the charge accounts. Cancelling takes it all back the same way.
  > - **Decision for the owner to confirm:** the charges are credited to account **2150** (Stock Received But Not
  >   Billed) unless another account is given. The forwarder's or customs invoice, entered in Accounting with its
  >   line on 2150, then clears it, as for goods received (step 31). So no link to Buying or Accounting was
  >   needed, and Stock and Buying still run alone. A customer who pays duty from cash or a bank can name that
  >   account instead.
  > - **Rules:** the receipt must be submitted; the landed cost cannot be dated before the receipt or in the
  >   future; a receipt with a posted landed cost cannot be cancelled until it is cancelled; a basis with nothing
  >   to share by (no weights given) is refused.
  > - **Permissions:** `stock.landedcosts.read`, `.create`, `.submit` (Store Keeper has `stock.*`).
  > - **Not yet:**
  >   - one landed cost across several receipts, or a purchase invoice's lines pulled in as charges;
  >   - weight and volume held on the item (typed each time today);
  >   - editing a draft (create a new one);
  >   - a landed cost list page, print voucher and link in "related documents";
  >   - the journal is dated on the landed cost's date, while the stock ledger takes the cost from the
  >     receipt's date (reports as of a date between the two differ from the general ledger until the later date);
  >   - customs duty and excise as their own tax types (they are plain charges).
  > - **Screen:** not yet opened in a browser; it builds and type-checks only.

  - What: allocate freight, customs duty, clearing and LC bank charges to the items of a goods receipt, by
    value, quantity, weight or volume, so item cost includes them.
  - In SAP, the landed cost document is based on the goods receipt and updates the item unit cost
    ([SAP Business One help: landed costs](https://help.sap.com/saphelp_sbo91/helpdata/en/44/f8c616445241aae10000000a114a6b/content.htm)).
  - Why it matters: Ethiopian imports carry duty, excise, surtax and large LC charges.
  - Belongs with Phase 5 "LC".
- **Framework (blanket) agreements (M, value 3):**

  > **Built (step 61).** Buying keeps a **Blanket Agreement** document (`BPA-<FY>-#####`, `/api/buying/agreements`: create, get,
  > list (optionally by supplier), submit, cancel) and purchase orders take an optional `agreementId` to call off against it.
  > See "Framework agreements" in `docs/modules/buying.md` for the rules and the decisions to revisit.
  - What: an agreement with a supplier for a planned quantity or amount at agreed prices over a period.
    Purchase orders call off against it, and it shows what remains.
  - SAP B1 has this as blanket agreements
    ([SAP Business One help: blanket agreements](https://help.sap.com/saphelp_sbo92/helpdata/en/b6/1e853cc4b14d758ee9f6f0e2ebee54/content.htm)).
  - Fits public procurement framework contracts; belongs with Phase 5 "tender".
- **Foreign currency and revaluation (L, value 4):**
  - What: invoices in USD or EUR, payments at the day's rate with realised difference, and month-end
    revaluation of open items with unrealised difference, reversed next month.
  - SAP B1 does this with its exchange rate differences run
    ([SAP Learning: working with currencies](https://learning.sap.com/courses/handling-accounting-in-sap-business-one/working-with-currencies-1)).
  - Already designed in `docs/modules/accounting.md` ("Multi-currency").
  - Why it matters: since the birr was floated in 2024, revaluation of foreign-currency payables matters for
    importers.
- **Alerts (M, value 3):**
  - What: a saved condition (overdue invoices, a loan ending, stock below reorder level, a contract
    expiring) that notifies chosen users on a schedule.
  - SAP B1 builds alerts on saved queries; they notify but do not block
    ([SAP Learning: using alerts](https://learning.sap.com/courses/implementing-sap-business-one/using-alerts)).
  - Needs an in-app notification inbox first.

  > **Built (step 62): the inbox only, not the alerts.** Core keeps a per-user inbox (`core.notification`) and
  > any module can send to it through `INotifier` (`Gebeta.Application`, like `IRecordPermissions`).
  > - **Sending:** `NotifyUserAsync(companyId, username, message)` and `NotifyRoleAsync(companyId, roleName, message)`.
  >   A message has a title, an optional body, an optional link to a record (kind and id, the kind names the
  >   document flow uses) and a severity (Info, Warning, Critical). A role message is stored once per holder.
  > - **Deny by default:** an unknown, inactive or no-access person gets nothing (the user must be assigned to the
  >   company, or hold `core.companies.access-all`). Sending is best effort: a failure is logged and swallowed, so
  >   the business action that sent it is never lost. A module without a notifier uses `NoNotifier`.
  > - **API** (any signed-in user, no permission, always the caller's own): `GET /api/notifications?unread&page&pageSize`
  >   (newest first, at most 100 a page; returns total and unread), `GET /api/notifications/unread-count`,
  >   `POST /api/notifications/{id}/read`, `POST /api/notifications/read-all`. Another user's notification is
  >   answered as not found, for everyone including administrators. Notifications of a company the user may no
  >   longer work in are hidden.
  > - **Web:** a bell with the unread count in the shell bar (checked every minute) and an inbox page at `/inbox`
  >   that opens the record linked.
  > - **One real producer:** when someone other than the creator cancels a purchase order, Buying tells its creator
  >   (a Warning linking to the order).
  >
  > **Not built:** alert rules (saved conditions on a schedule) and the scheduler to run them; other producers
  > (approval steps, overdue invoices, loans ending, stock below reorder level, contracts expiring); delivery by email,
  > SMS or push; per-user preferences or muting; text in the reader's language (messages are stored as written by
  > the sender, English for now); retention (nothing is deleted, so old read messages should be purged one day);
  > live push (the bell polls); deleting a notification; the inbox does not yet follow a role message to
  > someone assigned the role later.

## What SAP does that Gebeta should not copy

- **User-written SQL queries** behind alerts and approvals (B1 Query Manager) are powerful, but they bypass
  permissions and module boundaries. Gebeta keeps fixed, tested checks and reports.
- **Five dimensions and percentage distribution rules** from day one: one cost centre covers the sugar
  company's need. More can follow on the same column pattern.

## VERIFY

- **Deduction limit:** the one-third limit on deductions (Labour Proclamation 1156/2019; article to confirm
  with a labour expert) before check #1 can ever block.
- **Cost centres:** the customer's cost centre list and which accounts need one, from the current chart.

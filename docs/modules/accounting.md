# Accounting module

> **Built so far** (`src/Modules/Accounting`): Ethiopian chart of accounts template (set up on the
> `CompanyCreated` event or by `POST /api/accounting/companies/{id}/chart`), manual journal entries
> (`JV-<FY>-#####`) with submit/cancel, append-only general ledger with reversal rows, trial balance, and the
> payroll journal: `SalarySlipSubmitted` from Payroll arrives over RabbitMQ and is posted (earnings → 6110,
> employer pension → 6120 and 2230, income tax → 2220, pension → 2230, other deductions → 2240, net → 2210);
> `SalarySlipCancelled` reverses it. Account mapping is fixed to the template codes for now; a mapping
> screen per company is planned.
>
> **Invoicing** (step 17): customers and suppliers are Core parties (`/api/core/parties`). Sales and purchase
> invoices (`SINV-` / `PINV-<FY>-#####`, `/api/accounting/invoices`) compute VAT 15% (sales: when the company
> is VAT-registered; purchases: when the supplier is; lines can be VAT-exempt) and withholding 2% on goods above
> ETB 10,000 and services above ETB 3,000 (purchases: when the company is a withholding agent, the default;
> sales: when the customer withholds). Submitting posts Dr 1200 (payable amount) + Dr 1260 (withheld) / Cr income
> + Cr 2300 for sales, and Dr expense + Dr 1250 / Cr 2100 + Cr 2400 for purchases; cancelling reverses it.
> Payments (`PAY-<FY>-#####`, `/api/accounting/payments`) settle one invoice up to its outstanding amount
> (bank 1120 by default); an invoice with payments cannot be cancelled. Reports: `/reports/vat` (output, input,
> net payable) and `/reports/withholding` (withheld from suppliers, withheld by customers). Rates and thresholds
> are configuration (`Accounting:*`). Not yet: partial allocation of one payment across invoices, refunds,
> withholding receipts, TOT.
>
> **Corrections and cut-over** (step 21): `POST /invoices/{id}/return` makes a credit note (`CN-`, sales) or debit
> note (`DN-`, purchases) for some or all of a submitted invoice's lines: VAT as on the original, withholding at
> the same rate when the original had any (no threshold), journal with sides swapped, and it reduces what is
> still owed, so it cannot exceed the outstanding amount. VAT and withholding reports count returns negative.
> `POST /payments/{id}/cancel` reverses a payment's journal and puts the amount back on the invoice.
> `POST /invoices/opening` brings in an invoice still open at cut-over for its outstanding amount (it may already
> be overdue); it posts nothing, since the total is in the opening balances journal, but can be paid as usual.
> `/reports/ageing?kind=Sales|Purchase&asOf=` buckets open amounts per customer or supplier (not due, 1–30,
> 31–60, 61–90, over 90 days past due), counting only payments and returns dated by then.
>
> **Month close and reports** (step 18): `POST /api/accounting/periods/close` closes the month containing a date:
> one of the 12 Ethiopian fiscal periods for companies on the Ethiopian fiscal year (Nehase takes Pagume), else the
> calendar month. Months close in order, only with no draft journals or invoices left in them, and the closing
> keeps the trial balance totals and the month's profit (the sign-off record). The end of the latest closed
> month is the **lock date**: `AccountingDbContext` refuses any ledger row dated on or before it, whatever
> wrote it (journals, invoices, payments, payroll and cash journals; a message from another module into a
> closed month is retried and dead-lettered). `POST /periods/reopen` reopens the latest closed month with a
> reason. Reports: `/reports/profit-and-loss`, `/reports/balance-sheet` (profit not yet closed to retained
> earnings shown in equity) and `/reports/general-ledger` (one account, opening and running balance).
>
> **Cut-over and year end** (step 19): `POST /opening-balances` posts the old system's trial balance as one
> balanced journal (source `Opening`), usually dated the day before the company starts in Gebeta.
> `POST /years/close` closes the fiscal year containing a date (Hamle–Sene, or January–December): one journal
> (source `YearEnd`) dated the year's last day brings every income and expense account to zero and puts the
> profit or loss in retained earnings (3200). The year's last month must still be open; close it afterwards.
> Cancelling the year-end journal reopens the year. Profit and loss leaves year-end journals out, so it still
> shows the year's result. Not yet: open invoices per customer and supplier at cut-over, comparative columns.

The core of the system. Every other money-moving module posts here.

## Scope

| Area | Contents |
|---|---|
| Masters | Chart of accounts (tree), account types, cost centres, accounting dimensions (branch, project, cashier), fiscal years, currencies |
| Ledger | General ledger, payment ledger (open receivable/payable per invoice) |
| Documents | Journal Entry, Payment Entry, Sales Invoice, Purchase Invoice, Debit/Credit Notes, Exchange Rate Revaluation, Period Closing Voucher |
| Tax | Tax templates (VAT 15%, withholding 2%/3%, TOT), inclusive/exclusive, per item or per document; VAT closing (current custom `vat_closing`) |
| Budget | Per account / cost centre / project, monthly distribution, stop or warn |
| Banking | Bank accounts, bank statement import, reconciliation, reversal log (current custom `bank_reconciliation_reversal_log`) |
| LC | Letters of credit linked to purchase orders, receipts, invoices, payments (current `lc_master` and related tables) |
| Reports | Trial balance, balance sheet, P&L, cash flow, general ledger, AR/AP ageing, VAT reports |

## The ledger

```
accounting.gl_entry   (append-only)
  id, company_id, posting_date, account_id, party_type, party_id,
  debit, credit,                         -- company currency
  debit_in_account_currency, credit_in_account_currency, account_currency,
  cost_center_id, branch_id, project_id, cashier_id,   -- dimensions
  voucher_type, voucher_id, voucher_no, voucher_line_id,
  is_reversal, reverses_entry_id, created_at, created_by
```

Rules:
1. Posting goes through one service: `ILedgerPostingService.Post(IReadOnlyList<GlLine>)`.
   It checks: debits = credits per voucher (company currency), accounts are leaf and active,
   the period is open, required dimensions are present, party is given for receivable/payable.
2. **Lines are not merged when they differ in any dimension.** (ERPNext merges lines with the same
   account and cost centre; `cash_management` had to work around that so a transfer between two
   cashiers on one cash account did not become one zero line.)
3. Cancel writes mirror rows with `is_reversal = true` dated on the original posting date (or the
   cancel date if the period is closed and the user chooses so).
4. Other modules never post to the GL directly. They publish `JournalRequested`; Accounting
   validates and posts it (see below). Rules that need an immediate answer stay in the other
   module, e.g. the cashier overdraw check runs on Cash Management's own cashier ledger.

## Journals from other modules

```
Payroll / Cash / Stock / Manufacturing / HR
   └─ JournalRequested { sourceModule, sourceDocument, company, postingDate, lines[ accountRef,
                         debit, credit, party, dimensions ], idempotencyKey }
Accounting
   ├─ maps accountRef → account (account mapping table per module, see below)
   ├─ validates (balanced, open period, leaf accounts, dimensions)
   ├─ posts a Journal Entry of type "Module Posting", linked to the source document
   └─ publishes JournalPosted { idempotencyKey, journalNo }  or  JournalRejected { reason }
```

- The same `idempotencyKey` never posts twice. A cancel in the source module sends a
  `JournalReversalRequested`, which posts the reversal.
- Rejected journals show in the source module with the reason (for example "period closed"),
  and can be resent after fixing.
- **Account mapping**: modules refer to accounts by a code (`accountRef`). When Accounting is
  installed, the mapping screen links each code to a real account, and the UI turns code fields
  into account pickers. Unmapped codes are rejected with a clear message.
- Month-end close is blocked while any journal for that period is Pending.

## Standalone and plugged in

**Alone** (on Core): full accounting. Invoices use Core parties and items; everything else works
as described above.

**When plugged in with:**

| Module | Adds |
|---|---|
| Payroll | Payroll journals arrive automatically; payroll cost by cost centre |
| HR | Employees as parties for advances, per diem and expense claims; budget checks on per diem |
| Cash Management | Cashier dimension on cash accounts; cash movements arrive as journals; daily cashier-vs-GL reconciliation |
| Stock | Perpetual inventory: stock value journals and reposting adjustments; stock accounts per warehouse |
| Manufacturing | WIP, operating cost absorption and variance journals |

Accounting also **provides** the capabilities `IJournalSink`, `IAccountDirectory` and `IBudgetCheck`
to the other modules.

## Payment ledger

`accounting.payment_ledger_entry` tracks each invoice's outstanding amount, so allocation,
partial payments, advances and ageing are fast and do not need to scan the GL.

## Multi-currency

- Each document has `currency` and `exchange_rate`; lines store both currencies.
- Payments at a different rate than the invoice post the difference to Exchange Gain/Loss.
- **Exchange Rate Revaluation** (month end): for open foreign-currency receivable, payable and bank
  balances, compute the new company-currency value and post the unrealised gain/loss; reverse on
  the first day of the next period.

## Period closing

- Accounting periods can be closed per company and per module (e.g. close stock but not payroll).
- Period Closing Voucher moves income and expense balances into retained earnings per fiscal year.
- Posting into a closed period is refused for everyone except a role "Accounts Manager (closed
  period)", and is logged.

## Budgets

- Checked at Material Request, Purchase Order and on GL posting.
- Actions per budget: Stop, Warn, Ignore. Monthly distribution optional.

## Tests that must exist before go-live

- Every document type: post, cancel, amend, and the GL nets to zero after cancel.
- Rounding: invoices with many lines and tax round to the cent and still balance.
- Revaluation and its reversal.
- Trial balance equals the sum of GL per account for random data (property-based test).
- Migration check: trial balance per company per month equals ERPNext's.

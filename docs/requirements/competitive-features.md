# Simple features to adopt next: what leading ERPs do that Gebeta does not yet

*Business developer and product analyst, 18 November 2026 (Hidar 9, 2019 E.C.). Status: proposal for the product owner.*

## Recommendation

Build these three first. All are small, they improve modules that already exist, and they need no new
infrastructure.

1. **Payroll comparison before submit** (Payroll, S). After slips are generated, show what changed against
   last month, employee by employee and component by component, and flag big jumps, new joiners and missing
   employees. It runs every month for every factory's payroll and catches typing mistakes before birr reach
   the bank. It also makes the parallel run against the old system faster, which is the roadmap's Phase 1
   exit test.
2. **Printable payment, receipt and journal vouchers with the amount in words, in Amharic and English** (Accounting and Cash, S).
   Ethiopian finance offices, and public enterprises above all, file a signed paper voucher for every payment.
   Gebeta can print a withholding receipt and a payslip, but **it cannot print a voucher today**. The
   amount-in-words function is already planned in `docs/ethiopia.md` §5.
3. **Comparative columns on profit and loss and the balance sheet** (Accounting, S). Add "previous period"
   and "same period last year" columns, with the change in birr and in percent. The monthly report to the
   board and to the Group, and the annual IFRS statements, need these columns. Today accountants copy two
   reports into Excel to compare them. The gap is already noted in `docs/modules/accounting.md` ("Not yet: …
   comparative columns").

Next in line are a **statement of account per customer or supplier** (S) and **employee loans and standing
deductions with a running balance** (M). Loans have the highest value of the medium items, so start them
straight after the top three.

**Not now:** approval rules by amount (they need the workflow engine, which isn't built yet), automatic
purchase orders from reorder levels (they need material requests), emailed dunning letters, MoR e-invoicing,
and bank API integrations. Each one needs new infrastructure or an outside party.

---

## How the candidates were chosen

- **Already built, so excluded:** checked against `docs/roadmap.md`, `docs/custom-features-inventory.md`,
  `docs/modules/*.md`, `docs/ethiopia.md`, the "Built so far" paragraph in `docs/frontend.md`, every screen in
  `web/src/app/apps.ts`, and the API endpoints in `src/Modules/*/Gebeta.*.Presentation`. Examples of what is
  excluded: one payment for several invoices, bank CSV import, ageing, VAT and withholding CSV files, back pay,
  and additional salary in bulk.
- **Value (1–5):** how often it is used (daily or monthly), whether law or audit requires it, and how much
  manual work or how many mistakes it removes for a company like a sugar factory: thousands of workers, a
  public enterprise, audited, and seasonal (crushing season).
- **Effort:** S is up to about a week for one developer, on existing tables and screens. M is one to three
  weeks, with a new table or a cross-module capability. L is more than that.
- **Score** = value ÷ effort, where S = 1, M = 2 and L = 3.

## Ranked list

| # | Feature | Module(s) | Value | Effort | Score |
|---|---|---|---|---|---|
| 1 | Payroll comparison with last month before submit | Payroll | 5 | S | 5.0 |
| 2 | Printable vouchers with amount in words (Amharic/English) | Accounting, Cash, SharedKernel | 4 | S | 4.0 |
| 3 | Comparative columns on P&L and balance sheet | Accounting | 4 | S | 4.0 |
| 4 | Statement of account per customer / supplier | Accounting | 4 | S | 4.0 |
| 5 | Employee loans and standing deductions with balance | Payroll | 5 | M | 2.5 |
| 6 | Reorder level per item and warehouse, with a "below reorder level" list | Stock | 3 | S | 3.0 |
| 7 | Customer credit limit warning | Selling, Accounting | 3 | S | 3.0 |
| 8 | Copy document (invoice, order, journal) | Accounting, Buying, Selling | 3 | S | 3.0 |
| 9 | Recurring journals and invoices (due list, one click) | Accounting | 4 | M | 2.0 |
| 10 | Purchase invoice check against order and goods received (three-way match) | Buying, Accounting, Stock | 4 | M | 2.0 |
| 11 | Budget vs actual report (per account and cost centre, monthly) | Accounting | 4 | M | 2.0 |
| 12 | Suggested supplier payments by due date | Accounting | 3 | M | 1.5 |

The list is ordered by score. Within a tie, the item with more risk behind it comes first: #6–8 rank below
#4, and #5 is placed above #6–8 because of its value of 5.

---

## Top 3 in detail

### 1. Payroll comparison with last month before submit

> **Built (step 48).** `GET /api/payroll/runs/{id}/comparison` (CSV with `?format=csv`), and the
> "Compare with last month" section of the payroll run screen. `PayrollComparisonTests` checks the worked
> example below: every employee's figures and both months' totals, which equal the statutory reports.
> Differences from this specification:
> - **Criterion 11 is deferred:** the run does not store its flagged count when submitted, because that needs a
>   schema change.
> - **Criterion 7:** "left on <date>" is read from the employee's leaving date.
> - **No "group by department" yet:** Gebeta has no departments.

**In one line:** after **Generate**, the payroll run shows each employee's gross, tax, pension and net against
the previous month, gives totals per component, and flags changes above a threshold, new employees and
missing employees, all before **Submit**.

**Who has it:**
- Zoho Payroll has a comparison report, by employee or by component, against the previous pay run
  ([Zoho Payroll: comparison report](https://www.zoho.com/in/payroll/kb/employer/pay-runs/comparison-report.html)).
- SAP HCM's Wage Type Reporter has a "payroll comparison" option that shows the amount and percent difference
  between two periods ([guru99: SAP Wage Type Reporter](https://www.guru99.com/what-is-wage-type-reporter.html);
  [SAP Help: Wage Type Reporter H99CWTR0](https://help.sap.com/saphelp_46C/helpdata/EN/25/ba05bbaa0511d295e200a0c9306433/content.htm)).
- ERPNext only has a Salary Register to export and compare by hand
  ([Frappe docs: HR reports](https://docs.frappe.io/erpnext/user/manual/en/human-resources-reports)).

**Why it matters here:**
- A sugar factory's payroll officer runs payroll every month for several thousand permanent and seasonal
  workers.
- Overtime and deductions arrive as pasted spreadsheets (additional salary). One extra zero is easy to miss
  among 3,000 slips, and once the bank transfer list has gone to CBE the money is hard to recover.
- The comparison turns "check 3,000 slips" into "check the 40 that changed".
- It also serves the Phase 1 exit test in `docs/roadmap.md` (three parallel months must match to the cent).
  Slips migrated as history (`IsHistory`, `docs/modules/payroll.md`) become "last month", so the first Gebeta
  month is compared automatically with the last month paid in the old system.

**Builds on:**
- `PayrollRun` with its create → generate → submit steps and its `Issues` list
  (`src/Modules/Payroll/Gebeta.Payroll.Domain/.../PayrollRun.cs`, `PayrollRunDto` in
  `Gebeta.Payroll.Contracts/Payroll.cs`).
- The run screen `web/src/pages/payroll/Run.tsx`.
- Submitted slips and their lines.
- Additional salary and back payment entries, which supply the reason for a change.
- The shared `CsvFile` helper.

**Effort: S.** It is one read-only query over two months of slips that already exist, plus one table on the
run page and a CSV download. No new tables are needed: the threshold is configuration, `Payroll:*`.

**User story:** As a payroll officer, after I generate this month's slips I want to see who is paid
differently from last month and why. Then I can correct mistakes before I submit the run and send the bank
list.

**Acceptance criteria**

1. After **Generate**, the run page shows a **Compare with last month** section. "Last month" is the company's
   previous payroll period, the one ending the day before this period starts.
2. For each employee with a slip in either month it shows: employee number and name, gross, income tax,
   employee pension and net for both months, the difference in birr and in percent, and a status. The status
   is *Changed*, *New* (no slip last month), *Missing* (a slip last month, none this month) or *Same*.
3. By default only *Changed*, *New* and *Missing* rows are shown. A toggle shows all rows.
4. A row is **flagged** when its net pay changes by more than the threshold. The default is 10%, set by the
   company in `Payroll:ComparisonThresholdPercent`. *New* and *Missing* are always flagged.
5. Each changed row lists the reasons that can be known from the data:
   - additional salary documents in the month (`ADS-` number and amount);
   - back pay entries;
   - payment days different from last month;
   - a new salary structure assignment.

   If none of these applies, the row says "no recorded reason".
6. A totals block shows, for both months: headcount, total gross, income tax, employee pension, employer
   pension and net, plus the differences. The totals equal the sum of the slips. A test checks this against
   the income tax and pension reports for the same months.
7. A *Missing* employee who separated is shown as "left on <date>" once HR records separations. Until then
   the row is flagged so a person can check it.
8. Only submitted slips count for last month. This month uses the run's generated (draft) slips, and history
   slips count as submitted.
9. **Download CSV** gives the same rows (UTF-8 with BOM, like the other payroll reports).
10. Viewing needs `payroll.runs.read`. Salary amounts are not shown to anyone without it.
11. Submitting is not blocked by flags (it is a warning tool). The run's audit record stores how many rows
    were flagged when it was submitted.

**Worked example.** Hidar 2019 (10 Nov – 9 Dec 2026) is compared with Tikimt 2019 (11 Oct – 9 Nov 2026).
Tax uses the current table in `docs/ethiopia.md`. Pension is 7% employee and 11% employer, on basic salary
earned.

| Emp. | Tikimt: gross / tax / pension / net | Hidar: gross / tax / pension / net | Δ net | Status and reason |
|---|---|---|---|---|
| E-101 | 8,000 / 1,150 / 560 / 6,290 | 9,200 / 1,450 / 560 / 7,190 | +900 (+14.3%) | Changed, flagged: overtime ADS-2019-00031 +1,200 |
| E-102 | 12,000 / 2,250 / 840 / 8,910 | 6,000 / 700 / 420 / 4,880 | −4,030 (−45.2%) | Changed, flagged: payment days 30 → 15 (unpaid leave) |
| E-103 | 8,000 / 1,150 / 560 / 6,290 | 20,000 / 4,950 / 560 / 14,490 | +8,200 (+130.4%) | Changed, flagged: overtime ADS-2019-00031 +12,000 (**typing error, should be 1,200**) |
| E-104 | none | 5,000 / 500 / 350 / 4,150 | +4,150 | New |
| E-105 | 8,000 / 1,150 / 560 / 6,290 | none | −6,290 | Missing |
| E-106 | 3,500 / 225 / 245 / 3,030 | 3,500 / 225 / 245 / 3,030 | 0 | Same (hidden by default) |

How the numbers are worked out:
- E-101 Hidar: 9,200 × 25% − 850 = 1,450 tax; 9,200 − 1,450 − 560 = 7,190.
- E-103 Hidar: 20,000 × 35% − 2,050 = 4,950 tax.
- E-102 Hidar: 6,000 × 20% − 500 = 700 tax.

Totals:

| | Tikimt | Hidar | Δ |
|---|---|---|---|
| Headcount | 5 | 5 | 0 (1 new, 1 missing) |
| Gross | 39,500 | 43,700 | +4,200 |
| Income tax | 5,925 | 7,825 | +1,900 |
| Employee pension | 2,765 | 2,135 | −630 |
| Employer pension (11%) | 4,345 | 3,355 | −990 |
| Net | 30,810 | 33,740 | +2,930 |

The officer sees E-103 at +130%, corrects the additional salary to 1,200 (cancel and re-enter before the slip
is submitted), regenerates, and E-103 drops back to +900.

**Edge cases**
- **First month in Gebeta without migrated history:** show "no previous month to compare" and no flags.
- **Previous month's run not submitted or cancelled:** use only submitted slips. If there are none, treat it
  as the first month.
- **Two slips for one employee in a month** (for example a final settlement): add them together.
- **Back pay paid this month:** its entries are shown as a reason. The net jump is expected but still flagged
  if it is above the threshold.
- **Pagume:** when Pagume falls in the Nehase period (`docs/ethiopia.md` §1), compare Nehase-with-Pagume
  against Hamle as they are. More payment days are a normal reason, not an error.
- **Seasonal workers:** many *New* rows at the start of the crushing season are expected. A "group by
  department" option keeps the list readable.
- **Employee moved between companies:** within one company they count as New or Missing. The other company's
  run shows the other side.
- **Regenerating the run** recalculates the comparison. Nothing is stored except the flag count when the run
  is submitted.
- **Previous month negative or zero net** (a full recovery): show the percentage as "n/a" and flag the row.

---

### 2. Printable vouchers with the amount in words (Amharic and English)

> **Built (step 49).**
> - **Amount in words:** `AmountInWords` in SharedKernel, tested on every boundary and worked example below,
>   and served once by `GET /api/core/amount-in-words`.
> - **Vouchers:** journal, payment/receipt and cashier movement vouchers, with a "Print voucher" link on the
>   journal page, each invoice payment and the cashier ledger.
>
> Differences from this specification:
> - **One endpoint instead of a field on each DTO:** a single source for every module, payslips later included.
> - **Petty cash:** an approved request prints from the cashier ledger movement it pays out, because the
>   request has no date of its own.
> - **Not built yet:**
>   - logging each print to the audit trail (criterion 7: there is no audit trail yet);
>   - filling "prepared by" with the submitter (the documents do not carry who submitted them);
>   - the logo;
>   - a test of the multi-page A4 layout.
> - **Style:** English always ends "only"; Amharic adds "ብቻ" only when asked; "አንድ መቶ" and "አንድ ሺህ" are written
>   with "one". **VERIFY** this style with the customer.

**In one line:** a print layout for payments (`PAY-`), cashier receipts and payments (`CR-`, `CP-`), petty
cash requests (`PCR-`) and journals (`JV-`). It has the company header, the Ethiopian date, the lines and
accounts, the total in figures and **in words in Amharic and English**, and signature boxes (prepared by,
checked by, approved by, received by).

**Who has it:**
- ERPNext stores an `in_words` field on invoices and journals and offers `money_in_words` for print formats.
  It is English only, and its language and currency problems are known
  ([Frappe forum: money_in_words](https://discuss.frappe.io/t/how-to-use-frappe-utils-money-in-words/71051);
  [Frappe docs: centavo in words](https://docs.frappe.io/erpnext/centavo-being-printed-in-words-for-usd)).
- SAP Business One keeps the amount in words on checks, and partners add it to print layouts
  ([SAP Community: amount in words](https://answers.sap.com/questions/2313241/amount-in-words.html)).
- Odoo prints the amount in words on its check layouts, and local add-ons extend it to other documents
  ([Odoo Apps: Jordan check printing](https://apps.odoo.com/apps/modules/18.0/l10n_jo_check_printing);
  [Odoo Apps: amount in words](https://apps.odoo.com/apps/modules/19.0/zehntech_amount_in_words)).
- None of them does Amharic well. That gap is what sets Gebeta apart.

**Why it matters here:**
- Every payment in an Ethiopian finance office, and above all in a public enterprise, ends as a signed paper
  voucher filed with its supporting documents for the auditor.
- Cheques and vouchers carry the amount in words so the figure cannot be altered.
- Cashiers and accountants print vouchers **daily**. Today Gebeta records the payment but the voucher has to
  be typed again in Word or Excel. That is double entry, and the amount on paper can differ from the books.

**Builds on:**
- The print pattern of `web/src/pages/accounting/WithholdingReceipt.tsx` and `MyPayslips.tsx` (a `no-print`
  toolbar and `window.print()`).
- `formatMoney` and the Ethiopian date in `web/src/lib/ethiopian`.
- The `EthiopianDate` long form ("ኅዳር 9 ቀን 2019") in `src/BuildingBlocks/Gebeta.SharedKernel/Ethiopia/EthiopianDate.cs`.
- The existing payment, cashier transaction, petty cash and journal detail endpoints.
- The plan in `docs/ethiopia.md` §5 ("Amount in words … planned (SharedKernel)").

**Effort: S.** It is one pure function per language, tested with a table of examples, plus one shared voucher
layout component used by four detail pages. No new data is stored: the words are computed when printing.

**User story:** As a cashier or accountant, I want to print a voucher for a payment I have just recorded, with
the amount in Amharic and English words and signature boxes. Then I can get it signed and file it without
typing it again.

**Acceptance criteria**

1. Submitted payments, cashier receipts and payments, approved petty cash requests and submitted journals have
   a **Print voucher** button. Drafts print with a "DRAFT" watermark. Cancelled documents print with
   "CANCELLED".
2. The voucher shows:
   - the company name, TIN and logo (branding);
   - the voucher type and number;
   - the date in the user's calendar, with the other calendar in brackets;
   - the party (name and TIN) and the bank or cashier;
   - the lines (account code and name, cost centre, debit and credit for journals; invoices settled for
     payments);
   - the total in figures;
   - the total **in words in Amharic and in English** (both, whatever the UI language);
   - four signature boxes, with "prepared by" filled with the user who submitted it;
   - the print date and the user who printed it.
3. The amount in words follows these rules:
   - birr and santim, 2 decimals, rounded as the document is;
   - "zero birr" for 0;
   - santim alone when there are no birr;
   - up to 999,999,999,999.99;
   - foreign-currency documents use the currency's own names (USD: dollars and cents).
4. The function `AmountInWords.Amharic(decimal, currency)` / `.English(...)` lives in SharedKernel, so
   Payroll can later put net pay in words on the payslip. It is also exposed as a field on the detail DTOs, so
   the web app does not reimplement it. Unit tests cover the boundaries: 0, 0.01, 1, 10, 11, 19, 20, 99, 100,
   101, 999, 1,000, 1,001, 10,000, 100,000, 999,999.99, 1,000,000, 1,000,000,000 and 1,000,000,000,000 −
   0.01.
5. Credit notes, reversals and negative journal totals print the absolute amount in words. The document title
   says what it is ("Credit note", "Reversal of JV-…"); the words never say "minus".
6. The layout fits one A4 page for up to 15 lines and continues on further pages with the total on the last
   page.
7. Printing is logged in the audit trail (document, user, time), because a reprinted voucher is an audit
   question.

**Worked examples**

| Document | Figure | English | Amharic |
|---|---|---|---|
| Payment to a supplier: invoice 100,000 + VAT 15% 15,000 = 115,000, less withholding 3% of 100,000 = 3,000 | 112,000.00 | One hundred twelve thousand birr only | አንድ መቶ አሥራ ሁለት ሺህ ብር |
| Petty cash payment for fuel | 2,475.25 | Two thousand four hundred seventy-five birr and twenty-five santim | ሁለት ሺህ አራት መቶ ሰባ አምስት ብር ከሃያ አምስት ሳንቲም |
| Payroll journal, Hidar 2019 net pay | 33,740.00 | Thirty-three thousand seven hundred forty birr only | ሠላሳ ሦስት ሺህ ሰባት መቶ አርባ ብር |
| Sales receipt for a sugar consignment | 1,150,000.50 | One million one hundred fifty thousand birr and fifty santim | አንድ ሚሊዮን አንድ መቶ ሃምሳ ሺህ ብር ከሃምሳ ሳንቲም |
| Bank charge | 0.75 | Seventy-five santim only | ሰባ አምስት ሳንቲም |

The voucher is dated "ኅዳር 9 ቀን 2019 ዓ.ም. (18 Nov 2026)" for an Amharic user.

**Edge cases**
- **Numbers from 11 to 19 in Amharic** ("አሥራ አንድ" …) and round hundreds and thousands: use "አንድ መቶ" and
  "አንድ ሺህ", or "መቶ" and "ሺህ"? Pick one style and test it. **VERIFY with the customer** which style their
  current vouchers and cheques use.
- **Spelling variants** (ሥ/ስ, ሦ/ሶ, ሠ/ሰ): use the customer's style and keep it in one table so it can be
  changed.
- **"Only" / "ብቻ" at the end**, as is often written on cheques: make it a company setting. **VERIFY** the
  practice.
- **Long Amharic text** for large amounts must wrap without breaking the layout. Use a Ge'ez font that prints
  (`docs/ethiopia.md` §6).
- **Journals with many lines:** the total in words is the total debit, which equals the total credit.
- **Multi-currency payment:** the words use the document currency, and the birr equivalent is shown in
  figures underneath.
- **Not a tax invoice.** This feature prints *vouchers*, not sales invoices for customers. Tax invoices must
  come from MoR-authorised numbering or machines (`docs/ethiopia.md` §3). **VERIFY with a tax expert** before
  Gebeta prints anything that a customer would treat as a tax invoice.

---

### 3. Comparative columns on profit and loss and the balance sheet

> **Built (step 50).**
> - **Dates:** `ComparisonPeriods` in the Accounting domain works out the comparison range from the fiscal
>   calendar (whole periods, or the same number of days for any other range), tested on Pagume and leap years.
> - **Reports:** `GET /api/accounting/reports/profit-and-loss/compare` and `/reports/balance-sheet/compare`
>   (CSV with `?format=csv`) run the plain report twice and merge by account code. The test uses the worked
>   example below, moved to Tikimt, and checks the comparison column against the plain report.
> - **Screen:** a **Compare with** choice on the financial reports, with both ranges in the headings.
>
> Differences from this specification:
> - **The note** says there are no postings in the comparison period, rather than naming a cut-over date:
>   Gebeta does not store one yet.
> - **Print** uses the browser's print of the screen; there is no separate print layout.
> - **Fixed on the way:** a new company's first journal failed because the chart of accounts was not yet set
>   up; creating a journal now sets it up first.

**In one line:** P&L and the balance sheet get a **Compare with** choice: *previous period*, *same period last
year*, or *year to date vs last year to date*. The report then shows the second column, the change in birr
and the change in percent.

**Who has it:**
- Business Central builds financial reports from column definitions with comparison date formulas: −1P for
  the previous period, −1FY for the same period last year, and a built-in YTD comparison
  ([Microsoft Learn: column definitions](https://learn.microsoft.com/en-ie/dynamics365/business-central/bi-column-definitions)).
- Odoo, Zoho Books and ERPNext also have period comparison on their standard financial reports. That is from
  general product knowledge; I did not check their pages for this document.
- Gebeta needs only the simple fixed set, not BC's general column designer.

**Why it matters here:**
- Every month the finance manager reports results to management, and a public enterprise reports to the
  Group and its board.
- Sugar is seasonal, so "Hidar this year vs Hidar last year" means more than "Hidar vs Tikimt".
- At year end, IFRS financial statements must show comparative figures for the previous period (IAS 1,
  para. 38), and IFRS is required by Financial Reporting Proclamation 847/2014 (`docs/ethiopia.md` §5).
- Today the accountant runs the report twice and builds the comparison in Excel, every month. That costs time
  and is error-prone.

**Builds on:**
- `GET /api/accounting/reports/profit-and-loss?from&to` and `/reports/balance-sheet?asOf`
  (`src/Modules/Accounting/Gebeta.Accounting.Presentation/AccountingEndpoints.cs`).
- The page `web/src/pages/accounting/FinancialReports.tsx`.
- `EthiopianFiscalYear` and the 12 fiscal periods (Nehase takes Pagume).
- P&L already excludes year-end journals.

**Effort: S.** The existing query runs a second time for the comparison dates and the results are merged by
account. The only new logic is working out the comparison dates.

**User story:** As a finance manager, I want this month's P&L and balance sheet next to last month's and last
year's figures, with the changes. Then I can explain the results to management without building a
spreadsheet.

**Acceptance criteria**

1. P&L and the balance sheet have **Compare with:** *None* (default), *Previous period*, *Same period last
   year*. P&L also has *Last year to date*.
2. The comparison dates are worked out from the company's fiscal calendar:
   - **Ethiopian fiscal year:** "period" means the Ethiopian month, Nehase includes Pagume, and "last year"
     means the same Ethiopian month or the same fiscal-year-to-date span one year earlier.
   - **Gregorian fiscal year:** use calendar months.
   - A custom from–to range that is not a whole period compares with a range of the same length just before
     it ("previous") or one year earlier ("last year").
3. Columns: current, comparison, change (birr), change (%). Percent is "n/a" when the comparison amount is
   zero.
4. Every account with a balance in either column is shown. An account with none in one column shows "–" in
   that column, not 0.00.
5. The subtotals (income, expenses, profit; assets, liabilities, equity) are compared the same way. The
   balance sheet still balances in each column on its own.
6. The headings show both ranges in the user's calendar, for example "Hidar 2019" and "Hidar 2018" or
   "10 Nov – 9 Dec 2026".
7. The comparison column equals the plain report run for the comparison dates (a test runs both).
8. A comparison that falls before the company's first date in Gebeta shows a note such as "no data before
   <cut-over date>" instead of zeros. If opening balances were posted, the balance sheet comparison as of the
   day before cut-over shows them.
9. The CSV and print outputs include the comparison columns.

**Worked example:** P&L for Hidar 2019 (10 Nov – 9 Dec 2026), compared with the same period last year, Hidar
2018 (10 Nov – 9 Dec 2025). The figures are illustrative, not customer data.

| Account | Hidar 2019 | Hidar 2018 | Change | % |
|---|---|---|---|---|
| 4100 Sales: sugar | 62,400,000.00 | 51,800,000.00 | +10,600,000.00 | +20.5% |
| 5000 Cost of sales | 41,300,000.00 | 36,900,000.00 | +4,400,000.00 | +11.9% |
| 6110 Salaries and wages | 9,850,000.00 | 8,760,000.00 | +1,090,000.00 | +12.4% |
| 6400 Bank charges | 42,500.00 | – | +42,500.00 | n/a |
| **Profit** | **11,207,500.00** | **6,140,000.00** | **+5,067,500.00** | **+82.5%** |

With *Last year to date* the columns are FY 2019 to date (Hamle 1, 2018 – Hidar 30, 2019 = 8 Jul – 9 Dec 2026)
against the same span of FY 2018 (8 Jul – 9 Dec 2025).

**Edge cases**
- **Pagume:** "Previous period" of Meskerem is Nehase including Pagume (5 or 6 days). "Same period last year"
  of Nehase is Nehase including last year's Pagume, which can differ in length (a leap year). Show the actual
  date ranges in the headings so nobody is misled.
- **Company changed from a Gregorian to an Ethiopian fiscal year** (or the other way): compare by date range
  and show the ranges.
- **Year-end closing journals:** P&L already leaves them out. The balance sheet one year back shows retained
  earnings after closing, as it should.
- **A reopened period with late postings:** the comparison is always computed live, never stored.
- **Accounts created this year:** these show "–" for last year. **Renamed accounts** are matched by account id,
  not by name.
- **Cost-centre filter:** when it is added later, it applies to both columns.

---

## The other candidates

### 4. Statement of account per customer or supplier (S, value 4)

> **Built (step 51).** `GET /api/accounting/reports/party-statement?companyId&partyId&kind&from&to` (CSV with
> `?format=csv`) and a printable page opened from the customer or supplier page, which can print as a
> **balance confirmation** letter with a reply box for the party to sign. The closing balance is shown in
> figures and in words, with the ageing underneath.
>
> Differences from this note:
> - **Built from the documents, not the GL:** ledger rows carry no party, so the statement lists the party's
>   submitted invoices, withholding, credit or debit notes and payments. A test checks that the closing balance
>   always equals the party's ageing total. Journals posted straight to a receivable or payable account do not
>   appear, because they name no party.
> - **One kind at a time:** a party that is both customer and supplier gets two statements (receivable and
>   payable), chosen on the page. They are not netted.
> - **Not built yet:** sending statements to many customers at once (Zoho), because Gebeta does not send
>   e-mail yet.

- **Feature:** a printable statement for one party and a date range: opening balance, every invoice,
  payment, credit or debit note and withholding certificate with a running balance, closing balance, and the
  ageing buckets at the bottom. It can also be used as a **balance confirmation letter**.
- **Who has it:**
  - Odoo's customer statement and follow-up report
    ([Odoo 18: follow-up on invoices](https://www.odoo.com/documentation/18.0/applications/finance/accounting/payments/follow_up.html));
  - Zoho Books, which sends statements to many customers at once
    ([Zoho Books: statements to multiple customers](https://www.zoho.com/en-ng/books/kb/year-end-accounting/acc-st-multiple-cx.html));
  - Business Central's statement and reminder documents
    ([Microsoft Learn: collect outstanding balances](https://learn.microsoft.com/en-US/dynamics365/business-central/receivables-collect-outstanding-balances)).
- **Why here:**
  - Monthly with large customers and suppliers.
  - At year end, auditors ask for receivable and payable confirmations, and finance staff build them by hand
    from the GL.
  - Withholding has to be shown, so the customer can see why the balance differs from the invoice total.
- **Builds on:** GL rows with `party_id`, `/reports/general-ledger` (opening and running balance),
  `/reports/ageing`, `PartyDetail.tsx`, and the print pattern from #2 (amount in words for the closing
  balance).
- **Effort: S.** It is the general ledger filtered by party instead of by account, plus a print layout.

### 5. Employee loans and standing deductions with a running balance (M, value 5)

- **Feature:** record a loan or a standing deduction for an employee once: amount, instalment, start month,
  optional end or total. Each month's slip then deducts the instalment automatically until the balance is
  zero, with pause, early settlement and a balance report. Typical cases are cost-sharing repayment,
  cooperative or credit association (Idir/Equb), court orders, salary advances and healthcare.
- **Who has it:**
  - Zoho Payroll loans, with instalments deducted each pay run, pause and resume, and manual repayments
    ([Zoho Payroll: loans](https://www.zoho.com/ae/payroll/help/employer/loans.html));
  - Odoo salary attachments (running, completed, cancelled; court orders and voluntary assignments)
    ([Odoo 18: salary attachments](https://www.odoo.com/documentation/18.0/applications/hr/payroll/salary_attachments.html)).
- **Why here:**
  - Monthly, for a large share of the workforce. `docs/ethiopia.md` §4 lists these deductions as planned.
  - The inventory has `healthcare_payment_deduction` and `employee_advance_auto_repayment_dates`
    (`docs/custom-features-inventory.md`).
  - Today they are re-entered monthly as additional salary, so balances are tracked in Excel and over- or
    under-deductions happen.
- **Builds on:** additional pay entries (`additional_pay`, `source_id` is already "a plain reference shared by
  every document that gives entries", `docs/modules/payroll.md`), the slip calculation, and back pay's rule
  for releasing entries when a slip is cancelled.
- **Effort: M.** It needs a new document with a balance, a monthly generator at run time, and pause and
  settle actions.
- **Legal:** the total deductions from a month's wage may be capped by Labour Proclamation 1156/2019 (we
  believe Art. 59: deductions may not exceed one third of the monthly wage, except where the law or a court
  says otherwise). **VERIFY with a labour expert** before building the cap.

### 6. Reorder level and a "below reorder level" list (S, value 3)

- **Feature:** a reorder level and reorder quantity per item and warehouse, and a stock report "items at or
  below reorder level", with a button that opens a purchase order filled with those items.
- **Who has it:**
  - ERPNext reorder levels with automatic material requests
    ([Frappe docs: auto creation of material request](https://docs.frappe.io/erpnext/user/manual/en/auto-creation-of-material-request));
  - Odoo min/max reordering rules
    ([Odoo 18: reordering rules](https://www.odoo.com/documentation/18.0/applications/inventory_and_mrp/inventory/warehouses_storage/replenishment/reordering_rules.html));
  - Business Central's reorder point on the item card.
- **Why here:** a factory stops when a critical spare part or process chemical runs out, and many such items
  have long lead times (imports, LC). Storekeepers check this weekly.
- **Builds on:** `/api/stock/reports/balance`, the warehouses, and the purchase order form with prefill (as
  **Receive goods** does).
- **Effort: S.** It is two fields on Stock's item settings, one report and one prefill. Automatic material
  requests wait for Material Requests (Phase 5).

### 7. Customer credit limit warning (S, value 3)

- **Feature:** a credit limit on the customer. On a sales order or sales invoice, show a warning when
  outstanding plus this document goes over the limit. A role can be allowed to override it.
- **Who has it:**
  - ERPNext, with a credit limit per company and a "credit controller" role that may override
    ([Frappe docs: credit limit](https://docs.frappe.io/erpnext/credit-limit));
  - Business Central, which warns on the sales order but does not block
    ([Microsoft Learn: credit limit patterns](https://learn.microsoft.com/nl-be/dynamics365/guidance/business-processes/pattern-manually-set-check-customer-credit-limits));
  - SAP Business One.
- **Why here:** it prevents goods leaving to a customer who already owes too much, which is daily for
  companies that sell on credit.
- **Builds on:** Core parties, the payment ledger and outstanding amounts (`/reports/ageing`), and Selling's
  order form.
- **Effort: S.** Selling needs a small capability asking Accounting for a customer's outstanding amount, with
  a fallback of "unknown".
- **VERIFY with the customer** how much sugar is sold on credit. If sales are cash or prepaid through the
  Group, this drops down the list.

### 8. Copy document (S, value 3)

- **Feature:** **Copy** on an invoice, order or journal opens a new draft with the same party, lines and
  accounts, dated today.
- **Who has it:**
  - Business Central's Copy Document
    ([Microsoft Learn: sell products](https://learn.microsoft.com/en-gb/dynamics365/business-central/sales-how-sell-products));
  - Odoo's *Duplicate*;
  - ERPNext's *Duplicate*.
- **Why here:** it cuts typing on the many similar documents a factory enters daily (the same supplier and
  items every week), with fewer keying mistakes.
- **Builds on:** the existing create forms, which already accept prefill (*Record supplier invoice*, *Create
  sales invoice*).
- **Effort: S.** It is front-end prefill plus a "copied from" reference.

### 9. Recurring journals and invoices (M, value 4)

- **Feature:** save a journal or invoice as a template with a frequency (monthly by Ethiopian or Gregorian
  month) and an end date. A **Due now** list creates the month's documents as drafts in one click.
- **Who has it:**
  - Business Central's recurring general journals
    ([Microsoft Learn: introduction to recurring journals](https://docs.microsoft.com/learn/modules/periodic-journal-entries-dynamics-365-business-central/1-recurring));
  - NetSuite memorized transactions, where a run that falls in a closed period posts to the next open one
    ([Oracle NetSuite Help: memorized transactions](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N564245.html));
  - SAP S/4HANA's Manage Recurring Journal Entries app (F1598);
  - Zoho Books recurring invoices
    ([Zoho Books: recurring invoices](https://www.zoho.com/books/help/recurring-invoice/ri-createsend.html));
  - ERPNext Auto Repeat
    ([Frappe docs: auto repeat](https://docs.frappe.io/erpnext/auto-repeat-recurring-orders-and-invoices)).
- **Why here:** monthly accruals, rent, insurance prepayment release, and depreciation until the Assets module
  exists. These are typed again every month before month close.
- **Builds on:** journals and invoices, and naming series. The "due list plus button" approach avoids a
  scheduler.
- **Effort: M.** It needs a new template table and a generator. Ethiopian month arithmetic already exists in
  `EthiopianDate`.

### 10. Purchase invoice check against order and goods received: three-way match (M, value 4)

- **Feature:** when a purchase invoice names a purchase order, warn when the billed quantity is more than
  what was received, or the rate is above the order rate. The purchase order shows an *exception* state.
- **Who has it:**
  - Odoo bill control on received quantities with 3-way matching ("should be paid")
    ([Odoo 17: bill control policies](https://www.odoo.com/documentation/17.0/applications/inventory_and_mrp/purchase/manage_deals/control_bills.html));
  - NetSuite and SAP have the same control.
- **Why here:** it stops paying for goods that never arrived or at a higher price than ordered. This is a
  frequent audit finding in public enterprises, and it applies to every supplier invoice.
- **Builds on:** Buying already counts received and billed quantities per line (`IPurchaseReceiptLookup`,
  `IPurchaseBillingLookup`, `docs/modules/buying.md`), and account 2150 Stock Received But Not Billed.
- **Effort: M.** Accounting must ask Buying through a new capability (Accounting must not depend on Buying),
  with a fallback when Buying is not installed.

### 11. Budget vs actual report (M, value 4)

- **Feature:** enter or paste an annual budget per account and cost centre, split by month, and show a report
  of budget, actual, variance and percent used for the month and year to date. This is report only; blocking
  comes later.
- **Who has it:**
  - ERPNext budgets with stop, warn or ignore, and the Budget Variance Report
    ([Frappe docs: budget variance report](https://docs.frappe.io/erpnext/budget-variance-report));
  - SAP Business One budgets that block or warn on purchase orders
    ([SAP Help: budget settings](https://help.sap.com/saphelp_sbo91/helpdata/en/45/063d32856241e1e10000000a1553f6/content.htm));
  - Business Central budget columns in financial reports.
- **Why here:** public enterprises run on an approved annual budget and report utilisation to their owner.
  Today this lives in Excel.
- **Builds on:** the GL with cost centres and the financial reports. It pairs naturally with #3 (a budget
  column).
- **Effort: M.** It needs a new budget table, paste import (as additional salary does), and one report.
  Checks on purchase orders are a later L item.

### 12. Suggested supplier payments by due date (M, value 3)

- **Feature:** a "due by <date>" list of open purchase invoices grouped by supplier, with tick boxes. **Pay
  selected** creates one payment per supplier through the existing multi-invoice payment.
- **Who has it:** Business Central's Suggest Vendor Payments
  ([Microsoft Learn: suggest vendor payments](https://learn.microsoft.com/en-au/dynamics365/business-central/payables-how-suggest-vendor-payments));
  SAP's payment proposal (the automatic payment program).
- **Why here:** a weekly payment run for the treasury. It reduces missed due dates and double payments.
- **Builds on:** `/reports/ageing?kind=Purchase` and payments with `allocations` (step 24).
- **Effort: M.** It is a list screen plus bulk creation. A bank file per bank waits for bank formats
  (`docs/ethiopia.md` §5).

---

## What I would not build now

| Idea | Why not now |
|---|---|
| Approval rules by amount (SAP B1 approval procedures, BC workflows) | Needs the Core workflow engine, which the roadmap plans but is not built. Do it once that exists. |
| Automatic purchase requests from reorder levels | Needs Material Requests (Phase 5). #6 gives most of the value with a list and a button. |
| Emailed reminders and dunning levels (BC reminders, Odoo follow-ups) | Needs outgoing email set up per company. The printed statement (#4) comes first. |
| MoR e-invoicing and fiscal printers | External integration that needs MoR specifications, as in `docs/ethiopia.md` §3. |
| Bank API payments | Each Ethiopian bank has its own format or API. Files come first. |

## VERIFY items

1. **Deduction cap.** Labour Proclamation 1156/2019, the article on deductions from wages (we believe Art. 59,
   one third of the monthly wage). It affects #5. **VERIFY with a labour expert.**
2. **Pension base when payment days are reduced.** Is the 7% / 11% on earned basic or on full basic? The #1
   example uses earned basic. **VERIFY with the pension agency or a labour expert.**
3. **Voucher wording.** Do public enterprises have to use a prescribed payment voucher format (Ministry of
   Finance or the public enterprise holding agency)? What is the Amharic spelling style, and is "ብቻ"
   expected? It affects #2. **VERIFY with the customer's finance head.**
4. **Printing tax invoices.** Can a sales invoice printed from Gebeta serve as a tax invoice without MoR
   authorisation? #2 deliberately prints only vouchers. **VERIFY with a tax expert.**
5. **Income tax table.** The income tax table used in the examples is still marked verify in
   `docs/ethiopia.md` §3.
6. **IFRS comparatives.** That IFRS comparatives (IAS 1.38) apply to the customer's statutory accounts under
   Proclamation 847/2014 and AABE rules. **VERIFY with the customer's auditor** (this is low risk).

## Open questions for the customer

1. What net-pay change should be flagged in the payroll comparison: 10%, or a birr amount? Should seasonal
   workers be compared at all?
2. Who signs vouchers today (how many signature boxes, which titles)? Can we have a copy of the current
   voucher and cheque forms?
3. Is sugar sold on credit to distributors, and how are credit limits set today? (This decides #7.)
4. Which standing deductions do you have (cost sharing, cooperative, court orders, healthcare, advances), and
   how many employees have each? (This sizes #5.)
5. Is the annual budget kept per account and cost centre, and by month? Who receives the utilisation report?
   (This decides #11.)

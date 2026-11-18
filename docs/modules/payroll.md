# Payroll module

The **first module to go live** (see [roadmap](../roadmap.md)).

## Scope

| Area | Contents (current custom doctypes in brackets) |
|---|---|
| Setup | Salary components, salary structures, salary structure assignment, income tax slabs, pension rules, component expense accounts (`component_expense_account`) |
| Run | Payroll entry (bulk), salary slip, additional salary (`bulk_additional_salary`, `employees_for_additional_salary`) |
| Arrears | Salary back payment (`salary_back_payment`, `back_payment_detail`, `back_payment_component`, `back_payment_payroll_line`, settings) |
| Payment | Salary payment distribution requests, bank file export, payroll journal |
| Reports | Payroll register, tax report (for the revenue authority), pension report, bank advice, payslips (PDF) |

## Salary components and formulas

A component has: name, abbreviation (e.g. `BS`, `IT`, `P7%`), type (earning/deduction),
taxable or not, depends on payment days, formula or fixed amount, condition, account, and the
**back payment treatment** (current custom field `custom_back_payment_treatment`).

### Formula engine

Built in Gebeta (`Gebeta.Payroll.Domain.Formulas.Formula`), no external library:
- the **same syntax as ERPNext salary formulas** (a Python subset), so existing formulas such as
  `0 if BS <= 2000 else BS*0.15 - 300 if BS <= 4000 else ...` run unchanged and need no conversion;
- every number is a `decimal`; rounding is half away from zero;
- only these names exist: component abbreviations, `base`, `payment_days`, `total_working_days`,
  declared employee variables, and for deductions `gross_pay` and `taxable_earnings`;
- only these functions: `if`, `min`, `max`, `round`, `flt`, `floor`, `ceil`, `abs`, `int`, and
  `income_tax(x)` (the table valid on the payroll date);
- no attribute access, imports or statements; length (2,000 characters) and nesting are limited;
- components are evaluated in dependency order; a circle, an unknown name or an earning that reads a
  deduction is a validation error when the structure is saved.

A parity test runs the current ERPNext income tax formula through the engine for every amount from
0 to 20,000 birr in steps of 0.50 and compares it with Gebeta's tax table.

## Income tax and pension (Ethiopia)

- Tax slabs are **data**, not code: a slab table with from, to, rate, deduction, valid from.
  The current system uses: 0 – 2,000: 0%; 2,001 – 4,000: 15% − 300; 4,001 – 7,000: 20% − 500;
  7,001 – 10,000: 25% − 850; 10,001 – 14,000: 30% − 1,350; above 14,000: 35% − 2,050.
  (Check against the current proclamation before go-live.)
- Pension: employee 7%, employer 11% of basic salary (configurable).
- Tax is monthly on taxable earnings of the month. Arrears are taxed per the back-payment rule below.

## Salary slip calculation

```
inputs:  assignment (structure + base) valid in the period, payment days (from HR attendance and
         unpaid leave), additional salaries in the period, loan/advance repayments
steps:   earnings → taxable gross → deductions (tax, pension, others) → net pay
output:  slip lines with amount, formula used, and the values of variables (for "why this number")
```

Submitting slips (usually through a Payroll Entry) creates one payroll journal:
expense accounts per component and cost centre → payroll payable, tax payable, pension payable.
It is sent as `JournalRequested`; with Accounting installed it is posted to the GL, otherwise it
stays in the outbound journal list for export.

## Standalone and plugged in

**Alone** (on Core): employees come from the Core directory, extended by Payroll's own
`employee_profile` (salary structure, bank account, tax ID, pension ID). Payment days are entered
or imported per period. Everything else works: structures, formulas, tax, slips, back payment,
payslips, bank file, and the payroll journal as an export.

**When plugged in with:**

| Module | Adds |
|---|---|
| HR | Payment days from attendance and unpaid leave (`IPaymentDaysProvider`); grade and step drive the salary; deductions for advances and healthcare (`IEmployeeDeductions`); final settlement when `EmployeeSeparated` arrives; payslips in employee self-service; Salary tab on the HR employee form |
| Accounting | Payroll journal posted automatically, with status on the payroll entry; account pickers on salary components |
| Cash Management | Employees paid in cash become cashier payments |
| Manufacturing | Piece-rate or overtime earnings from job-card time logs |

## Salary back payment (arrears)

**Built** (`Gebeta.Payroll.Domain.BackPay`, `/api/payroll/back-payments`). The current custom logic, kept:

1. Input: employee (or many, bulk), the revised salary structure assignment, effective from/to,
   payroll date, reason, reference.
2. For each **submitted** slip in the period, recalculate the slip with the revised assignment
   (in memory, never saving it), and compare per component.
3. Difference per component per month → back payment detail rows. Each component follows its
   back-payment treatment: **Arrears**, **Income Tax**, **Employee Pension**, **Employer Pension**,
   **Other Deduction**, or **Exclude**.
4. Tax is allocated to the month it belongs to: additional tax = new tax of the month − tax
   already withheld for it, so arrears are not over-taxed by landing in one month. Employee and
   employer pension and other salary-based deductions are differences the same way.
5. A decrease (downgrade) follows the negative policy in settings; with "Recover in Payroll" it
   creates a **recovery** line instead of a payment.
6. Earlier back payments for the same months are taken into account, so running it twice does
   not pay twice.
7. On submit, create **additional pay entries** for the payroll month (arrears or recovery,
   back-payment income tax, pension and employer contributions, with refunds when they are
   negative). The first slip whose period contains the payroll date takes them as extra lines that
   are not taxed again. Cancelling that slip releases them; the back payment can only be cancelled
   while none of its entries is in a slip. Negative arrears follow `Payroll:NegativeArrearsPolicy`
   (`RecoverInPayroll` by default, or `Refuse`).
8. Closed periods and submitted slips are never changed.

## Tests that must exist

- Golden tests from real anonymised slips of the current system: same inputs → same net pay to the cent.
- Tax slab boundaries (2000, 2000.01, 4000, ...).
- Back payment: increase, decrease, partial month, run twice, cancel after payroll.
- Formula engine: cycles, unknown variables, division by zero, injection attempts.

## Pay values

A salary structure can declare **pay values** besides the base, for example `ctc` (a contract
amount) or `fuel_payment` (an allowance agreed per employee). Its formulas use them like `base`.

- Each salary structure assignment gives the amounts, and must give all of them and nothing
  else.
- A change to a pay value is a new assignment, like a raise. Back pay therefore recalculates past
  months with the new values.
- API: `variables` on `POST /api/payroll/structures`, `values` on `POST /api/payroll/assignments`.
- Screens: Payroll → Salary structures (list, create with a "try it" calculation, details), and the
  Salary section of each employee (assignment history, new assignment asking for the structure's pay
  values).
- Debt: a structure cannot be edited once saved (create a new one and reassign). That is on purpose
  for now, because slips keep the structure's name, but a versioned edit will be wanted.

## Additional pay and back payments

A slip keeps what was paid on top of the formulas that month (overtime, a one-off allowance or
deduction). A back payment recalculating the month pays it again: it does not change with the base,
but it changes the tax.

Before step 40 the recalculation left it out, so a raise over a month with overtime gave arrears
that were too low, or even negative. Slips made before step 40 have no record of their additional
pay; their back payments still have that problem. That only matters for test data, because no
company is live yet.

## Salary slip history

Slips paid in the previous system come over at cut-over as history (`IsHistory`, see
docs/data-migration.md step 5). They are submitted, keep their old number and lines, post nothing,
and cannot be cancelled. Back payments and reports read them like any other slip.

## Additional salary

Overtime, an allowance or a one-off deduction (a cooperative or loan repayment) for many employees
in one payroll month: one document per component and month (`ADS-` numbers). Amounts are typed
per employee or pasted from a spreadsheet (employee number and amount).

- Submitting gives each employee an entry. Their slip for that month takes it **into the
  calculation**, so income tax sees a taxable earning, and the slip shows it as its own line. Use
  a structure component's abbreviation to add to that component; any other abbreviation joins the
  month's calculation at zero with the document's type and taxable flag (the same rule back
  payments use).
- Back payment entries are different: they are lines of their own, already taxed by the back
  payment.
- Cancelling releases the entries, and is refused once a slip includes one: cancel that slip, or
  correct the amount in a later month.
- `additional_pay.source_id` is a plain reference shared by every document that gives entries.
  Back payments link their entries through `back_payment_id`.

## Comparison with last month

After **Generate**, the payroll run shows each employee's gross, income tax, pension and net against
last month: the submitted slips of the period ending the day before this one, history slips
included. Each row is *Changed*, *New*, *Missing* or *Same*.

- **Flags:** a row is flagged when net pay moves by more than `Payroll:ComparisonThresholdPercent`
  (default 10%), and *New* and *Missing* rows are always flagged.
- **Reasons:** each row lists what the data shows: additional salary (`ADS-` number and amount),
  back pay entries, different payment days, a new salary assignment, and the leaving date.
- **Totals:** both months' totals use the same rules as the income tax and pension reports, through
  `SlipFigures`.
- **No block:** it is a warning, and submitting is never blocked.
- **Reads:** everything is read in batches, never one query per employee.


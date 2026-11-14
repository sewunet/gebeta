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

- Expressions like `base`, `BS * 0.07`, `0 if BS <= 2000 else BS*0.15 - 300 ...`.
- Implemented on NCalc behind `IFormulaEngine`:
  - only whitelisted variables: component abbreviations, `base`, `payment_days`, `total_working_days`,
    employee fields (grade, step, branch), and settings;
  - only whitelisted functions: `if`, `min`, `max`, `round`, `floor`, `ceil`, `abs`;
  - all numbers evaluated as `decimal`;
  - a timeout and a size limit.
- Components are evaluated in dependency order (topological sort of abbreviations), and a cycle is
  a validation error when the structure is saved.
- Python-style `x if c else y` from ERPNext is converted to `if(c, x, y)` by the migration tool.

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

The current custom logic, to be kept:

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
7. On submit, create **additional salary** lines in the payroll month (arrears, recovery,
   back-payment income tax). They appear in that month's slip. Cancel removes them, unless they
   are already in a submitted slip.
8. Closed periods and submitted slips are never changed.

## Tests that must exist

- Golden tests from real anonymised slips of the current system: same inputs → same net pay to the cent.
- Tax slab boundaries (2000, 2000.01, 4000, ...).
- Back payment: increase, decrease, partial month, run twice, cancel after payroll.
- Formula engine: cycles, unknown variables, division by zero, injection attempts.

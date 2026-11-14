# HR module

People data and everything except salary calculation (see [payroll.md](payroll.md)).

## Scope

| Area | Contents (current custom doctypes in brackets) |
|---|---|
| Employee | Personal data, children, ethnicity, education, qualification, grade, level, step (`employee_step`, `level`), branch, department, reporting line |
| Organisation | Designations, grades, internal vacancies (`internal_vacancy_applicant`), succession planning, talent management and planning |
| Lifecycle | Onboarding, promotion (`bulk_employee_promotion`, `promotion_test*`, `promotion_applicant`), transfer (`bulk_employee_transfer`), separation (`employee_separation_reason`, `separation_reason`), clearance (`clearance`, `clearance_template`), severance (`employee_severance_amount`) |
| Attendance | Shifts, check-ins (`bulk_employee_checkin`), attendance collection from devices (`attendance_collection`, `attendance_collection_log`) |
| Leave | Leave types, policies, allocation, auto increment by service years (`leave_allocation_auto_increment`, custom leave increment period), leave plan, encashment |
| Performance | Appraisal, self appraisal, areas of improvement, disciplinary actions and types |
| Training | Training plan, events, attendance, evaluation, assessment templates and responses, impact assessment, commitment, reports |
| Expenses | Employee advance and auto repayment, advance clearance, per diem and expense requests (`employee_per_diem_and_expense_request`, `per_diem_*`, grade expense limits) |
| Health | Healthcare payment requests and deductions |
| Knowledge | Knowledge management and distribution |
| Housing | Company houses (`house`) |

## Leave ledger

Like accounting, leave uses an append-only **leave ledger**:
`allocation (+)`, `application (−)`, `expiry (−)`, `encashment (−)`, `carry forward (+)`.
Balance = sum of the ledger up to a date. Auto increment and accrual are scheduled jobs that add
allocation rows, so the history is always explainable.

## Attendance

- Check-ins come from biometric devices (pull job or push API), mobile app, or bulk import.
- A daily job turns check-ins into attendance using the shift (late entry, early exit, half day).
- Attendance gives **payment days** to payroll for each period.

## Employee self-service (React)

Payslips, leave balance and applications, attendance, advances, per diem and expense requests,
training, and personal data change requests (approved by HR).

## Standalone and plugged in

**Alone** (on Core): everything in the scope table. HR extends the Core employee record with its
own tables. Advances, per diem and expense claims are approved and tracked, and their journals
stay in the outbound journal list for export.

**When plugged in with:**

| Module | Adds |
|---|---|
| Payroll | HR provides payment days (attendance, unpaid leave) and deductions (advances, healthcare); grade/step changes and separations are sent as events; payslips appear in self-service |
| Accounting | Advance, per diem and expense journals posted automatically; budget check on per diem and training costs |
| Cash Management | Advances and per diem can be paid out by a cashier |
| Stock | Items issued to employees (uniforms, tools) show on the employee and on clearance |
| Manufacturing | Employees as operators on job cards |

Events HR publishes: `EmployeeHired`, `EmployeeChanged` (grade, step, branch, department),
`EmployeeSeparated`, `AttendanceFinalized` (period), `LeaveApproved`.

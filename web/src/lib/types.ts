// The API's response shapes (see the *.Contracts projects).

export type ModuleDto = { id: string; name: string; version: string; enhances: string[] };
export type MeDto = {
  id: string; username: string; email?: string; fullName?: string; language: "en" | "am";
  calendar: "Ethiopian" | "Gregorian" | "Both"; defaultCompanyId?: string; roles: string[]; permissions: string[];
  accessAllCompanies: boolean; companyIds: string[];
};
export type CompanyDto = {
  id: string; name: string; abbreviation: string; legalForm: string; tin: string; vatRegistrationNumber?: string;
  defaultCurrency: string; fiscalYearType: string; timeZone: string;
};
export type EmployeeDto = {
  id: string; companyId: string; number: string; firstName: string; fatherName: string; grandfatherName?: string;
  fullName: string; gender: string; dateOfJoining: string; dateOfLeaving?: string; status: string; tin?: string;
};
export type SalaryLineDto = { component: string; abbreviation: string; type: string; amount: number; formula?: string };
export type SalarySlipDto = {
  id: string; number?: string; companyId: string; employeeId: string; employeeNumber: string; employeeName: string;
  periodStart: string; periodEnd: string; ethiopianPeriod: string; status: string; structureName: string; base: number;
  paymentDays: number; totalWorkingDays: number; lines: SalaryLineDto[]; grossPay: number; taxableEarnings: number;
  totalDeductions: number; netPay: number; employerContributions: number; taxTable: string;
};
export type SalaryCalculationDto = {
  lines: SalaryLineDto[]; grossPay: number; taxableEarnings: number; totalDeductions: number; netPay: number;
  employerContributions: number; taxTable: string;
};
export type PayrollRunDto = {
  id: string; number?: string; companyId: string; periodStart: string; periodEnd: string; status: string;
  slipsGenerated: boolean; slipCount: number; totalGross: number; totalNet: number;
  issues: { employeeId: string; employeeName: string; reason: string }[];
};

export const can = (me: MeDto | undefined, permission: string) =>
  !!me?.permissions.some((p) => p === "*" || p === permission || (p.endsWith(".*") && permission.startsWith(p.slice(0, -1))));

export type LeaveApplicationDto = {
  id: string; number?: string; companyId: string; employeeId: string; employeeName: string; type: string;
  fromDate: string; toDate: string; days: number; isPaid: boolean; reason?: string; status: string; approvedBy?: string;
};
export type LeaveBalanceDto = { type: string; allocated: number; taken: number; balance: number };
export type AttendanceDto = { employeeId: string; date: string; status: string };
export type CashierDto = {
  id: string; companyId: string; code: string; name: string; type: string; status: string; cashAccountCode: string;
  autoTopUp: boolean; maxTransaction?: number; maxWithdrawal?: number; maxDeposit?: number; balance: number;
};
export type CashLedgerEntryDto = { date: string; kind: string; sourceNumber: string; description: string; amount: number; balanceAfter: number };
export type PettyCashRequestDto = {
  id: string; number?: string; cashierId: string; cashierName: string; requestedAmount: number; balanceWhenRequested: number;
  createdBySystem: boolean; status: string; fundingFrom?: string; sourceAccountCode?: string; fundingCashierId?: string;
  approvedBy?: string; rejectionReason?: string;
};
export type CashMovementDto = { number: string; cashierId: string; kind: string; amount: number; balanceAfter: number; topUpRequest?: PettyCashRequestDto };
export type AccountDto = { id: string; code: string; name: string; rootType: string; isGroup: boolean; parentId?: string };
export type JournalLineDto = { accountCode: string; accountName: string; debit: number; credit: number; description?: string };
export type JournalEntryDto = {
  id: string; number?: string; companyId: string; postingDate: string; remark: string; source: string; sourceNumber?: string;
  status: string; lines: JournalLineDto[]; totalDebit: number; totalCredit: number;
};
export type TrialBalanceDto = {
  companyId: string; asOf: string; totalDebit: number; totalCredit: number;
  rows: { code: string; name: string; rootType: string; debit: number; credit: number; balance: number }[];
};

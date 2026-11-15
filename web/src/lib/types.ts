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

// The API's response shapes (see the *.Contracts projects).

export type ModuleDto = { id: string; name: string; version: string; enhances: string[] };
export type MeDto = {
  id: string; username: string; email?: string; fullName?: string; language: "en" | "am";
  calendar: "Ethiopian" | "Gregorian" | "Both"; defaultCompanyId?: string; roles: string[]; permissions: string[];
  accessAllCompanies: boolean; companyIds: string[]; employeeId?: string;
};
export type CompanyDto = {
  id: string; name: string; abbreviation: string; legalForm: string; tin: string; vatRegistrationNumber?: string; tradeLicenseNumber?: string;
  defaultCurrency: string; fiscalYearType: string; timeZone: string;
};
export type EmployeeDto = {
  id: string; companyId: string; number: string; firstName: string; fatherName: string; grandfatherName?: string;
  fullName: string; gender: string; dateOfJoining: string; dateOfLeaving?: string; status: string; tin?: string;
};
export type SalaryComponentDto = {
  name: string; abbreviation: string; type: "Earning" | "Deduction" | "EmployerContribution"; formula?: string | null; amount?: number | null;
  condition?: string | null; isTaxable: boolean; dependsOnPaymentDays: boolean;
};
/** A salary structure; `variables` are the pay values (besides the base) every assignment gives its formulas. */
export type SalaryStructureDto = { id: string; companyId: string; name: string; isActive: boolean; components: SalaryComponentDto[]; variables?: string[] };
export type SalaryStructureAssignmentDto = { id: string; employeeId: string; structureId: string; base: number; fromDate: string; values?: Record<string, number> };
export type SalaryLineDto = { component: string; abbreviation: string; type: string; amount: number; formula?: string };
export type SalarySlipDto = {
  id: string; number?: string; companyId: string; employeeId: string; employeeNumber: string; employeeName: string;
  periodStart: string; periodEnd: string; ethiopianPeriod: string; status: string; structureName: string; base: number;
  paymentDays: number; totalWorkingDays: number; lines: SalaryLineDto[]; grossPay: number; taxableEarnings: number;
  totalDeductions: number; netPay: number; employerContributions: number; taxTable: string;
  /** Paid in the previous system and kept as history: posts nothing, cannot be cancelled. */
  isHistory?: boolean;
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

/** A Gebeta user; <c>isInvited</c>: set up ahead, not signed in yet. */
export type UserDto = {
  id: string; username: string; email?: string; fullName?: string; isActive: boolean; roles: string[]; companyIds: string[]; isInvited?: boolean;
  /** The employee this user is, for self-service. */
  employeeId?: string;
};
export type AdditionalSalaryLineDto = { employeeId: string; employeeNumber: string; employeeName: string; amount: number; note?: string };
/** Overtime, an allowance or a deduction for many employees in one payroll month; <c>inSlips</c>: amounts already in salary slips. */
export type AdditionalSalaryDto = {
  id: string; number?: string; companyId: string; payrollDate: string; component: string; abbreviation: string; type: "Earning" | "Deduction";
  isTaxable: boolean; status: string; remark?: string; lines: AdditionalSalaryLineDto[]; total: number; inSlips: number;
};
export type PayFigures = { gross: number; incomeTax: number; employeePension: number; employerPension: number; net: number; paymentDays: number };
export type PayComparisonRow = {
  employeeId: string; employeeNumber: string; employeeName: string; previous?: PayFigures; current?: PayFigures;
  netChange: number; changePercent?: number; status: "Changed" | "New" | "Missing" | "Same"; flagged: boolean; reasons: string[];
};
export type PayTotals = { headcount: number; gross: number; incomeTax: number; employeePension: number; employerPension: number; net: number };
export type PayrollCheckRow = { employeeId: string; employeeNumber: string; employeeName: string; slipId: string; slipNumber?: string; check: string; severity: "Error" | "Warning"; message: string };
export type PayrollChecksDto = {
  runId: string; periodStart: string; periodEnd: string; slipsChecked: number; minimumNetSharePercent: number;
  counts: { check: string; severity: "Error" | "Warning"; employees: number }[]; rows: PayrollCheckRow[]; errors: number; warnings: number; flaggedEmployees: number;
};
export type PayrollComparisonDto = {
  runId: string; periodStart: string; periodEnd: string; previousPeriodEnd: string; hasPrevious: boolean; thresholdPercent: number;
  rows: PayComparisonRow[]; previousTotals: PayTotals; currentTotals: PayTotals; flaggedCount: number;
};
export type ComparativeRow = { code: string; name: string; current?: number; comparison?: number; change: number; changePercent?: number };
export type ComparativeReportDto = {
  companyId: string; report: "ProfitAndLoss" | "BalanceSheet"; compare: string; currentFrom: string; currentTo: string; comparisonFrom: string; comparisonTo: string;
  sections: { name: "Income" | "Expenses" | "Assets" | "Liabilities" | "Equity"; rows: ComparativeRow[]; total: ComparativeRow }[];
  summary: ComparativeRow[]; currentBalanced: boolean; comparisonBalanced: boolean; note?: string;
};
export type MyLeaveDto = { balances: LeaveBalanceDto[]; applications: LeaveApplicationDto[] };
export type RoleDto = { id: string; name: string; description?: string; isSystem: boolean; permissions: string[] };

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

export type PartyDto = { id: string; companyId: string; name: string; type: "Customer" | "Supplier" | "Both"; tin?: string; vatRegistrationNumber?: string; phone?: string };
export type InvoiceLineDto = { description: string; quantity: number; rate: number; amount: number; accountCode: string; isService: boolean; vatApplicable: boolean; itemId?: string; itemCode?: string };
export type InvoiceDto = {
  id: string; number?: string; kind: "Sales" | "Purchase"; companyId: string; partyId: string; partyName: string; partyTin?: string; supplierInvoiceNumber?: string;
  postingDate: string; dueDate: string; status: string; lines: InvoiceLineDto[]; netTotal: number; vatAmount: number; grandTotal: number;
  withholdingAmount: number; payableAmount: number; outstanding: number; isReturn: boolean; returnAgainstNumber?: string; isOpening: boolean; withholdingReceiptNumber?: string; orderReference?: string;
};
export type PaymentDto = {
  id: string; number?: string; kind: string; partyId: string; partyName: string; invoiceId: string; invoiceNumber?: string; amount: number; postingDate: string;
  bankAccountCode: string; reference?: string; status: string; allocations?: { invoiceId: string; invoiceNumber?: string; amount: number }[];
};
export type AgeingRow = { partyId: string; partyName: string; partyTin?: string; notDue: number; days1To30: number; days31To60: number; days61To90: number; over90: number; total: number };
export type LoanRepaymentDto = { date: string; amount: number; source: "Payroll" | "Manual"; reference?: string; inSlip: boolean };
export type EmployeeLoanDto = {
  id: string; number?: string; companyId: string; employeeId: string; employeeNumber: string; employeeName: string; kind: "Loan" | "StandingDeduction";
  component: string; abbreviation: string; principal?: number; instalment: number; startMonth: string; endMonth?: string;
  status: "Draft" | "Running" | "Paused" | "Completed" | "Cancelled"; recovered: number; balance?: number; remark?: string; repayments: LoanRepaymentDto[];
};
export type PartyStatementLine = { date: string; type: string; number?: string; documentId?: string; reference?: string; dueDate?: string; debit: number; credit: number; balance: number };
export type PartyStatementDto = {
  companyId: string; partyId: string; partyName: string; partyTin?: string; kind: "Sales" | "Purchase"; from: string; to: string; opening: number;
  lines: PartyStatementLine[]; totalDebit: number; totalCredit: number; closing: number; ageing: AgeingRow;
};
export type AgeingReportDto = { kind: string; asOf: string; rows: AgeingRow[]; totals: AgeingRow };
export type VatReportDto = {
  from: string; to: string; outputVat: number; inputVat: number; netPayable: number;
  rows: { kind: string; number?: string; date: string; partyName: string; partyTin?: string; net: number; vat: number }[];
};
export type WithholdingRow = {
  number?: string; date: string; partyName: string; partyTin?: string; taxableAmount: number; withheld: number; receiptNumber?: string; supplyType?: string; ratePercent: number;
};
export type WithholdingReceiptDto = {
  invoiceId: string; receiptNumber: string; date: string; status: string; agentName: string; agentTin: string; supplierName: string; supplierTin?: string;
  invoiceNumber?: string; supplierInvoiceNumber?: string; supplyType: string; taxableAmount: number; ratePercent: number; withheld: number;
};
export type WithholdingReportDto = { from: string; to: string; totalWithheldFromSuppliers: number; totalWithheldByCustomers: number; fromSuppliers: WithholdingRow[]; byCustomers: WithholdingRow[] };
export type PeriodClosingDto = {
  id: string; label: string; start: string; end: string; status: "Closed" | "Reopened"; totalDebit: number; totalCredit: number; netProfit: number;
  closedAt: string; closedBy: string; reopenedAt?: string; reopenedBy?: string; reopenReason?: string;
};
export type PeriodClosingsDto = { companyId: string; lockDate?: string; closings: PeriodClosingDto[] };
export type ReportRow = { code: string; name: string; rootType: string; amount: number };
export type ProfitAndLossDto = { from: string; to: string; income: ReportRow[]; expenses: ReportRow[]; totalIncome: number; totalExpenses: number; netProfit: number };
export type BalanceSheetDto = {
  asOf: string; assets: ReportRow[]; liabilities: ReportRow[]; equity: ReportRow[]; unclosedProfit: number;
  totalAssets: number; totalLiabilities: number; totalEquity: number; balanced: boolean;
};
export type GeneralLedgerDto = {
  accountCode: string; accountName: string; from: string; to: string; opening: number; totalDebit: number; totalCredit: number; closing: number;
  rows: { date: string; voucher: string; description: string; debit: number; credit: number; balance: number; isReversal: boolean }[];
};
export type BankAccountDto = { id: string; companyId: string; name: string; bankName: string; accountNumber: string; ledgerAccountCode: string; bookBalance: number };
export type StatementLineDto = { id: string; date: string; description: string; reference?: string; amount: number; balance?: number; matched: boolean; matchedVouchers: string[] };
export type BookEntryDto = { glEntryId: string; date: string; voucher: string; description: string; amount: number; matched: boolean };
export type ReconciliationDto = {
  bankAccountId: string; from: string; to: string; statementLines: StatementLineDto[]; bookEntries: BookEntryDto[];
  suggestions: { statementLineId: string; glEntryId: string }[]; bookBalance: number; unmatchedStatement: number; unmatchedBook: number;
  expectedStatementBalance: number; statementBalance?: number; difference?: number;
};
export type ItemDto = { id: string; companyId: string; code: string; name: string; uom: string; group?: string; isStockItem: boolean; isActive: boolean };
export type WarehouseDto = { id: string; companyId: string; code: string; name: string; stockAccountCode: string };
export type ItemStockSettingDto = { itemId: string; method: "MovingAverage" | "Fifo"; hasMovements: boolean };
export type StockEntryLineDto = {
  itemId: string; itemCode: string; itemName: string; uom: string; qty: number; rate?: number; sourceWarehouseId?: string; sourceWarehouse?: string;
  targetWarehouseId?: string; targetWarehouse?: string; value: number;
};
export type StockEntryDto = {
  id: string; number?: string; companyId: string; purpose: "Receipt" | "Issue" | "Transfer" | "Reconciliation"; postingDate: string; status: string; counterAccountCode: string;
  remark?: string; lines: StockEntryLineDto[]; totalValue: number; partyId?: string; partyName?: string; reference?: string;
};
export type StockBalanceRow = { itemId: string; itemCode: string; itemName: string; uom: string; warehouseId: string; warehouse: string; qty: number; rate: number; value: number; method: string };
export type StockBalanceDto = { companyId: string; asOf: string; rows: StockBalanceRow[]; totalValue: number };
export type StockLedgerDto = {
  itemId: string; itemCode: string; itemName: string;
  rows: { date: string; voucher: string; warehouse: string; qty: number; rate: number; valueChange: number; qtyAfter: number; valueAfter: number }[];
};
export type PurchaseOrderLineDto = { itemId: string; itemCode: string; itemName: string; uom: string; qty: number; rate: number; amount: number; warehouseId?: string; receivedQty: number; billedQty: number };
export type PurchaseOrderDto = {
  id: string; number?: string; companyId: string; supplierId: string; supplierName: string; orderDate: string; requiredBy: string; status: string;
  state: "Draft" | "ToReceiveAndBill" | "ToReceive" | "ToBill" | "Completed" | "Closed" | "Cancelled";
  lines: PurchaseOrderLineDto[]; netTotal: number; percentReceived: number; percentBilled: number; remark?: string;
};
export type SalesOrderLineDto = { itemId: string; itemCode: string; itemName: string; uom: string; qty: number; rate: number; amount: number; warehouseId?: string; deliveredQty: number; billedQty: number };
export type SalesOrderDto = {
  id: string; number?: string; companyId: string; customerId: string; customerName: string; orderDate: string; deliveryDate: string; status: string;
  state: "Draft" | "ToDeliverAndBill" | "ToDeliver" | "ToBill" | "Completed" | "Closed" | "Cancelled";
  lines: SalesOrderLineDto[]; netTotal: number; percentDelivered: number; percentBilled: number; remark?: string;
};

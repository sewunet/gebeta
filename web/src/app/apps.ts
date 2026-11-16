/** Every screen as an "app": the navigation menu and the home page tiles are both built from this list. */
export type AppEntry = {
  id: string;
  group: "core" | "accounting" | "stock" | "payroll" | "hr" | "cash";
  path: string;
  title: string;      // i18n key
  subtitle?: string;  // i18n key
  permission: string;
  module?: string;    // shown only when this module is installed
};

export const APPS: AppEntry[] = [
  { id: "companies", group: "core", path: "/companies", title: "nav.companies", subtitle: "tiles.companies", permission: "core.companies.read" },
  { id: "employees", group: "core", path: "/employees", title: "nav.employees", subtitle: "tiles.employees", permission: "core.employees.read" },

  { id: "invoices", group: "accounting", path: "/accounting/invoices", title: "nav.invoices", subtitle: "tiles.invoices", permission: "accounting.invoices.read", module: "accounting" },
  { id: "parties", group: "accounting", path: "/accounting/parties", title: "nav.parties", subtitle: "tiles.parties", permission: "core.parties.read", module: "accounting" },
  { id: "ageing", group: "accounting", path: "/accounting/ageing", title: "nav.ageing", subtitle: "tiles.ageing", permission: "accounting.reports.read", module: "accounting" },
  { id: "banking", group: "accounting", path: "/accounting/banking", title: "nav.banking", subtitle: "tiles.banking", permission: "accounting.banking.read", module: "accounting" },
  { id: "journals", group: "accounting", path: "/accounting/journals", title: "nav.journals", subtitle: "tiles.journals", permission: "accounting.journals.read", module: "accounting" },
  { id: "accounts", group: "accounting", path: "/accounting/accounts", title: "nav.accounts", subtitle: "tiles.accounts", permission: "accounting.accounts.read", module: "accounting" },
  { id: "trial-balance", group: "accounting", path: "/accounting/trial-balance", title: "nav.trialBalance", subtitle: "tiles.trialBalance", permission: "accounting.reports.read", module: "accounting" },
  { id: "reports", group: "accounting", path: "/accounting/reports", title: "nav.financialReports", subtitle: "tiles.reports", permission: "accounting.reports.read", module: "accounting" },
  { id: "tax", group: "accounting", path: "/accounting/tax", title: "nav.taxReports", subtitle: "tiles.tax", permission: "accounting.reports.read", module: "accounting" },
  { id: "periods", group: "accounting", path: "/accounting/periods", title: "nav.periods", subtitle: "tiles.periods", permission: "accounting.reports.read", module: "accounting" },

  { id: "stock-entries", group: "stock", path: "/stock/entries", title: "nav.stockEntries", subtitle: "tiles.stockEntries", permission: "stock.entries.read", module: "stock" },
  { id: "stock-balance", group: "stock", path: "/stock/balance", title: "nav.stockBalance", subtitle: "tiles.stockBalance", permission: "stock.reports.read", module: "stock" },
  { id: "stock-ledger", group: "stock", path: "/stock/ledger", title: "nav.stockLedger", subtitle: "tiles.stockLedger", permission: "stock.reports.read", module: "stock" },
  { id: "items", group: "stock", path: "/items", title: "nav.items", subtitle: "tiles.items", permission: "core.items.read" },
  { id: "warehouses", group: "stock", path: "/stock/warehouses", title: "nav.warehouses", subtitle: "tiles.warehouses", permission: "stock.warehouses.read", module: "stock" },

  { id: "slips", group: "payroll", path: "/payroll/slips", title: "nav.salarySlips", subtitle: "tiles.slips", permission: "payroll.salary-slips.read", module: "payroll" },
  { id: "run", group: "payroll", path: "/payroll/run", title: "nav.payrollRun", subtitle: "tiles.run", permission: "payroll.runs.read", module: "payroll" },
  { id: "calculator", group: "payroll", path: "/payroll/calculator", title: "nav.calculator", subtitle: "tiles.calculator", permission: "payroll.calculator.use", module: "payroll" },

  { id: "leave", group: "hr", path: "/hr/leave", title: "nav.leave", subtitle: "tiles.leave", permission: "hr.leave.read", module: "hr" },
  { id: "attendance", group: "hr", path: "/hr/attendance", title: "nav.attendance", subtitle: "tiles.attendance", permission: "hr.attendance.read", module: "hr" },

  { id: "cashiers", group: "cash", path: "/cash/cashiers", title: "nav.cashiers", subtitle: "tiles.cashiers", permission: "cash.cashiers.read", module: "cash" },
  { id: "petty-cash", group: "cash", path: "/cash/petty-cash", title: "nav.pettyCash", subtitle: "tiles.pettyCash", permission: "cash.cashiers.read", module: "cash" },
];

export const GROUPS: AppEntry["group"][] = ["accounting", "stock", "payroll", "hr", "cash", "core"];

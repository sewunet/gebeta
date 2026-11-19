/**
 * The menu and home page groups, in the order work flows: your own records, selling, buying and stock, the books (entries, cash, then
 * reports), people and payroll, and the settings an administrator uses.
 */
export const GROUPS = ["me", "selling", "buying", "stock", "accounting", "cash", "reports", "hr", "payroll", "settings"] as const;
export type Group = (typeof GROUPS)[number];

/** Every screen as an "app": the navigation menu and the home page tiles are both built from this list, in this order within a group. */
export type AppEntry = {
  id: string;
  group: Group;
  path: string;
  title: string;      // i18n key
  subtitle?: string;  // i18n key
  permission: string;
  module?: string;    // shown only when this module is installed
};

export const APPS: AppEntry[] = [
  { id: "my-payslips", group: "me", path: "/me/payslips", title: "nav.myPayslips", subtitle: "tiles.myPayslips", permission: "payroll.self.read", module: "payroll" },
  { id: "my-leave", group: "me", path: "/me/leave", title: "nav.myLeave", subtitle: "tiles.myLeave", permission: "hr.self.read", module: "hr" },

  { id: "sales-orders", group: "selling", path: "/selling/orders", title: "nav.salesOrders", subtitle: "tiles.salesOrders", permission: "selling.orders.read", module: "selling" },

  { id: "purchase-orders", group: "buying", path: "/buying/orders", title: "nav.purchaseOrders", subtitle: "tiles.purchaseOrders", permission: "buying.orders.read", module: "buying" },
  { id: "agreements", group: "buying", path: "/buying/agreements", title: "nav.agreements", subtitle: "tiles.agreements", permission: "buying.agreements.read", module: "buying" },

  { id: "items", group: "stock", path: "/items", title: "nav.items", subtitle: "tiles.items", permission: "core.items.read" },
  { id: "stock-entries", group: "stock", path: "/stock/entries", title: "nav.stockEntries", subtitle: "tiles.stockEntries", permission: "stock.entries.read", module: "stock" },
  { id: "stock-balance", group: "stock", path: "/stock/balance", title: "nav.stockBalance", subtitle: "tiles.stockBalance", permission: "stock.reports.read", module: "stock" },
  { id: "stock-ledger", group: "stock", path: "/stock/ledger", title: "nav.stockLedger", subtitle: "tiles.stockLedger", permission: "stock.reports.read", module: "stock" },
  { id: "warehouses", group: "stock", path: "/stock/warehouses", title: "nav.warehouses", subtitle: "tiles.warehouses", permission: "stock.warehouses.read", module: "stock" },

  { id: "invoices", group: "accounting", path: "/accounting/invoices", title: "nav.invoices", subtitle: "tiles.invoices", permission: "accounting.invoices.read", module: "accounting" },
  { id: "parties", group: "accounting", path: "/accounting/parties", title: "nav.parties", subtitle: "tiles.parties", permission: "core.parties.read", module: "accounting" },
  { id: "banking", group: "accounting", path: "/accounting/banking", title: "nav.banking", subtitle: "tiles.banking", permission: "accounting.banking.read", module: "accounting" },
  { id: "journals", group: "accounting", path: "/accounting/journals", title: "nav.journals", subtitle: "tiles.journals", permission: "accounting.journals.read", module: "accounting" },
  { id: "accounts", group: "accounting", path: "/accounting/accounts", title: "nav.accounts", subtitle: "tiles.accounts", permission: "accounting.accounts.read", module: "accounting" },
  { id: "periods", group: "accounting", path: "/accounting/periods", title: "nav.periods", subtitle: "tiles.periods", permission: "accounting.reports.read", module: "accounting" },

  { id: "cashiers", group: "cash", path: "/cash/cashiers", title: "nav.cashiers", subtitle: "tiles.cashiers", permission: "cash.cashiers.read", module: "cash" },
  { id: "petty-cash", group: "cash", path: "/cash/petty-cash", title: "nav.pettyCash", subtitle: "tiles.pettyCash", permission: "cash.cashiers.read", module: "cash" },

  { id: "reports", group: "reports", path: "/accounting/reports", title: "nav.financialReports", subtitle: "tiles.reports", permission: "accounting.reports.read", module: "accounting" },
  { id: "trial-balance", group: "reports", path: "/accounting/trial-balance", title: "nav.trialBalance", subtitle: "tiles.trialBalance", permission: "accounting.reports.read", module: "accounting" },
  { id: "ageing", group: "reports", path: "/accounting/ageing", title: "nav.ageing", subtitle: "tiles.ageing", permission: "accounting.reports.read", module: "accounting" },
  { id: "tax", group: "reports", path: "/accounting/tax", title: "nav.taxReports", subtitle: "tiles.tax", permission: "accounting.reports.read", module: "accounting" },

  { id: "employees", group: "hr", path: "/employees", title: "nav.employees", subtitle: "tiles.employees", permission: "core.employees.read" },
  { id: "leave", group: "hr", path: "/hr/leave", title: "nav.leave", subtitle: "tiles.leave", permission: "hr.leave.read", module: "hr" },
  { id: "attendance", group: "hr", path: "/hr/attendance", title: "nav.attendance", subtitle: "tiles.attendance", permission: "hr.attendance.read", module: "hr" },

  { id: "run", group: "payroll", path: "/payroll/run", title: "nav.payrollRun", subtitle: "tiles.run", permission: "payroll.runs.read", module: "payroll" },
  { id: "slips", group: "payroll", path: "/payroll/slips", title: "nav.salarySlips", subtitle: "tiles.slips", permission: "payroll.salary-slips.read", module: "payroll" },
  { id: "loans", group: "payroll", path: "/payroll/loans", title: "nav.loans", subtitle: "tiles.loans", permission: "payroll.loans.read", module: "payroll" },
  { id: "additional-salary", group: "payroll", path: "/payroll/additional-salary", title: "nav.additionalSalary", subtitle: "tiles.additionalSalary", permission: "payroll.additional-salary.read", module: "payroll" },
  { id: "structures", group: "payroll", path: "/payroll/structures", title: "nav.salaryStructures", subtitle: "tiles.structures", permission: "payroll.structures.read", module: "payroll" },
  { id: "calculator", group: "payroll", path: "/payroll/calculator", title: "nav.calculator", subtitle: "tiles.calculator", permission: "payroll.calculator.use", module: "payroll" },

  { id: "companies", group: "settings", path: "/companies", title: "nav.companies", subtitle: "tiles.companies", permission: "core.companies.read" },
  { id: "cost-centers", group: "settings", path: "/cost-centers", title: "nav.costCenters", subtitle: "tiles.costCenters", permission: "core.cost-centers.read" },
  { id: "users", group: "settings", path: "/users", title: "nav.users", subtitle: "tiles.users", permission: "core.users.read" },
  { id: "roles", group: "settings", path: "/roles", title: "nav.roles", subtitle: "tiles.roles", permission: "core.roles.read" },
];

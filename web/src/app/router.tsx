import { createRootRoute, createRoute, createRouter, lazyRouteComponent } from "@tanstack/react-router";
import { validateKind } from "../pages/accounting/invoices/shared";
import { validateItem, validatePurpose } from "../pages/stock/search";
import { Layout } from "./Layout";

const root = createRootRoute({ component: Layout });

const items = createRoute({ getParentRoute: () => root, path: "/items", component: lazyRouteComponent(() => import("../pages/stock/Items"), "Items") });
const item = createRoute({ getParentRoute: () => root, path: "/items/$id", component: lazyRouteComponent(() => import("../pages/stock/Items"), "ItemDetail") });
const warehouses = createRoute({ getParentRoute: () => root, path: "/stock/warehouses", component: lazyRouteComponent(() => import("../pages/stock/Warehouses"), "Warehouses") });
const stockEntries = createRoute({ getParentRoute: () => root, path: "/stock/entries", component: lazyRouteComponent(() => import("../pages/stock/StockEntries"), "StockEntries") });
const stockEntryNew = createRoute({ getParentRoute: () => root, path: "/stock/entries/new", component: lazyRouteComponent(() => import("../pages/stock/StockEntries"), "StockEntryCreate"),
  validateSearch: validatePurpose });
const stockEntry = createRoute({ getParentRoute: () => root, path: "/stock/entries/$id", component: lazyRouteComponent(() => import("../pages/stock/StockEntries"), "StockEntryDetail") });
const stockBalance = createRoute({ getParentRoute: () => root, path: "/stock/balance", component: lazyRouteComponent(() => import("../pages/stock/StockReports"), "StockBalance") });
const stockLedger = createRoute({ getParentRoute: () => root, path: "/stock/ledger", component: lazyRouteComponent(() => import("../pages/stock/StockReports"), "StockLedger"),
  validateSearch: validateItem });

const dashboard = createRoute({ getParentRoute: () => root, path: "/", component: lazyRouteComponent(() => import("../pages/Home"), "Home") });
const companies = createRoute({ getParentRoute: () => root, path: "/companies", component: lazyRouteComponent(() => import("../pages/Companies"), "Companies") });
const employees = createRoute({ getParentRoute: () => root, path: "/employees", component: lazyRouteComponent(() => import("../pages/Employees"), "Employees") });
const companyPage = createRoute({ getParentRoute: () => root, path: "/companies/$id", component: lazyRouteComponent(() => import("../pages/Companies"), "CompanyDetail") });
const employeePage = createRoute({ getParentRoute: () => root, path: "/employees/$id", component: lazyRouteComponent(() => import("../pages/Employees"), "EmployeeDetail") });
const slips = createRoute({ getParentRoute: () => root, path: "/payroll/slips", component: lazyRouteComponent(() => import("../pages/payroll/Slips"), "Slips") });
const slip = createRoute({ getParentRoute: () => root, path: "/payroll/slips/$id", component: lazyRouteComponent(() => import("../pages/payroll/SlipDetail"), "SlipDetail") });
const run = createRoute({ getParentRoute: () => root, path: "/payroll/run", component: lazyRouteComponent(() => import("../pages/payroll/Run"), "Run") });
const calculator = createRoute({ getParentRoute: () => root, path: "/payroll/calculator", component: lazyRouteComponent(() => import("../pages/payroll/Calculator"), "Calculator") });
const leave = createRoute({ getParentRoute: () => root, path: "/hr/leave", component: lazyRouteComponent(() => import("../pages/hr/Leave"), "Leave") });
const attendance = createRoute({ getParentRoute: () => root, path: "/hr/attendance", component: lazyRouteComponent(() => import("../pages/hr/Attendance"), "Attendance") });
const cashiers = createRoute({ getParentRoute: () => root, path: "/cash/cashiers", component: lazyRouteComponent(() => import("../pages/cash/Cashiers"), "Cashiers") });
const cashier = createRoute({ getParentRoute: () => root, path: "/cash/cashiers/$id", component: lazyRouteComponent(() => import("../pages/cash/CashierDetail"), "CashierDetail") });
const pettyCash = createRoute({ getParentRoute: () => root, path: "/cash/petty-cash", component: lazyRouteComponent(() => import("../pages/cash/PettyCash"), "PettyCash") });
const accounts = createRoute({ getParentRoute: () => root, path: "/accounting/accounts", component: lazyRouteComponent(() => import("../pages/accounting/Accounts"), "Accounts") });
const journals = createRoute({ getParentRoute: () => root, path: "/accounting/journals", component: lazyRouteComponent(() => import("../pages/accounting/journals/JournalList"), "JournalList") });
const journalNew = createRoute({ getParentRoute: () => root, path: "/accounting/journals/new", component: lazyRouteComponent(() => import("../pages/accounting/journals/JournalCreate"), "JournalCreate") });
const journal = createRoute({ getParentRoute: () => root, path: "/accounting/journals/$id", component: lazyRouteComponent(() => import("../pages/accounting/journals/JournalDetail"), "JournalDetail") });
const trialBalance = createRoute({ getParentRoute: () => root, path: "/accounting/trial-balance", component: lazyRouteComponent(() => import("../pages/accounting/TrialBalance"), "TrialBalance") });
const parties = createRoute({ getParentRoute: () => root, path: "/accounting/parties", component: lazyRouteComponent(() => import("../pages/accounting/Parties"), "Parties") });
const party = createRoute({ getParentRoute: () => root, path: "/accounting/parties/$id", component: lazyRouteComponent(() => import("../pages/accounting/PartyDetail"), "PartyDetail") });
const invoices = createRoute({ getParentRoute: () => root, path: "/accounting/invoices", component: lazyRouteComponent(() => import("../pages/accounting/invoices/InvoiceList"), "InvoiceList"), validateSearch: validateKind });
const invoiceNew = createRoute({ getParentRoute: () => root, path: "/accounting/invoices/new", component: lazyRouteComponent(() => import("../pages/accounting/invoices/InvoiceCreate"), "InvoiceCreate"), validateSearch: validateKind });
const invoice = createRoute({ getParentRoute: () => root, path: "/accounting/invoices/$id", component: lazyRouteComponent(() => import("../pages/accounting/invoices/InvoiceDetail"), "InvoiceDetail") });
const taxReports = createRoute({ getParentRoute: () => root, path: "/accounting/tax", component: lazyRouteComponent(() => import("../pages/accounting/TaxReports"), "TaxReports") });
const periods = createRoute({ getParentRoute: () => root, path: "/accounting/periods", component: lazyRouteComponent(() => import("../pages/accounting/Periods"), "Periods") });
const financial = createRoute({ getParentRoute: () => root, path: "/accounting/reports", component: lazyRouteComponent(() => import("../pages/accounting/FinancialReports"), "FinancialReports") });
const receipt = createRoute({ getParentRoute: () => root, path: "/accounting/invoices/$id/receipt", component: lazyRouteComponent(() => import("../pages/accounting/WithholdingReceipt"), "WithholdingReceipt") });
const banking = createRoute({ getParentRoute: () => root, path: "/accounting/banking", component: lazyRouteComponent(() => import("../pages/accounting/Banking"), "Banking") });
const ageing = createRoute({ getParentRoute: () => root, path: "/accounting/ageing", component: lazyRouteComponent(() => import("../pages/accounting/Ageing"), "Ageing") });

export const router = createRouter({
  routeTree: root.addChildren([items, item, warehouses, stockEntries, stockEntryNew, stockEntry, stockBalance, stockLedger, dashboard, companies, companyPage, employees, employeePage, slips, slip, run, calculator, leave, attendance, cashiers, cashier, pettyCash, accounts, journals, journalNew, journal, trialBalance, parties, party, invoices, invoiceNew, invoice, taxReports, periods, financial, ageing, receipt, banking]),
});

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}

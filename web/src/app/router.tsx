import { createRootRoute, createRoute, createRouter } from "@tanstack/react-router";
import { Companies, CompanyDetail } from "../pages/Companies";
import { Home } from "../pages/Home";
import { Employees, EmployeeDetail } from "../pages/Employees";
import { Calculator } from "../pages/payroll/Calculator";
import { Run } from "../pages/payroll/Run";
import { SlipDetail } from "../pages/payroll/SlipDetail";
import { Slips } from "../pages/payroll/Slips";
import { Accounts } from "../pages/accounting/Accounts";
import { JournalList } from "../pages/accounting/journals/JournalList";
import { JournalCreate } from "../pages/accounting/journals/JournalCreate";
import { JournalDetail } from "../pages/accounting/journals/JournalDetail";
import { TrialBalance } from "../pages/accounting/TrialBalance";
import { Parties } from "../pages/accounting/Parties";
import { InvoiceList } from "../pages/accounting/invoices/InvoiceList";
import { InvoiceCreate } from "../pages/accounting/invoices/InvoiceCreate";
import { InvoiceDetail } from "../pages/accounting/invoices/InvoiceDetail";
import { validateKind } from "../pages/accounting/invoices/shared";
import { TaxReports } from "../pages/accounting/TaxReports";
import { Periods } from "../pages/accounting/Periods";
import { FinancialReports } from "../pages/accounting/FinancialReports";
import { Ageing } from "../pages/accounting/Ageing";
import { WithholdingReceipt } from "../pages/accounting/WithholdingReceipt";
import { Banking } from "../pages/accounting/Banking";
import { Cashiers } from "../pages/cash/Cashiers";
import { CashierDetail } from "../pages/cash/CashierDetail";
import { PettyCash } from "../pages/cash/PettyCash";
import { Attendance } from "../pages/hr/Attendance";
import { Leave } from "../pages/hr/Leave";
import { Layout } from "./Layout";

const root = createRootRoute({ component: Layout });

const dashboard = createRoute({ getParentRoute: () => root, path: "/", component: Home });
const companies = createRoute({ getParentRoute: () => root, path: "/companies", component: Companies });
const employees = createRoute({ getParentRoute: () => root, path: "/employees", component: Employees });
const companyPage = createRoute({ getParentRoute: () => root, path: "/companies/$id", component: CompanyDetail });
const employeePage = createRoute({ getParentRoute: () => root, path: "/employees/$id", component: EmployeeDetail });
const slips = createRoute({ getParentRoute: () => root, path: "/payroll/slips", component: Slips });
const slip = createRoute({ getParentRoute: () => root, path: "/payroll/slips/$id", component: SlipDetail });
const run = createRoute({ getParentRoute: () => root, path: "/payroll/run", component: Run });
const calculator = createRoute({ getParentRoute: () => root, path: "/payroll/calculator", component: Calculator });
const leave = createRoute({ getParentRoute: () => root, path: "/hr/leave", component: Leave });
const attendance = createRoute({ getParentRoute: () => root, path: "/hr/attendance", component: Attendance });
const cashiers = createRoute({ getParentRoute: () => root, path: "/cash/cashiers", component: Cashiers });
const cashier = createRoute({ getParentRoute: () => root, path: "/cash/cashiers/$id", component: CashierDetail });
const pettyCash = createRoute({ getParentRoute: () => root, path: "/cash/petty-cash", component: PettyCash });
const accounts = createRoute({ getParentRoute: () => root, path: "/accounting/accounts", component: Accounts });
const journals = createRoute({ getParentRoute: () => root, path: "/accounting/journals", component: JournalList });
const journalNew = createRoute({ getParentRoute: () => root, path: "/accounting/journals/new", component: JournalCreate });
const journal = createRoute({ getParentRoute: () => root, path: "/accounting/journals/$id", component: JournalDetail });
const trialBalance = createRoute({ getParentRoute: () => root, path: "/accounting/trial-balance", component: TrialBalance });
const parties = createRoute({ getParentRoute: () => root, path: "/accounting/parties", component: Parties });
const invoices = createRoute({ getParentRoute: () => root, path: "/accounting/invoices", component: InvoiceList, validateSearch: validateKind });
const invoiceNew = createRoute({ getParentRoute: () => root, path: "/accounting/invoices/new", component: InvoiceCreate, validateSearch: validateKind });
const invoice = createRoute({ getParentRoute: () => root, path: "/accounting/invoices/$id", component: InvoiceDetail });
const taxReports = createRoute({ getParentRoute: () => root, path: "/accounting/tax", component: TaxReports });
const periods = createRoute({ getParentRoute: () => root, path: "/accounting/periods", component: Periods });
const financial = createRoute({ getParentRoute: () => root, path: "/accounting/reports", component: FinancialReports });
const receipt = createRoute({ getParentRoute: () => root, path: "/accounting/invoices/$id/receipt", component: WithholdingReceipt });
const banking = createRoute({ getParentRoute: () => root, path: "/accounting/banking", component: Banking });
const ageing = createRoute({ getParentRoute: () => root, path: "/accounting/ageing", component: Ageing });

export const router = createRouter({
  routeTree: root.addChildren([dashboard, companies, companyPage, employees, employeePage, slips, slip, run, calculator, leave, attendance, cashiers, cashier, pettyCash, accounts, journals, journalNew, journal, trialBalance, parties, invoices, invoiceNew, invoice, taxReports, periods, financial, ageing, receipt, banking]),
});

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}

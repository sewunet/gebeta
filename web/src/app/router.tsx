import { createRootRoute, createRoute, createRouter } from "@tanstack/react-router";
import { Companies } from "../pages/Companies";
import { Dashboard } from "../pages/Dashboard";
import { Employees } from "../pages/Employees";
import { Calculator } from "../pages/payroll/Calculator";
import { Run } from "../pages/payroll/Run";
import { SlipDetail } from "../pages/payroll/SlipDetail";
import { Slips } from "../pages/payroll/Slips";
import { Accounts } from "../pages/accounting/Accounts";
import { Journals } from "../pages/accounting/Journals";
import { TrialBalance } from "../pages/accounting/TrialBalance";
import { Cashiers } from "../pages/cash/Cashiers";
import { PettyCash } from "../pages/cash/PettyCash";
import { Attendance } from "../pages/hr/Attendance";
import { Leave } from "../pages/hr/Leave";
import { Layout } from "./Layout";

const root = createRootRoute({ component: Layout });

const dashboard = createRoute({ getParentRoute: () => root, path: "/", component: Dashboard });
const companies = createRoute({ getParentRoute: () => root, path: "/companies", component: Companies });
const employees = createRoute({ getParentRoute: () => root, path: "/employees", component: Employees });
const slips = createRoute({ getParentRoute: () => root, path: "/payroll/slips", component: Slips });
const slip = createRoute({ getParentRoute: () => root, path: "/payroll/slips/$id", component: SlipDetail });
const run = createRoute({ getParentRoute: () => root, path: "/payroll/run", component: Run });
const calculator = createRoute({ getParentRoute: () => root, path: "/payroll/calculator", component: Calculator });
const leave = createRoute({ getParentRoute: () => root, path: "/hr/leave", component: Leave });
const attendance = createRoute({ getParentRoute: () => root, path: "/hr/attendance", component: Attendance });
const cashiers = createRoute({ getParentRoute: () => root, path: "/cash/cashiers", component: Cashiers });
const pettyCash = createRoute({ getParentRoute: () => root, path: "/cash/petty-cash", component: PettyCash });
const accounts = createRoute({ getParentRoute: () => root, path: "/accounting/accounts", component: Accounts });
const journals = createRoute({ getParentRoute: () => root, path: "/accounting/journals", component: Journals });
const trialBalance = createRoute({ getParentRoute: () => root, path: "/accounting/trial-balance", component: TrialBalance });

export const router = createRouter({
  routeTree: root.addChildren([dashboard, companies, employees, slips, slip, run, calculator, leave, attendance, cashiers, pettyCash, accounts, journals, trialBalance]),
});

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}

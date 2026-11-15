import { createRootRoute, createRoute, createRouter } from "@tanstack/react-router";
import { Companies } from "../pages/Companies";
import { Dashboard } from "../pages/Dashboard";
import { Employees } from "../pages/Employees";
import { Calculator } from "../pages/payroll/Calculator";
import { Run } from "../pages/payroll/Run";
import { SlipDetail } from "../pages/payroll/SlipDetail";
import { Slips } from "../pages/payroll/Slips";
import { Layout } from "./Layout";

const root = createRootRoute({ component: Layout });

const dashboard = createRoute({ getParentRoute: () => root, path: "/", component: Dashboard });
const companies = createRoute({ getParentRoute: () => root, path: "/companies", component: Companies });
const employees = createRoute({ getParentRoute: () => root, path: "/employees", component: Employees });
const slips = createRoute({ getParentRoute: () => root, path: "/payroll/slips", component: Slips });
const slip = createRoute({ getParentRoute: () => root, path: "/payroll/slips/$id", component: SlipDetail });
const run = createRoute({ getParentRoute: () => root, path: "/payroll/run", component: Run });
const calculator = createRoute({ getParentRoute: () => root, path: "/payroll/calculator", component: Calculator });

export const router = createRouter({
  routeTree: root.addChildren([dashboard, companies, employees, slips, slip, run, calculator]),
});

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}

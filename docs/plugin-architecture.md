# Plugin architecture

**Every business module runs on its own.** Installing another module next to it **adds features**;
removing it takes those features away again without breaking anything.

```
            ┌──────────── Core (always installed) ────────────┐
            │ auth, users, roles, companies, branches,        │
            │ departments, cost centres, currencies,          │
            │ basic employee / party / item directory,        │
            │ naming series, workflow, audit, files, branding │
            └─────────────────────────────────────────────────┘
   ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌──────────┐ ┌─────────┐ ┌───────────────┐
   │   HR    │ │ Payroll │ │  Cash   │ │Accounting│ │  Stock  │ │ Manufacturing │  ... plugins
   └─────────┘ └─────────┘ └─────────┘ └──────────┘ └─────────┘ └───────────────┘
        any combination: one module alone, a few, or all of them
```

The only hard dependency is **Core**. No business module references another business module's code.

## 1. How modules connect

Four mechanisms. A module uses only these to work with other modules.

### 1.1 Integration events (publish / subscribe)

A module announces facts: `SalarySlipSubmitted`, `AttendanceFinalized`, `JournalRequested`.
Any installed module may subscribe. If nobody subscribes, nothing happens and nothing breaks.

- Published through the **outbox** in the same transaction as the change; consumed through an
  **inbox** that ignores duplicates (every handler is idempotent).
- Transport: in-process when modules share one host; RabbitMQ when they run as separate
  services. Module code is the same in both cases.
- Event contracts live in small `*.Contracts` packages, versioned; adding fields is allowed,
  removing or renaming needs a new event version.

### 1.2 Capabilities with built-in fallbacks

When a module needs an answer **now** (not just a notification), it asks for a capability
interface defined in Core. Each capability has a simple fallback inside the asking module, used
when no other module provides it.

| Capability (in Core) | Asked by | Provided by (when installed) | Fallback when not installed |
|---|---|---|---|
| `IPaymentDaysProvider` | Payroll | HR (attendance + unpaid leave) | Payment days entered or imported in Payroll |
| `IJournalSink` | Payroll, Cash, Stock, Manufacturing, HR (advances, per diem) | Accounting, or the ERPNext connector | Journals kept in the module's outbound journal list, exported to Excel/CSV |
| `IAccountDirectory` | every module that stores accounts | Accounting | Free-text account codes |
| `IStockService` | Manufacturing, Buying, Selling | Stock | Quantities recorded only; no stock ledger |
| `IItemValuation` | Manufacturing (costing) | Stock | Standard cost entered on the item |
| `IEmployeeDeductions` | Payroll | HR (advances, healthcare), Cash (staff loans) | Deductions entered as additional salary |
| `IBudgetCheck` | Buying, HR (per diem) | Accounting (budgets) | No budget check |

Rule: a module must be fully usable with every fallback. Providers only make it richer.

### 1.3 Extension data instead of foreign columns

A module never adds columns to another module's tables. It keeps its own table keyed by the
other record's id, for example:

- Payroll: `payroll.employee_profile (employee_id, salary_structure, bank_account, tax_id, ...)`
- Cash Management: `cash.cashier (employee_id, ...)`
- Stock: `stock.item_settings (item_id, valuation_method, has_batch, ...)`

Core's employee, party and item records stay small; each module adds what it needs.

### 1.4 UI extension slots

The React shell defines named slots. A module's frontend fills slots of other screens, only
when both are installed.

| Slot | Example |
|---|---|
| `sidebar` | Each module adds its workspace |
| `form.tabs:<doctype>` | Payroll adds a **Salary** tab to the Employee form; Cash adds a **Cashier** tab |
| `form.actions:<doctype>` | Accounting adds **View journal** to a salary slip or petty cash request |
| `form.fields:<doctype>` | Accounting turns free-text account codes into account pickers |
| `list.columns:<doctype>` | HR adds Leave balance to the employee list |
| `dashboard.widgets` | Number cards and charts per module |
| `selfservice.cards` | HR adds Leave; Payroll adds Payslips |
| `report.links:<module>` | Cross-module reports appear only when both modules exist |

## 2. Module manifest

### Backend

```csharp
public sealed class PayrollModule : IErpModule
{
    public ModuleInfo Info => new(Id: "payroll", Name: "Payroll", Version: new(1, 0, 0));

    public IReadOnlyList<string> Requires => ["core"];                    // hard
    public IReadOnlyList<string> Enhances => ["hr", "accounting", "cash"]; // optional

    public void Register(IServiceCollection services, IConfiguration config)
    {
        services.AddPayrollDomain();
        services.AddFallback<IPaymentDaysProvider, ManualPaymentDaysProvider>();
        services.AddFallback<IJournalSink, OutboundJournalSink>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapPayrollApi();

    public void Subscribe(IEventSubscriptions events)
    {
        events.On<JournalPosted>(JournalPostedHandler.Handle);      // only fires if Accounting exists
        events.On<EmployeeSeparated>(FinalSettlementHandler.Handle); // only fires if HR exists
    }
}
```

- `AddFallback` registers the fallback only if no other installed module registered the capability.
- The host loads the modules listed in configuration:

```json
{ "Modules": { "Enabled": ["core", "hr", "payroll"] } }
```

- Each module has its own database schema and migrations. Installing a module runs its
  migrations; uninstalling keeps its data (archived), so re-installing restores it.
- On start-up the host logs which capability came from which module, and `/api/modules` returns
  the installed modules and capabilities to the frontend.

### Frontend

Each module is an npm package (`@gebeta/payroll-web`) exporting a manifest:

```ts
export const payrollWeb: WebModule = {
  id: "payroll",
  routes: payrollRoutes,
  sidebar: { label: "Payroll", icon: "wallet" },
  slots: {
    "form.tabs:employee": { requires: [], component: EmployeeSalaryTab },
    "form.actions:salary-slip": { requires: ["accounting"], component: ViewJournalButton },
    "selfservice.cards": { requires: [], component: MyPayslipsCard },
  },
};
```

The shell includes all module packages at build time and enables only those returned by
`/api/modules`. A slot with `requires` renders only when those modules are installed too.

## 3. Deployment options

The same module packages support three set-ups:

| Set-up | When | Notes |
|---|---|---|
| **One host, chosen modules** (default) | Most customers | One process, one database; events in-process; simplest to run |
| **One module alone** | A customer buys only Payroll or only HR | Core + that module |
| **Separate services** | Large customers, a module needs its own scaling | Each module in its own process (still one Core); events over RabbitMQ; each module may have its own database |

## 4. Consistency between modules

Cross-module effects are **eventual**: a submitted salary slip creates its GL journal a moment
later, when Accounting handles `JournalRequested`. This keeps modules independent. To stay safe:

- Each module's own data is always consistent (one transaction inside the module).
- Rules that must be checked **immediately** live inside one module. Example: the cashier
  overdraw check runs on Cash Management's own cashier ledger, not on the GL.
- Every cross-module flow has a status the user can see (e.g. journal *Pending → Posted*, or
  *Failed* with the reason) and a retry.
- Reconciliation jobs compare the two sides daily (cashier ledger vs. GL cash accounts, stock
  value vs. GL stock accounts, payroll totals vs. payroll journal).

## 5. What each module does alone and what plugging adds

| Module | Alone | + HR | + Payroll | + Accounting | + Cash | + Stock | + Manufacturing |
|---|---|---|---|---|---|---|---|
| **HR** | Employees, org, attendance, leave, training, promotion, separation, self-service | | Salary tab on employee, payslips in self-service, final settlement on separation | Advances, per diem and expense claims post journals; budget check | Advances and per diem paid out by cashiers | | |
| **Payroll** | Structures, formulas, tax, slips, back payment, payslips, bank file, journal export | Payment days from attendance and leave; deductions for advances and healthcare; grade/step drive salary | | Payroll journal posted to GL automatically; account pickers | Salaries paid in cash by cashiers | | |
| **Accounting** | GL, invoices, payments, tax, banking, budgets, closing, reports | Employee as party for advances and claims | Payroll journals arrive automatically | | Cashier dimension and cash movements arrive automatically | Perpetual inventory journals; stock accounts | WIP and variance journals |
| **Cash Management** | Cashiers, own cashier ledger with overdraw check, petty cash requests, top-ups, transfers, journal export | Cashier linked to employee | Cash salary payments | Journals posted to GL; source-account picker; daily reconciliation | | | |
| **Stock** | Items, warehouses, stock ledger, FIFO / moving average, batches, serials, reposting | Issues to employees | | Stock value journals and reposting adjustments posted to GL | | | Material transfer, consumption, finished goods |
| **Manufacturing** | BOMs, work orders, job cards, workstations (parallel), capacity, maintenance, cost estimates from standard cost | Operators from employees, time logs per employee | Piece-rate or overtime from job cards | WIP and variance journals | | Real material reservation, transfer, consumption and actual costing | |

## 6. Rules for module developers

1. Reference only Core and other modules' `*.Contracts` packages. An architecture test fails the
   build otherwise.
2. Every capability you ask for must have a fallback in your own module.
3. Every event handler must be idempotent and must tolerate events arriving late or twice.
4. Never read another module's tables, even for reports. Cross-module reports subscribe to events
   and build their own read model, or call the other module's query API.
5. CI runs each module's test suite in three set-ups: **alone**, **with its usual partners**, and
   **all modules**.

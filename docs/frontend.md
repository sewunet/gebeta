# Frontend

> **Built so far** (`web/`): Keycloak sign-in (authorization code + PKCE, tokens in memory), Fiori-style shell (see
> below) built from installed modules and permissions, company switcher, English/Amharic and calendar
> preference saved on the user profile, Ethiopian dates and birr formatting, and screens for home,
> companies, employees (list and add), users (list, invite, roles, company access, deactivate) and roles
> (permissions, who holds them), salary slips (list by month, detail, submit/cancel), payroll run
> with CSV report downloads, salary structures (list, create with a try-it calculation, details) and each
> employee's salary assignments with pay values, the salary calculator; HR leave (apply, approve, cancel, balances, annual
> allocation) and attendance; Cash cashiers (balances, receipts, payments, transfers, ledger) and petty
> cash approval; Accounting chart of accounts, journals (list, details, balanced entry form) and trial balance; customers and suppliers, sales and purchase invoices with VAT and withholding (submit, cancel, receive or pay, credit and debit notes, payment history with cancel), receivables and payables ageing, open invoices at cut-over, banks (accounts, CSV statement import, reconciliation, booking charges and interest from the statement), one payment for several invoices, VAT and withholding report, profit and loss, balance sheet, general ledger, and closing (month close and reopen, year-end closing, opening balances). Styling is plain CSS with the Gebeta palette for
> now; shadcn/ui and Tailwind can come later. The OpenAPI client is hand-typed in `src/lib/types.ts`
> until client generation is set up.
>
> **UI pattern: SAP Fiori floorplans** (chosen over Odoo and NetSuite styles; layout and interaction only, with
> Gebeta's own colors, type and icons; no SAP fonts, icons or branding). Building blocks in `src/ui/fiori.tsx`:
> - **Shell bar** across the top (menu, Gebeta, current app, company, user menu with language, calendar and
>   sign-out) and a **navigation menu** that opens over the page; both, and the home tiles, come from the app
>   registry `src/app/apps.ts`.
> - **Launchpad** home: tiles grouped by area, key tiles with a live figure (receivables, payables, VAT this
>   month, lock date, bank accounts, employees).
> - **List report**: filter bar in the page header, a table card with its own toolbar (title with count,
>   actions), rows that open the object page; tables collapse to label/value cards on phones.
> - **Object page**: header with title, status and key figures, actions top right, an anchor bar jumping to
>   sections; the header shrinks on scroll. Create and edit pages put Save and Cancel in a **footer bar**.
> - **Dialogs** for short tasks (pay, credit note), **message strips** for results, **object status** colors
>   (positive, critical, negative, information).
>
> Every screen uses it: Accounting (invoices and journals as list report, create page and object page; customers
> and suppliers, chart of accounts, trial balance, financial reports, ageing, banks, VAT and withholding, closing),
> Payroll (salary slips and slip object page, the payroll run as a three-step process, the calculator), HR (leave
> and attendance with dialogs), Cash (cashiers with a cashier object page, petty cash approve and reject
> dialogs) and Core (companies and employees with object pages). The withholding receipt keeps its print layout.
>
> **Edit mode** (step 27): companies (name, VAT registration, trade licence), employees (names, gender, joining
> date, TIN; record or take back a leaving) and customers or suppliers (with their own object page listing their
> invoices and open amounts) are edited in place: Edit in the header, fields become inputs, Save and Cancel in the
> footer bar. Every page is loaded on first use (`lazyRouteComponent`), so the first download stays small.
>
> **Stock screens** (step 29): items (list, create dialog, item page with edit mode, stock per warehouse and the
> valuation method), warehouses, stock entries (list report; create page per purpose with Save and post or Save as
> draft; object page with post and cancel), stock balance (filter by warehouse, click through to the ledger) and
> stock ledger per item. A Stock group on the home page shows the stock value.
>
> **Purchase orders** (step 32): list report with fulfilment percentages and state, create page, object page with
> received and billed quantities per line; Receive goods and Record supplier invoice open the stock receipt and the
> purchase invoice pre-filled from the order (`?po=<id>`).
>
> **Sales orders** (step 33): the same pattern; Deliver goods and Create sales invoice open the stock issue and the
> sales invoice pre-filled from the order (`?so=<id>`).
> Data hooks shared between screens (parties, items, warehouses, bank accounts, cashiers, orders) live in
> `src/lib/queries.ts`; screens never import each other.

React 19 + TypeScript (strict) + Vite, in `web/`.

## Structure

```
web/src/
├─ app/                # router, providers, layout (sidebar, top bar)
├─ api/                # generated OpenAPI client (do not edit)
├─ components/         # shared UI: DataTable, DocumentForm, MoneyInput, LinkField, StatusBadge
├─ modules/
│  ├─ accounting/
│  ├─ stock/
│  ├─ hr/
│  ├─ payroll/
│  └─ cash-management/
│     ├─ routes.tsx
│     ├─ petty-cash-request/  (List.tsx, Form.tsx, schema.ts)
│     └─ cashier/
├─ lib/                # auth, permissions, formatting (money, dates, Ethiopian calendar)
└─ i18n/               # en.json, am.json
```

## Shared building blocks

| Component | What it does |
|---|---|
| `DocumentForm` | Header with name, status badge and lifecycle buttons (Save, Submit, Cancel, Amend) driven by the user's permissions; sections and tabs; child tables |
| `DataTable` | Server-side paging, filters, sort, saved views, column picker, export |
| `LinkField` | Searchable select for linked records (employee, account, item), with server-side search and filters |
| `ChildTable` | Editable rows with keyboard navigation |
| `MoneyInput` / `QtyInput` | Exact decimal input; currency shown from the document |
| `ApprovalBar` | Workflow actions for the current state |
| `Timeline` | Comments, changes, attachments |

Screens are written per document. There is no generic metadata-driven form, except for
rendering **custom fields** inside a "More fields" section.

## Apps by role

The UI is split into focused workspaces, not one giant menu:
- **Desk** for accountants, HR, store keepers (full lists and forms).
- **Self-service** for employees: payslips, leave, attendance, expense and per diem requests.
- **Cashier** screens: balances, petty cash requests, top-ups, transfers.
- **Approvals inbox** for managers, across all modules.

## Rules

- Server state only through TanStack Query; no copying server data into global stores.
- Forms validated with Zod schemas that mirror the server validators; the server is still the
  final check.
- Permissions come from the API (`/api/me/permissions`) and only hide or disable UI; the server enforces them.
- All text through i18next; no hard-coded strings.
- Accessible by default: keyboard navigation, focus states, labels (Radix primitives help).
- Theming with CSS variables fed from branding settings (colours, logos), as in `brand_theme` today.

## Auth

- OIDC Authorization Code + PKCE against Keycloak (`oidc-client-ts`).
- Access token in memory, silent refresh; never in localStorage.

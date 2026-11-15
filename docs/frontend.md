# Frontend

> **Built so far** (`web/`): Keycloak sign-in (authorization code + PKCE, tokens in memory), layout with
> sidebar from installed modules and permissions, company switcher, English/Amharic and calendar
> preference saved on the user profile, Ethiopian dates and birr formatting, and screens for dashboard,
> companies, employees (list and add), salary slips (list by month, detail, submit/cancel), payroll run
> with CSV report downloads, the salary calculator; HR leave (apply, approve, cancel, balances, annual
> allocation) and attendance; Cash cashiers (balances, receipts, payments, transfers, ledger) and petty
> cash approval; Accounting chart of accounts, journals (list, details, balanced entry form) and trial balance; customers and suppliers, sales and purchase invoices with VAT and withholding (submit, cancel, receive or pay, credit and debit notes, payment history with cancel), receivables and payables ageing, open invoices at cut-over, banks (accounts, CSV statement import, reconciliation, booking charges and interest from the statement), one payment for several invoices, VAT and withholding report, profit and loss, balance sheet, general ledger, and closing (month close and reopen, year-end closing, opening balances). Styling is plain CSS with the Gebeta palette for
> now; shadcn/ui and Tailwind can come later. The OpenAPI client is hand-typed in `src/lib/types.ts`
> until client generation is set up.

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

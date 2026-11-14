# Cash Management module

Rebuilds the current `cash_management` app: cashiers, their balances, and petty cash funding.

## Concepts

| Entity | Fields (from the current app) |
|---|---|
| **Cashier** | ID, name, company, status (Active / Suspended / Closed), **cashier type** (Sales Cashier / Petty Cashier / Other), employee, user, auto top-up, max transaction, max withdrawal, max deposit, default cost centre, current balance |
| **Cash accounting settings** (per company) | Enable automatic requests, minimum balance, target balance, default cash account |
| **Petty Cash Request** (document, `PCR-.YYYY.-`) | Company, cashier, posting date, status (Draft / Pending / Approved and Paid / Rejected / Cancelled), origin (Manual / System), current/minimum/target balance, requested amount, **funding from** (Source Account / Cashier), source account, funding cashier, cash account, journal entry, approved by/at, rejection reason |

## Cashier balance

- Cash Management keeps its **own cashier ledger** (`cash.cashier_ledger_entry`, append-only):
  one row per cash movement (receipt, payment, transfer in/out, top-up), with the cash account code
  and the source document.
- A cashier's balance = sum of that ledger for the cashier. This works with or without Accounting.
- Several cashiers can share one cash account; their balances stay separate.
- Validation runs inside the module, in the same transaction as the movement: a movement may
  **not** take a cashier's own balance below zero. Only that cashier's balance is checked, not the
  whole cash account.
- When Accounting is installed, every movement also sends a `JournalRequested` with the cashier
  as a dimension, and a daily job checks that each cashier's ledger equals the GL cash account
  lines for that cashier. GL lines with different cashiers are never merged (see
  [accounting.md](accounting.md)).
- Per-transaction limits (max transaction / withdrawal / deposit) are checked on payments.
- Payments made by a cashier record which cashier paid (`cashier`) and, for transfers, which
  cashier received (`paid_to_cashier`).

## Petty cash funding

```
Draft ──submit──► Pending ──approve──► Approved and Paid (journal entry posted)
                     └────reject─────► Rejected (reason required)
```

On approval the approver picks the cash account and the funding:

| Funding from | Journal entry |
|---|---|
| **Source Account** (bank or other non-cash account; not Cash, Receivable or Payable) | Dr cash account (cashier = requester) / Cr source account |
| **Cashier** (another active cashier of the same company) | Dr cash account (cashier = requester) / Cr cash account (cashier = funding cashier). The funding cashier's balance is validated. |

The amount is always the requested amount.

## Automatic top-ups

- Only for cashiers of type **Petty Cashier** with auto top-up on, and only when the company
  setting is enabled.
- After each posting that lowers a petty cashier's balance (and in a daily job for all of them),
  if balance < minimum balance and there is no open request, create a System request for
  `target balance − current balance`.
- Sales Cashiers are never topped up automatically; their cash goes the other way (deposits to bank).

## Standalone and plugged in

**Alone** (on Core): cashiers, the cashier ledger with the overdraw check, limits, petty cash
requests with approval, top-ups and transfers. Source and cash accounts are entered as codes;
approved requests and movements appear in an **outbound journal** list that can be exported to
Excel for another accounting system.

**When plugged in with:**

| Module | Adds |
|---|---|
| Accounting | Journals posted to the GL automatically (status Pending → Posted on each request); account pickers for source and cash accounts; daily cashier-vs-GL reconciliation; cashier ledger report shows the GL journal |
| HR | Cashier linked to an employee record; employee advances and per diem paid out by a cashier |
| Payroll | Salaries paid in cash: the payroll cash list becomes cashier payments |
| ERPNext connector (during the move) | Journals posted to ERPNext instead (see [erpnext-integration.md](../erpnext-integration.md)) |

## Screens

- Cashier dashboard: balance per cashier, today's movements, pending requests.
- Petty cash request form with Approve (dialog: cash account, funding from, source account or
  funding cashier) and Reject (reason).
- Cashier ledger report (per cashier, with running balance).

## Tests that must exist

- Two cashiers on one cash account: a transfer between them gives two GL lines, not zero.
- A posting that would overdraw a cashier is refused; another cashier on the same account is not affected.
- Auto top-up creates one request only, for petty cashiers only.
- Approve from source account and from cashier; cancel reverses the journal.

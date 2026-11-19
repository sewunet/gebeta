# Buying module

> **Built so far** (step 32, `src/Modules/Buying`): purchase orders (`PO-<FY>-#####`) to a supplier with item lines
> (quantity, rate, suggested warehouse), a required-by date, submit, close or reopen (nothing more expected), and
> cancel (only before anything is received or billed). Each order shows, per line and in total, what has been
> received and billed, and its state: draft, to receive and bill, to receive, to bill, completed, closed, cancelled.

## Fulfilment without reaching into other modules

Buying keeps no copies of receipts or invoices. It asks the modules that hold them, through capabilities defined in
`Gebeta.Buying.Contracts` (docs/plugin-architecture.md):

| Capability | Provided by | Counts |
|---|---|---|
| `IPurchaseReceiptLookup` | Stock | submitted stock receipts whose reference is the order number |
| `IPurchaseBillingLookup` | Accounting | submitted purchase invoices whose order reference is the order number (debit notes negative) |

Without Stock or Accounting the empty fallbacks answer "nothing received" or "nothing billed", so Buying runs on Core
alone. The web app's **Receive goods** opens a stock receipt filled from the order (supplier, order number as the
reference, what is still to come); **Record supplier invoice** opens a purchase invoice filled with what is not billed
yet, on 2150 for stock items (cleared against the goods received).

## Landed costs

Landed costs (freight, duty, clearing, LC charges) are kept by Stock against the goods receipt, not by Buying: they
change stock value, and Buying keeps no copy of receipts. Nothing in Buying changes; the forwarder's invoice is an
ordinary purchase invoice whose line debits 2150, which clears the charge (docs/modules/stock.md).

## Not yet

Material requests, supplier quotations, approval rules, over-receipt tolerance, returns to supplier against an order,
price lists, and the matching mirror on the selling side (sales orders, deliveries, sales invoices).

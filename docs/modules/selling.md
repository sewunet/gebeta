# Selling module

> **Built so far** (step 33, `src/Modules/Selling`): sales orders (`SO-<FY>-#####`) from a customer with item lines
> (quantity, rate, warehouse the goods come from), a delivery date, submit, close or reopen, and cancel (only before
> anything is delivered or invoiced). Each order shows, per line and in total, what has been delivered and invoiced,
> and its state: draft, to deliver and invoice, to deliver, to invoice, completed, closed, cancelled.

## Fulfilment

The mirror of Buying (docs/modules/buying.md), through capabilities in `Gebeta.Selling.Contracts`:

| Capability | Provided by | Counts |
|---|---|---|
| `ISalesDeliveryLookup` | Stock | submitted stock issues whose reference is the order number |
| `ISalesBillingLookup` | Accounting | submitted sales invoices whose order reference is the order number (credit notes negative) |

The web app's **Deliver goods** opens a stock issue filled from the order (customer, order number, what is still to
deliver, cost of sales as the counter account); **Create sales invoice** opens a sales invoice with what is not
invoiced yet.

## Not yet

Quotations, price lists and discounts, credit limits, reserving stock for an order, delivery returns against an order,
and point-of-sale.

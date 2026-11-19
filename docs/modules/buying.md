# Buying module

> **Built so far** (step 32; framework agreements step 61, `src/Modules/Buying`): purchase orders (`PO-<FY>-#####`) to a supplier with item lines
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

## Framework agreements

> **Built (step 61).** A framework (blanket) agreement is an agreement with one supplier for a period: items at agreed
> rates, capped by a planned quantity per item, an amount for the whole agreement, or both. Purchase orders call off
> against it and it shows what remains.

- **Document:** `BPA-<FY>-#####`, draft then submitted, or cancelled. Fields: supplier, agreement date, valid from and
  to (both days inclusive), optional amount limit, and lines (item, agreed rate, optional planned quantity). It needs
  an amount limit, or a planned quantity on every line, so there is always something to call off against.
- **Calling off:** a purchase order carries an optional `agreementId` (`POST /api/buying/purchase-orders`). It is
  refused when the agreement is not submitted, the supplier differs, the order date is outside the period, a line's item
  is not on the agreement or its rate is not the agreed rate, a line is above the item's remaining quantity, or the
  order's total is above the remaining amount. It is checked when the order is written (early feedback) and again on
  submit, under a row lock on the agreement (`SELECT ... FOR UPDATE` in the submit transaction), so two orders cannot
  overdraw it together.
- **Balance is derived, not stored:** called off = the lines of the agreement's **submitted** orders (closed ones too);
  remaining = agreed - called off, never shown below zero. Nothing is kept on the agreement, so **cancelling an order
  gives its share back** with no extra step, and a draft holds nothing. The agreement shows, per item, quantity and
  amount called off and remaining, the total amount and remaining, and the list of orders.
- **State:** Draft, Active, Exhausted (amount used up, or every item at its planned quantity), Expired (after the last
  day), Cancelled. Expiry is worked out from today's date (Addis Ababa), not stored.
- **Cancel:** refused while any submitted order calls off against it (cancel those first); takes the same row lock.
- **Related documents and attachments:** an order lists its agreement and an agreement its orders under related
  documents (read permission `buying.agreements.read`); files on an agreement follow `buying.agreements.read` to open and
  `buying.agreements.create` to add or remove (`IRecordPermissions`).
- **Permissions:** `buying.agreements.read`, `.create`, `.submit` (the Purchaser role has them through `buying.*`;
  Accountant and Store Keeper can read).
- **Screens:** Buying, **Framework agreements** (list, create, object page with a **Create purchase order** action that
  opens a new order with the agreement's items and rates filled in). The order page shows its agreement.
- **Buying alone:** nothing here uses another module.

Decisions you may want to revisit:
1. **Rate must equal the agreed rate** (not just "not above"). Strict and simple; a tolerance or "price not above" rule
   is a small change in `BlanketAgreement.CheckCallOff`.
2. **A closed order still counts** what it ordered (closing an order does not release the unreceived part).
   Cancel to release; cancelling is refused once goods are received or billed, as for any order.
3. **Only submitted orders count**, so many drafts can be written against the same balance; the lock on submit is what
   stops overdrawing.
4. **Quantities are in the item's unit**; the amount cap uses order line amounts (rounded to the currency).
5. **Draft agreements cannot be edited** (no edit screen for any Buying document yet): cancel is only for submitted
   ones, so a mistaken draft is left unused. Same as purchase orders today.

## Not yet

Scheduled releases (delivery dates and quantities within an agreement), price breaks by quantity, renewal or amendment
of an agreement, an expiry warning, agreement terms and a tender that issues one (Phase 5), and the selling-side mirror.
Also:

Material requests, supplier quotations, approval rules, over-receipt tolerance, returns to supplier against an order,
price lists, and the matching mirror on the selling side (sales orders, deliveries, sales invoices).

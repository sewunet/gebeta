# Stock module

## Scope

| Area | Contents |
|---|---|
| Masters | Item, item group, UOM and conversions, warehouse tree, batch, serial number, item specification templates (current custom) |
| Documents | Material Request (with request types), Purchase Receipt, Delivery Note, Stock Entry (receipt, issue, transfer, manufacture, repack), Stock Reconciliation, Material Receipt for Transfer, Transport Order (current custom) |
| Valuation | FIFO, Moving Average (per item, chosen once) |
| Ledger | Stock ledger, linked to GL stock accounts (perpetual inventory) |
| Reports | Stock balance, stock ledger, ageing, valuation, projected quantity, reorder |

## The stock ledger

```
stock.stock_ledger_entry   (append-only, except the computed columns below)
  id, company_id, item_id, warehouse_id, batch_id, posting_date, posting_time, sequence,
  actual_qty, incoming_rate, outgoing_rate,
  -- computed by the valuation engine:
  qty_after_transaction, valuation_rate, stock_value, stock_value_difference, fifo_queue jsonb,
  voucher_type, voucher_id, voucher_line_id, is_cancelled
```

The computed columns are the one exception to "append-only": the reposting engine rewrites them.
The movement itself (`actual_qty`, voucher) never changes.

## Valuation engine

- Pure domain code: `IValuationMethod.Apply(state, movement) → (newState, outgoingRate)`.
  FIFO state is a queue of `(qty, rate)` batches; Moving Average state is `(qty, value)`.
- Every outgoing movement gets its rate from the engine, never from the user.
- The stock value difference of each voucher is sent as `JournalRequested` (stock account vs.
  expense/COGS/WIP). With Accounting installed it is posted to the GL; otherwise it is kept for export.

## Backdated entries and reposting

Posting or cancelling a movement dated in the past changes every later rate for that item and
warehouse. Process:

1. The submitting transaction writes the new movement and creates a `repost_request`
   (item, warehouse, from date/time/sequence).
2. A Hangfire job takes a lock per `(item, warehouse)`, recomputes every later ledger row in order,
   and collects the vouchers whose stock value changed.
3. For those vouchers it sends **adjustment** journals (`JournalRequested` with the difference),
   so the GL stays append-only and matches the stock ledger.
4. Transfers and manufacturing entries create follow-up repost requests for the target warehouse
   or the finished item.
5. Progress and failures are visible on a reposting page; stock reports show a warning while
   reposting is pending for an item.

Rules: future-dated stock postings are not allowed; negative stock is off by default (per company setting).

## Batches and serial numbers

- Batch: expiry, manufacturing date, own valuation optional.
- Serial number: one row per unit, status (in stock, delivered, in maintenance), full history.
- Selection on transactions through a bundle child table (like ERPNext v15's Serial and Batch Bundle).

## Standalone and plugged in

**Alone** (on Core): items (Core item + Stock's own item settings), warehouses, all stock
documents, the stock ledger with valuation, batches, serials and reposting. Stock value journals
are kept for export.

**When plugged in with:**

| Module | Adds |
|---|---|
| Accounting | Perpetual inventory: value journals and reposting adjustments posted automatically; stock-vs-GL daily reconciliation |
| Manufacturing | Stock provides `IStockService` and `IItemValuation`: real reservation, transfer to WIP, consumption, finished goods at actual cost |
| HR | Issues to employees; outstanding items blocked on clearance |
| Buying / Selling | Receipts and deliveries from purchase and sales orders |

## Tests that must exist

- Golden tests: sequences of receipts/issues with known FIFO and moving-average results.
- Backdated receipt → all later rates and GL adjustments are correct.
- Stock ledger value per account equals GL balance of the stock account (daily check job too).

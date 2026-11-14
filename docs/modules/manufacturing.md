# Manufacturing module

Runs alone on Core; Stock and Accounting make it much richer (see below). Built last.

## Standalone and plugged in

**Alone** (on Core): BOMs, work orders, job cards, workstations (including parallel
workstations), capacity planning, maintenance. Materials are recorded as quantities on the work
order (planned vs. used), and costs are estimated from the standard cost on the item.

**When plugged in with:**

| Module | Adds |
|---|---|
| Stock | Real material reservation, transfers to WIP, consumption and finished goods entries; actual costing from stock valuation (`IStockService`, `IItemValuation`) |
| Accounting | WIP, operating cost absorption and variance journals posted automatically |
| HR | Operators picked from employees; time logs per employee |
| Payroll | Piece-rate and overtime earnings from job-card time logs |

## Scope

| Area | Contents |
|---|---|
| Masters | BOM (multi-level, operations, scrap, by-products), workstation, workstation operating cost components (current custom), routing |
| Documents | Production Plan, Work Order, Job Card, Stock Entry (material transfer, manufacture), Maintenance Request (current custom) |
| Costing | Raw materials + operating cost + overhead → finished goods valuation |
| Reports | Production analytics, work order summary, BOM cost comparison, workstation load |

## BOM

- Tree of items with quantities per unit of output; operations with workstation and time.
- Cost roll-up from the current valuation rate, last purchase rate or a price list.
- Versioned: a work order keeps the BOM version it was created with.

## Work order flow

```
Work Order (submitted)
  → reserve materials
  → Material Transfer to WIP warehouse      (stock: store → WIP)
  → Job Cards per operation (time logs)
  → Manufacture entry                       (stock: WIP → finished goods, scrap)
       finished goods value = materials consumed + operating cost (from job cards) + additional costs
  → close (variance posted to GL)
```

## Job cards and workstations

- One job card per operation, with time logs per employee and workstation.
- **Parallel workstations** (current customisation in `customized_forcommon`): an operation can run
  on several machines of the same type at once. Each job card gets the full operation time per
  unit; time must **not** be divided again by the number of machines when job cards are created.
  (This was a real bug, fixed in the current system in commit `b510d57`; keep a test for it.)
- Workstation operating cost is built from components (electricity, labour, depreciation) per hour.

## Tests that must exist

- Multi-level BOM cost roll-up.
- Manufacture entry valuation = materials + operating cost, and GL matches.
- Parallel workstation job-card times (regression for the bug above).

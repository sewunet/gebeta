# Messaging with RabbitMQ

All integration events between modules go through **RabbitMQ**, in every set-up: one container
with all modules, one module alone, or every module in its own container. Module code never
knows which set-up it runs in ([ADR 0007](adr/0007-rabbitmq-and-docker.md)).

## Implementation

Gebeta's own small messaging layer on the official **RabbitMQ.Client** (7.x), in two building blocks:

| Piece | Where | What it does |
|---|---|---|
| Outbox and inbox tables | `Gebeta.Persistence` (`AddOutboxAndInbox`) | `outbox_message` and `inbox_message` in each module's own schema, created by the module's migrations |
| `IUnitOfWork.Publish(event)` | `Gebeta.Application` | Adds the event to the module's outbox; saved by the same `SaveChangesAsync` as the data |
| Outbox dispatcher | `Gebeta.Messaging` (`AddOutboxDispatcher<TDbContext>`) | Every second, takes pending rows with `FOR UPDATE SKIP LOCKED`, publishes with publisher confirms, marks them sent |
| Consumer | `Gebeta.Messaging` (`AddIntegrationEventConsumer<TEvent, TConsumer, TDbContext>`) | One quorum queue per module, inbox check and consumer in one transaction, retries, then dead letter |

Modules only see `IUnitOfWork.Publish` and `IIntegrationEventConsumer<T>` from `Gebeta.Application`,
so the transport can be changed without touching modules. Wolverine was considered first and
replaced; see [ADR 0007](adr/0007-rabbitmq-and-docker.md).

## Topology

```
                      exchange: gebeta.events  (topic, durable)
  publishers ──►      routing key: <module>.<event>.v<version>
                         │      e.g. payroll.salary-slip-submitted.v1
         ┌───────────────┼──────────────────────┬──────────────────────┐
         ▼               ▼                      ▼                      ▼
  queue gebeta.accounting  queue gebeta.payroll  queue gebeta.hr  queue gebeta.cash   (quorum queues)
   binds: *.journal-requested.v1  hr.attendance-finalized.v1  ...      ...
          *.journal-reversal-requested.v1  hr.employee-separated.v1
          ...                      accounting.journal-posted.v1
         │
         └─ failed after retries ─► exchange gebeta.dlx ─► queue gebeta.<module>.dlq
```

- **One queue per module** (`gebeta.<module>`). A module binds only the events it subscribes to,
  and only when it is installed. If no module is bound to an event, RabbitMQ drops it; this is
  what lets a module run alone.
- **Quorum queues** for durability and safe failover in a RabbitMQ cluster.
- **Ordering**: events for the same document carry the same `partitionKey` (document id).
  Handlers do not rely on global order; they check versions and timestamps (for example a
  `JournalPosted` for a slip that is already cancelled is ignored and logged).
- **Commands** (one sender, one receiver, an answer expected) are not used between modules.
  Modules use capabilities for questions and events for facts.

## Message envelope

CloudEvents-style headers, JSON body:

| Header | Example |
|---|---|
| `id` | `0193f1c2-...` (UUID v7, unique per message) |
| `type` | `payroll.salary-slip-submitted.v1` |
| `source` | `payroll` |
| `time` | `2026-11-25T09:14:03Z` |
| `company` | company id |
| `partitionKey` | document id |
| `correlationId` | id of the user request that started the flow |
| `causationId` | id of the message that caused this one |
| `tenant` | tenant id (only for SaaS set-ups) |

Money values in the body are strings ([ADR 0003](adr/0003-exact-decimal-money.md)).

## Reliability rules

1. **Outbox**: an event is stored in the module's `outbox` table in the same transaction as the
   change, then sent. A crash after commit never loses it; a rollback never sends it.
2. **Inbox**: each module stores handled message ids; a repeated message is acknowledged and
   skipped. Every handler is idempotent anyway.
3. **Retry**: up to 3 attempts with a short back-off (`RabbitMq:MaxAttempts`), then the dead-letter
   queue. Delayed retries (10 s, 1 min, 10 min, 1 h) are planned.
4. **Dead letters** show on an admin page per module, with the error, and can be replayed after a
   fix.
5. **Versioning**: adding a field keeps the version; removing or renaming one publishes a new
   `.v2` event alongside `.v1` until every subscriber has moved.
6. **Poison messages** (cannot be read at all) go straight to the dead-letter queue.

## Event catalogue (first version)

| Event | Publisher | Typical subscribers |
|---|---|---|
| `core.company-created.v1`, `core.employee-changed.v1`, `core.item-changed.v1` | Core | all |
| `hr.employee-hired.v1`, `hr.employee-changed.v1`, `hr.employee-separated.v1` | HR | Payroll, Cash |
| `hr.attendance-finalized.v1`, `hr.leave-approved.v1` | HR | Payroll |
| `payroll.salary-slip-submitted.v1`, `payroll.payroll-posted.v1`, `payroll.back-payment-submitted.v1` | Payroll | HR (self-service), reports |
| `*.journal-requested.v1`, `*.journal-reversal-requested.v1` | Payroll, Cash, Stock, Manufacturing, HR | Accounting or ERPNext connector |
| `accounting.journal-posted.v1`, `accounting.journal-rejected.v1`, `accounting.period-closed.v1` | Accounting | the module that requested the journal |
| `cash.cash-movement-posted.v1`, `cash.petty-cash-funded.v1` | Cash | Accounting (via journal), reports |
| `stock.stock-moved.v1`, `stock.reposting-completed.v1` | Stock | Manufacturing, reports |
| `manufacturing.work-order-completed.v1`, `manufacturing.job-time-logged.v1` | Manufacturing | Payroll (piece rate), Stock |

Each module doc lists its own events; the `*.Contracts` packages are the source of truth.

## Monitoring

- RabbitMQ management UI (port 15672) and Prometheus metrics (port 15692) → Grafana.
- Alerts: queue depth growing for more than 10 minutes, any message in a dead-letter queue,
  consumer count of a module queue = 0 while the module is installed.

# ADR 0007: RabbitMQ for all module events, Docker for every environment

- Status: Accepted
- Date: 2026-11-14

## Context

Modules are independent and talk through integration events ([ADR 0006](0006-independent-pluggable-modules.md)).
Earlier drafts used an in-process transport when modules share one host and a broker only when
they are split. Two transports means two behaviours to test, and moving a module to its own
service would change how its events are delivered.

## Decision

1. **RabbitMQ** carries every integration event, in every set-up, including a single container
   with all modules. Topic exchange `gebeta.events`, one quorum queue per module, dead-letter
   exchange `gebeta.dlx` ([messaging.md](../messaging.md)).
2. A small Gebeta messaging layer on the official **RabbitMQ.Client** provides the outbox
   dispatcher and the consumer with inbox (`Gebeta.Persistence`, `Gebeta.Messaging`), behind the
   `IUnitOfWork.Publish` and `IIntegrationEventConsumer<T>` interfaces.

   *Amended 2026-11-15:* the first draft chose Wolverine. It was replaced while building, because each
   module keeps its outbox and inbox in its own schema and transaction, which is simpler to control
   directly (about 400 lines, no code generation, one dependency), and the behaviour is covered by
   end-to-end tests against real RabbitMQ and PostgreSQL.
3. **Docker** for every environment. One server image (`abay/gebeta-server`) contains all
   modules; each container enables its modules by configuration. Docker Compose for development
   and single-server customers, Kubernetes for large ones ([deployment.md](../deployment.md)).

## Alternatives considered

- **In-process events with an optional broker**: fewer containers, but two code paths and
  surprises when a module is split out.
- **Kafka**: strong for very high event volumes and replay, but heavier to run on customer
  premises; ERP event volumes do not need it.
- **MassTransit**: mature, but new major versions need a commercial licence.
- **Wolverine**: capable and MIT-licensed, but brings its own hosting model and code generation;
  more than Gebeta needs for per-module outboxes.

## Consequences

- One more container to run and monitor, even for small customers (RabbitMQ uses little memory).
- Same delivery behaviour in development, test and production; splitting a module out is only a
  configuration change.
- Operations must watch queue depth and dead-letter queues (alerts in [messaging.md](../messaging.md#monitoring)).

# ADR 0008: Clean architecture inside every module

- Status: Accepted
- Date: 2026-11-14

## Decision

Every module is split into five projects, and dependencies point inwards only:

```
Gebeta.<Module>.Presentation ──► Gebeta.<Module>.Application ──► Gebeta.<Module>.Domain ──► Gebeta.SharedKernel
            ▲                              │   ▲
            │                              ▼   │
Gebeta.<Module>.Infrastructure ────────────────┘        Gebeta.<Module>.Contracts (no dependencies)
   (persistence + composition root: the IErpModule class)
```

| Layer | Contains | May depend on |
|---|---|---|
| **Domain** | Entities, value objects, business rules, repository interfaces | SharedKernel only |
| **Application** | Commands, queries, handlers, the module's unit-of-work interface | Domain, Contracts, Gebeta.Application |
| **Presentation** | HTTP endpoints, request/response mapping | Application, Modularity |
| **Infrastructure** | EF Core, repositories, RabbitMQ handlers, external services; the `IErpModule` class that wires the module | Application, Presentation |
| **Contracts** | DTOs, integration events and capability interfaces for other modules | nothing |

Building blocks shared by all modules: `Gebeta.SharedKernel` (domain primitives, Ethiopian
calendar), `Gebeta.Application` (command/query interfaces), `Gebeta.Modularity` (plugin system).

## Rules enforced by tests

`tests/Gebeta.ArchitectureTests` checks the compiled assemblies on every build:
- SharedKernel and Contracts depend on nothing in Gebeta;
- Domain depends only on SharedKernel;
- Domain and Application reference no ASP.NET Core, EF Core, Npgsql, RabbitMQ or Wolverine;
- Application does not depend on Infrastructure or Presentation;
- Presentation does not depend on Infrastructure;
- a module uses another module only through its `.Contracts` assembly.

## Consequences

- Business rules (tax, payroll, ledgers) can be tested without a database or web server.
- Infrastructure can change (in-memory → EF Core, RabbitMQ library) without touching rules.
- More projects per module; accepted for the clarity and the enforced boundaries.

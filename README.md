# Gebeta ERP

**Gebeta** (ገበታ, "the table") is a modular ERP and HR system by **Abay Systems**, built with **.NET 10 (C#)** on the backend and **React + TypeScript** on the frontend.

It replaces, step by step, the current ERPNext/HRMS (Frappe) setup and its custom apps
(`customized_forcommon`, `cash_management`, `brand_theme`). Both systems run side by side
until each module is proven, see [Roadmap](docs/roadmap.md).

**Every module runs on its own** (on top of a small Core) and **adds features when plugged in**
next to other modules. For example, Payroll alone takes payment days by hand and exports its
journal; with HR it reads attendance and leave, and with Accounting its journal is posted to the
GL automatically. See [Plugin architecture](docs/plugin-architecture.md).

> Status: **design phase**. This repository holds the design documents only. No code yet.

## Documents

| Document | What it covers |
|---|---|
| [Architecture](docs/architecture.md) | Module boundaries, document lifecycle, request flow |
| [Plugin architecture](docs/plugin-architecture.md) | Every module runs alone; plugging modules together adds features |
| [Messaging](docs/messaging.md) | RabbitMQ topology, message envelope, outbox/inbox, retries, event catalogue |
| [Deployment](docs/deployment.md) | Docker images and containers, set-ups, environments, backups, upgrades |
| [Tech stack](docs/tech-stack.md) | Chosen libraries and tools, and why |
| [Conventions](docs/conventions.md) | Code, naming, money and dates, API and database rules |
| [Platform](docs/platform.md) | Users, roles, permissions, companies, naming series, workflow, audit |
| [Frontend](docs/frontend.md) | React app structure, forms, lists, theming |
| [ERPNext coexistence](docs/erpnext-integration.md) | How the new system and ERPNext run together during the move |
| [Data migration](docs/data-migration.md) | Moving data from ERPNext and proving it matches |
| [Roadmap](docs/roadmap.md) | Phases, order of modules, exit criteria |
| [Custom features inventory](docs/custom-features-inventory.md) | Every custom doctype in the current system and where it goes |

### Modules

| Module | Document |
|---|---|
| Accounting | [docs/modules/accounting.md](docs/modules/accounting.md) |
| Stock | [docs/modules/stock.md](docs/modules/stock.md) |
| Manufacturing | [docs/modules/manufacturing.md](docs/modules/manufacturing.md) |
| HR | [docs/modules/hr.md](docs/modules/hr.md) |
| Payroll | [docs/modules/payroll.md](docs/modules/payroll.md) |
| Cash Management | [docs/modules/cash-management.md](docs/modules/cash-management.md) |

### Decisions (ADR)

| # | Decision |
|---|---|
| [0001](docs/adr/0001-modular-monolith.md) | Modular monolith, not microservices |
| [0002](docs/adr/0002-dotnet-postgresql.md) | .NET 10 and PostgreSQL |
| [0003](docs/adr/0003-exact-decimal-money.md) | Exact decimals for all money and quantities |
| [0004](docs/adr/0004-append-only-ledgers.md) | Append-only ledgers, reversal on cancel |
| [0005](docs/adr/0005-incremental-replacement.md) | Replace ERPNext module by module |
| [0006](docs/adr/0006-independent-pluggable-modules.md) | Independent, pluggable modules |
| [0007](docs/adr/0007-rabbitmq-and-docker.md) | RabbitMQ for all module events, Docker for every environment |

## Planned repository layout

```
gebeta/
├─ src/
│  ├─ Gebeta.Api/                # ASP.NET Core host (one deployable)
│  ├─ Gebeta.SharedKernel/       # Money, Quantity, DocumentBase, Result, events
│  ├─ Modules/
│  │  ├─ Core/                   # Platform: the only required module
│  │  ├─ Accounting/
│  │  ├─ Stock/
│  │  ├─ Manufacturing/
│  │  ├─ Hr/
│  │  ├─ Payroll/
│  │  └─ CashManagement/
│  └─ Gebeta.Migration/          # ERPNext → Gebeta data migration tool
├─ web/                          # React + TypeScript (Vite)
├─ tests/
├─ deploy/                       # docker-compose.yml, .env.example, Dockerfiles
└─ docs/
```

Each module folder has `Domain`, `Application`, `Infrastructure` and `Api` projects (see
[Architecture](docs/architecture.md)).

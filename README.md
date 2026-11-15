# Gebeta ERP

**Gebeta** (ገበታ, "the table") is a modular ERP and HR system by **Abay Systems**, built with **.NET 10 (C#)** on the backend and **React + TypeScript** on the frontend.

It replaces, step by step, the current ERPNext/HRMS (Frappe) setup and its custom apps
(`customized_forcommon`, `cash_management`, `brand_theme`). Both systems run side by side
until each module is proven, see [Roadmap](docs/roadmap.md).

**Every module runs on its own** (on top of a small Core) and **adds features when plugged in**
next to other modules. For example, Payroll alone takes payment days by hand and exports its
journal; with HR it reads attendance and leave, and with Accounting its journal is posted to the
GL automatically. See [Plugin architecture](docs/plugin-architecture.md).

> Status: **building, phase 0**. Building blocks, the plugin system and the first Core feature
> (companies) are in place; see [Roadmap](docs/roadmap.md).

**Built for Ethiopian businesses and law**: Ethiopian calendar and Hamle–Sene fiscal year, birr,
TIN, Ethiopian tax, pension and labour rules as dated configuration, English and Amharic. See
[Ethiopian business and law](docs/ethiopia.md).

## Documents

| Document | What it covers |
|---|---|
| [Architecture](docs/architecture.md) | Module boundaries, document lifecycle, request flow |
| [Ethiopian business and law](docs/ethiopia.md) | Calendar, fiscal year, tax, pension, labour law, language, data protection |
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
| [0008](docs/adr/0008-clean-architecture.md) | Clean architecture inside every module |
| [0009](docs/adr/0009-ethiopia-first.md) | Ethiopia first: Ethiopian rules are the defaults |

## Planned repository layout

```
gebeta/
├─ src/
│  ├─ Gebeta.Api/                # ASP.NET Core host (one deployable)
│  ├─ BuildingBlocks/
│  │  ├─ Gebeta.SharedKernel/    # Money, Quantity, DocumentBase, Result, Ethiopian calendar, TIN
│  │  ├─ Gebeta.Application/     # command / query / handler interfaces
│  │  └─ Gebeta.Modularity/      # plugin system: modules, capabilities, fallbacks
│  ├─ Modules/
│  │  ├─ Core/                   # Platform: the only required module (Domain, Application,
│  │  │                          #   Presentation, Infrastructure, Contracts)
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

Each module has `Domain`, `Application`, `Presentation`, `Infrastructure` and `Contracts`
projects ([ADR 0008](docs/adr/0008-clean-architecture.md)).

## Build and test

Requires the .NET 10 SDK (`global.json` pins the version).

```bash
dotnet build Gebeta.slnx
dotnet test --solution Gebeta.slnx --max-parallel-test-modules 2   # container tests start PostgreSQL/RabbitMQ in Docker
dotnet run --project src/Gebeta.Api      # http://localhost:5080/api/modules
```

The API needs PostgreSQL. Start the infrastructure (`docker compose -f deploy/docker-compose.yml up -d`)
and store the connection string once in .NET user-secrets (kept outside the repository):

```bash
dotnet user-secrets set "ConnectionStrings:Main" "Host=localhost;Port=5434;Database=gebeta;Username=gebeta;Password=<POSTGRES_PASSWORD from deploy/.env>" --project src/Gebeta.Api
```

Every endpoint except `/health/*` and `/openapi` needs a Keycloak access token for realm `gebeta`
with audience `gebeta-api`. Users are created in Gebeta on their first sign-in; usernames listed in
`Security:BootstrapAdmins` (user-secrets) get the System Manager role. For local testing, the
development-only client `gebeta-dev-cli` allows a password sign-in with the user in `deploy/.env`:

```bash
curl -s -X POST http://localhost:8180/realms/gebeta/protocol/openid-connect/token -d grant_type=password -d client_id=gebeta-dev-cli -d username=gebeta.admin -d "password=<GEBETA_DEV_PASSWORD from deploy/.env>"
```

Permissions look like `core.companies.create`; roles hold permissions or wildcards (`core.*`, `*`).
System roles (System Manager, Core Administrator, Core Viewer) are created by the migration.
Users see only the companies assigned to them (`PUT /api/core/users/{id}/companies/{companyId}`),
unless a role grants `core.companies.access-all`; other modules filter their records the same way
through `ICompanyAccess`.

In Development the API applies every module's migrations at start-up.

Run the API in Docker too (port 5090):

```bash
dotnet publish src/Gebeta.Api -c Release -o artifacts/publish
docker build -f deploy/Dockerfile.local -t abay/gebeta-server:dev .
docker compose -f deploy/docker-compose.yml --profile app up -d
``` Elsewhere run
`dotnet Gebeta.Api.dll --migrate` (the migrate container) before starting the API.
New migration for a module: `dotnet ef migrations add <Name> --project src/Modules/Core/Gebeta.Core.Infrastructure --startup-project src/Gebeta.Api --context CoreDbContext --output-dir Persistence/Migrations`.

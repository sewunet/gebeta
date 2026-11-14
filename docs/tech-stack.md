# Tech stack

## Backend

| Need | Choice | Notes |
|---|---|---|
| Runtime | **.NET 10 (LTS)**, C# | Supported until late 2028. Upgrade to the next LTS when it ships. |
| Web API | ASP.NET Core (Minimal APIs grouped per module) | OpenAPI document generated at build |
| ORM | **EF Core** (Npgsql provider) | Writes and normal reads |
| Raw SQL / reports | **Dapper** | Trial balance, stock balance, payroll registers |
| Database | **PostgreSQL 18** | `numeric` for money, schemas per module, row-level locking |
| Migrations | EF Core migrations, one history table per module | |
| Validation | FluentValidation | |
| Command handling | Plain handler interfaces in SharedKernel | Avoids a licensed mediator library; easy to read |
| Event bus | **RabbitMQ 4** (quorum queues) | All events between modules, see [messaging.md](messaging.md) |
| Messaging library | **Wolverine** (MIT) | RabbitMQ transport, PostgreSQL outbox/inbox, retries, dead letters |
| Background jobs | **Hangfire** (PostgreSQL storage) | Retries, dashboard, recurring jobs |
| Cache / locks | Redis (StackExchange.Redis) | Distributed locks for reposting and payroll runs |
| Auth | **Keycloak 26** (OIDC) with the Gebeta login theme | SSO, MFA, LDAP/AD if needed. OpenIddict is the self-hosted alternative. |
| Realtime | SignalR | |
| Formula engine (payroll) | NCalc, wrapped with a whitelist of variables and functions | No `eval`, no reflection |
| PDF | QuestPDF | Payslips, invoices, print formats |
| Excel | ClosedXML | Import and export |
| Logging / tracing | Serilog, OpenTelemetry | |
| Tests | xUnit, FluentAssertions, **Testcontainers** (real PostgreSQL), NetArchTest | |

## Frontend

| Need | Choice |
|---|---|
| Language | **TypeScript** (strict) |
| Build | Vite |
| UI | React 19 |
| Server state | TanStack Query |
| Routing | TanStack Router (typed routes) |
| Forms | React Hook Form + Zod |
| Tables | TanStack Table (virtualised lists) |
| Components | shadcn/ui on Radix + Tailwind CSS |
| API client | Generated from the backend OpenAPI document (orval) |
| Charts | Apache ECharts |
| i18n | i18next (English, Amharic) |
| Tests | Vitest, Testing Library, Playwright for end-to-end |

## Infrastructure

| Need | Choice |
|---|---|
| Containers | **Docker** for every environment; `deploy/docker-compose.yml` (see [deployment.md](deployment.md)) |
| Local dev | Docker Compose: PostgreSQL, RabbitMQ, Redis, Keycloak, Seq; app runs from the IDE |
| CI | GitHub Actions (build, test, architecture tests, migration check) |
| Deploy | Images `abay/gebeta-server`, `abay/gebeta-web`, `abay/gebeta-migrate`; Docker Compose for single servers, Kubernetes for large set-ups |
| Backups | PostgreSQL WAL archiving + nightly base backups (pgBackRest) |

## What we deliberately do not use

- **Microservices**, see [ADR 0001](adr/0001-modular-monolith.md).
- **`double`/`float` for money or quantity**, see [ADR 0003](adr/0003-exact-decimal-money.md).
- **Generic "DocType engine" built from metadata at runtime** (like Frappe). Modules are written as
  typed C# code. Custom fields for customers are supported through a JSONB `custom` column per
  document (see [Platform](platform.md#custom-fields)), not by changing tables at runtime.

# Deployment with Docker

Everything runs in **Docker** containers, from a developer laptop to production
([ADR 0007](adr/0007-rabbitmq-and-docker.md)).

## Images

| Image | Contents |
|---|---|
| `abay/gebeta-server` | The .NET host with **all** module packages. Which modules run is set per container by configuration. |
| `abay/gebeta-web` | The built React app served by Nginx; also proxies `/api` and `/hubs` to the server |

The `migrate` container uses the server image with `--migrate`: it updates every enabled module's
schema and exits.

| File | Use |
|---|---|
| `deploy/Dockerfile` | CI build: SDK stage publishes, ASP.NET runtime stage runs (non-root `app` user, health check on `/health/live`) |
| `deploy/Dockerfile.local` | Local build from `artifacts/publish` (no SDK image needed) |

One server image keeps releases simple: every container runs the same tested build, and moving a
module to its own container is only a configuration change.

- Multi-stage Dockerfiles: `mcr.microsoft.com/dotnet/sdk:10.0` to build,
  `mcr.microsoft.com/dotnet/aspnet:10.0` (chiseled, non-root) to run.
- Tagged with the version and the git commit (`1.4.0`, `1.4.0-74e5c81`). Never deploy `latest`.
- Health endpoints: `/health/live` and `/health/ready` (database, RabbitMQ, Redis checks).

## Containers

| Container | Image | Notes |
|---|---|---|
| `web` | `abay/gebeta-web` | Port 80/443 (behind the customer's reverse proxy or with its own TLS) |
| `server` | `abay/gebeta-server` | API + event consumers for the enabled modules; scale with replicas |
| `jobs` (planned) | `abay/gebeta-server` with `Gebeta__Role=Jobs` | Hangfire background jobs (payroll runs, reposting, revaluation) |
| `migrate` | `abay/gebeta-server` with `--migrate` | Runs once before `server` starts |
| `postgres` | `postgres:18` | One database, one schema per module |
| `rabbitmq` | `rabbitmq:4-management` | Event bus, quorum queues |
| `redis` | `redis:7` | Cache and distributed locks |
| `keycloak` | `quay.io/keycloak/keycloak:26.5` | Login (OIDC), with the Gebeta theme |
| `seq` | `datalust/seq` | Logs (or Grafana Loki) |
| file storage | any S3-compatible service | Attachments; local volume in development |

## Choosing modules

Per container, by environment variables:

```yaml
environment:
  Modules__Enabled__0: core
  Modules__Enabled__1: hr
  Modules__Enabled__2: payroll
```

### Set-up A: all chosen modules in one container (default)

```
web ─► server [core, hr, payroll, cash, accounting]  ─┐
       jobs   [same modules]                          ├─► postgres, rabbitmq, redis, keycloak
```

### Set-up B: one module alone (e.g. a customer who buys only Payroll)

```
web ─► server [core, payroll] ─► postgres, rabbitmq, redis, keycloak
```

### Set-up C: modules in separate containers

```
web ─► core-api        [core]
   ├─► hr-api          [core-client, hr]
   ├─► payroll-api     [core-client, payroll]        all ─► rabbitmq (events)
   └─► accounting-api  [core-client, accounting]     each ─► own schema or own database
```

`web` routes `/api/<module>/...` to the matching container. Core runs once; the other
containers read Core data through Core's API and cache it from Core events (`core-client`).
Because modules already talk only through RabbitMQ, nothing else changes between A, B and C.

## Environments

| Environment | How |
|---|---|
| Developer | `deploy/docker-compose.yml` starts the infrastructure; the server and web app run from the IDE (`dotnet watch`, `vite`) |
| Test / staging | Same compose file plus the `app` profile with built images, on one server |
| Production, small customer | Docker Compose on one server (or two: app + database), daily off-site backups |
| Production, large customer / SaaS | Kubernetes (Helm chart) with managed or clustered PostgreSQL and a 3-node RabbitMQ cluster |

## Login branding (Keycloak)

Users never see "Keycloak": sign-in, password reset and the admin console's sign-in page use the
**Gebeta** login theme.

| File | Purpose |
|---|---|
| `deploy/keycloak/themes/gebeta/login/theme.properties` | Extends Keycloak's `keycloak.v2` login theme; adds `css/gebeta.css` |
| `.../resources/css/gebeta.css` | Colours (Abay blue `#0F3B5F`, gold `#D9A441`), card, buttons, brand header, footer |
| `.../resources/img/gebeta-mark.svg`, `favicon.ico` | Gebeta mark and browser icon |
| `.../messages/messages_en.properties` | Wording ("Sign in to Gebeta") |
| `deploy/keycloak/import/gebeta-realm.json` | Realm `gebeta`: display name, `loginTheme: gebeta`, password reset, brute-force protection, and the `gebeta-web` client (public, PKCE) |

- The theme folder is mounted read-only into the container; the realm file is imported on first
  start (`--import-realm`, existing realms are not overwritten).
- The `master` realm (admin console) is set to the same theme once with
  `kcadm.sh update realms/master -s loginTheme=gebeta`.
- In development (`start-dev`) theme changes show after a browser refresh. In production the theme
  is built into a custom image (`abay/gebeta-keycloak`) so it is cached and versioned.
- Customer-specific branding (logo and colours per customer) uses one realm per customer with its
  own theme that extends `gebeta`.

## Configuration and secrets

- Configuration by environment variables (`ConnectionStrings__Main`, `RabbitMq__Host`, ...).
- Secrets come from a `.env` file that is not committed (dev, small installs), or Docker secrets /
  Kubernetes secrets in production. `deploy/.env.example` lists every variable without values.

## Data and backups

| Data | Volume | Backup |
|---|---|---|
| PostgreSQL | `pgdata` | pgBackRest: WAL archiving + nightly full backup, copied off the server; monthly restore test |
| RabbitMQ | `rabbitmq-data` | Definitions exported nightly; messages are not backed up (the outbox can resend) |
| Files | S3 bucket or `files` volume | Bucket versioning or nightly sync |
| Keycloak | its own database in PostgreSQL | Included in the PostgreSQL backup |

## Upgrades

1. Pull the new image tags.
2. Run `migrate` (migrations are backward compatible for one release, so the old server keeps
   working while it runs).
3. Restart `server` and `jobs` one replica at a time.
4. Roll back by going back to the previous tags; the database stays compatible for one release.

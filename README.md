# Wbskt

Wbskt is a real-time platform for connecting devices, scripts and apps. A client (an ESP32, a
Raspberry Pi, a script, a web page) registers under a policy, holds a WebSocket open to Wbskt, and
sends and receives JSON messages. Workflows react to those messages and to clients coming and
going, and act on them: notify you, message other clients, call an integration.

This repository is the backend. The web console lives in
[wbskt/Wbskt.Console](https://github.com/wbskt/Wbskt.Console).

## How it fits together

| Host | What it does |
|---|---|
| **Auth** (`Hosts/Wbskt.Auth.Host`) | Accounts, sign-in, email verification, password reset, tokens, tenants, roles and permissions. |
| **Management** (`Hosts/Wbskt.Management.Host`) | Workspaces, policies, clients and workflows: the API the console and the client SDK call. |
| **Socket** (`Hosts/Wbskt.Socket.Host`) | Holds the clients' WebSocket connections and routes their messages. |
| **Workflow Engine** (`Hosts/Wbskt.Workflow.Engine.Host`) | Runs workflows. Not publicly routed; only the management host calls it. |

The hosts share SQL Server (two databases, deployed from DACPACs), RabbitMQ for events between
hosts, and Redis for state that has to be seen by more than one host, such as revoked tokens. In
production they run behind Traefik with an OpenTelemetry stack (Prometheus, Loki, Tempo, Grafana).

[Docs/API.Endpoints.md](Docs/API.Endpoints.md) lists every endpoint by host, and
[Docs/AccessControl.md](Docs/AccessControl.md) explains the permission model.

## Repository layout

```
Hosts/          the four ASP.NET Core hosts
Clients/        Wbskt.Client.Sdk (the .NET client SDK), Wbskt.Simulator, a test dashboard page
Databases/      Wbskt.Database and Wbskt.Database.Auth (SQL projects) and migrations
Wbskt.Workflow* the workflow model, builder and abstractions the engine runs
Wbskt.Infrastructure, Wbskt.Primitives, Models, Events
                shared code: data access, messaging, contracts
Config/         shared host configuration
Tests/          unit, integration, end-to-end and load tests
Tools/          Wbskt.Workflow.Exporter
deploy/         Docker images, compose stack, VM scripts and observability config
Docs/           design notes, plans and references
```

## Running it locally

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download), Docker, and PowerShell 7 for
the database script.

1. Start SQL Server, RabbitMQ and Redis:

   ```bash
   docker run -d --name wbskt-sql -e "ACCEPT_EULA=1" -e "MSSQL_SA_PASSWORD=Welcome1234" -p 1433:1433 mcr.microsoft.com/azure-sql-edge
   docker run -d --name wbskt-rabbit -p 5672:5672 -p 15672:15672 rabbitmq:3-management
   docker run -d --name wbskt-redis -p 6379:6379 redis:7-alpine
   ```

   `Welcome1234` is the local development default the scripts expect. Never use it anywhere else.

2. Deploy both databases:

   ```bash
   dotnet tool install -g microsoft.sqlpackage
   pwsh ./Deploy-Databases.ps1          # add -Fresh to drop and recreate
   ```

3. Start the hosts as Development, either one by one:

   ```bash
   dotnet run --project Hosts/Wbskt.Auth.Host             # https://localhost:7000
   dotnet run --project Hosts/Wbskt.Management.Host       # https://localhost:7010
   dotnet run --project Hosts/Wbskt.Socket.Host           # https://localhost:7020
   dotnet run --project Hosts/Wbskt.Workflow.Engine.Host  # https://localhost:7030
   ```

   or all four in the background, waiting until they are healthy:

   ```bash
   Tests/Wbskt.E2E.FeatureTests/start-hosts.sh
   ```

You can also run the whole stack in containers with `deploy/compose/docker-compose.yml`; see
[deploy/README.md](deploy/README.md).

## Tests

```bash
dotnet test Tests/Wbskt.Workflow.Engine.Host.Tests              # unit tests, no infrastructure
dotnet test Tests/Wbskt.Workflow.Engine.Host.IntegrationTests
dotnet test Tests/Wbskt.E2E.FeatureTests                        # needs the hosts running
```

The end-to-end suite drives the public APIs and the real client SDK, and skips itself when the
hosts are not reachable. [Its README](Tests/Wbskt.E2E.FeatureTests/README.md) covers the details,
including the opt-in rate-limit scenarios. `Tests/Wbskt.LoadTester` is a console app, not a test
project.

## Deployment

Pushes to `master` build the images and push them to GHCR. A manually dispatched workflow deploys
a chosen tag to the VM over SSH, where Docker Compose runs the stack.
[deploy/README.md](deploy/README.md) covers setup, secrets, migrations, backups and observability.

## License

Wbskt is free for noncommercial use under the
[PolyForm Noncommercial License 1.0.0](LICENSE.txt). Personal projects, hobby builds, study,
research, and use by charities, schools and public institutions are all allowed.

Commercial use is not covered by that license. If you want to use Wbskt commercially, contact
the maintainers for a commercial license.

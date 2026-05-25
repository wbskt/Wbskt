# WBSKT — Copilot Instructions

WBSKT is a .NET 10 microservice-oriented platform for distributed client registration, real-time messaging, and workflow automation. Solution file is `Wbskt.slnx` (new SLNX format — older `dotnet` versions may not understand it; `global.json` pins SDK `10.0.0` with `rollForward: latestFeature`).

## Build / Run / Test

- **Build solution**: `dotnet build Wbskt.slnx`
- **Run all hosts together**: use the Rider compound config `.run/RunAll.run.xml` (launches Auth, Management, Socket, Workflow.Engine). Dev ports: Auth `7000`, Management `7010`, Socket `7020`, Workflow Engine `7030`.
- **Run a single host**: `dotnet run --project Hosts/Wbskt.Auth.Host` (substitute host project).
- **Tests** (xUnit + Moq): `dotnet test Tests/Wbskt.Workflow.Engine.Host.Tests` — single test: `dotnet test Tests/Wbskt.Workflow.Engine.Host.Tests --filter "FullyQualifiedName~MyTestClass.MyTestMethod"`.
- **Databases** (SSDT `.sqlproj` DACPAC deploy): `pwsh ./Deploy-Databases.ps1` (add `-Fresh` to drop+recreate). Requires `dotnet tool install -g microsoft.sqlpackage`. See `Scripts.bat` for the SQL Edge + RabbitMQ docker one-liners used in dev.

## Repository layout (top-level folders map to assembly roots)

- `Hosts/` — ASP.NET Core entry points (`Wbskt.Auth.Host`, `Wbskt.Management.Host`, `Wbskt.Socket.Host`, `Wbskt.Workflow.Engine.Host`).
- `Models/` — DTOs / request-response records per host (`Wbskt.Auth.Models`, `Wbskt.Management.Models`, `Wbskt.Workflow.Models`, shared `Wbskt.Models`).
- `Databases/` — SSDT projects (`Wbskt.Database`, `Wbskt.Database.Auth`) — source of truth for schema and stored procedures.
- `Events/` — `Wbskt.EventBus` (abstraction), `Wbskt.EventBus.RabbitMQ` (MassTransit impl), `Wbskt.Events` (event contracts).
- `Wbskt.Infrastructure/` — `BaseSqlProvider`, middlewares (incl. global exception handler), security, configuration binding.
- `Wbskt.Workflow*` — workflow engine core, abstractions, entities, mappers, providers.
- `Clients/` — SDK (`Wbskt.Client.Sdk`) consumed by edge devices, plus `Wbskt.Simulator`.
- `Config/` — shared JSON config fragments (`connectionstrings.json`, `jwt.json`, `rabbitmq.json`, `serilog.json`) loaded by hosts.
- `Docs/` — design docs (`Coding.Conventions.md`, `GEMINI.md`, workflow schema/engine notes). Read `Docs/Coding.Conventions.md` before non-trivial changes.

## Central package + SDK setup

- Versions are centrally managed in `Directory.Packages.props` (`ManagePackageVersionsCentrally=true`). Add new dependencies there, not in individual `.csproj` files.
- **Do not upgrade `MassTransit` past v8.x** — v9+ changes license away from MIT (commented in `Directory.Packages.props`).
- `nuget.config` disables several private feeds — leave those `disabledPackageSources` entries in place.

## Architecture big picture

Four cooperating hosts communicate via RabbitMQ (MassTransit) for async events and REST for sync calls; the Socket host owns persistent WebSocket connections to edge clients. The Workflow Engine consumes events and runs user-defined node/edge graphs (see `Docs/Wbskt.Workflow.Schema.md` and `Docs/Workflow.Engine.md`). Each host has its own SQL database (`Wbskt.Database.Auth` is isolated from the core `Wbskt.Database`) and its own Models assembly.

## Project-specific conventions (from `Docs/Coding.Conventions.md`)

These are non-obvious and enforced — follow them in every change:

- **ID boundary**: Public APIs expose `RefId` (GUID) only; internal `Id` (int) is for DB joins. Controllers translate via the **Reference Mapper Pattern** — keyed scoped services like `AddKeyedScoped<IReferenceMapper, ReferenceMapper<IProjectProvider>>("Project")`, injected with `[FromKeyedServices("Project")]`. Failed RefId lookup throws `SecurityException` (→ 403), never `NotFoundException` (prevents enumeration).
- **Method naming**: `FindBy...` returns an internal `int` ID only; `GetBy...` returns a full entity/DTO. Async methods always end in `Async`.
- **Returns**: Public methods never return `null` — throw a descriptive exception instead. Collection returns use `IReadOnlyCollection<T>`. Controllers return `async Task<T>` with a named record/class (no `object`/anonymous types) and return data directly; failures are translated to HTTP by the global exception handler — controllers stay on the happy path.
- **Providers**: Inherit from `Wbskt.Infrastructure/BaseSqlProvider.cs` for SQL boilerplate. **A provider must never depend on another provider** — orchestrate across providers in the Service layer. Extract `SqlDataReader → entity` mapping into private helpers.
- **Native first**: Prefer BCL / ASP.NET Core primitives (`PasswordHasher<T>`, `System.Text.Json`, built-in DI) over 3rd-party equivalents.
- **Disposables**: Use `await using var` for `IAsyncDisposable` (`SqlConnection`, `SqlDataReader`, …).
- **Syntax**: Always use braces for single-line `if`/`for`/`while`. Put a blank line before any comment.

## SQL / Database conventions

- Schema lives in the `.sqlproj` projects under `Databases/` — edit DDL there, then redeploy via `Deploy-Databases.ps1`.
- Stored procedure naming: `Entity_FindBy_Criteria` (returns Id), `Entity_GetBy_Criteria` (returns row), `Entity_Create`, `Entity_Grant`, `Entity_Verify`.
- SQL keywords UPPERCASE; always `dbo.` prefix; never `SELECT *` (one column per line for clean diffs); timestamps are `DATETIME2(3)`; every new table gets `CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME()`.

## Events / messaging

Cross-service events are defined in `Events/Wbskt.Events` and published/consumed through `Wbskt.EventBus` (RabbitMQ binding in `Wbskt.EventBus.RabbitMQ`). Most state changes (client payloads, registrations, workflow triggers) flow through the bus rather than direct REST calls — prefer adding/using an event over a sync inter-host HTTP call.

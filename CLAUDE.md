# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

---

## Assignment Context

This is a Software Architectures group assignment (50% of grade) at UA — Master in Informatics Engineering.

**Goal:** Evolve [nopCommerce](https://github.com/nopSolutions/nopCommerce) under one architectural scenario, demonstrate it under pressure, and document every decision.

**Two deliveries:**
- Part 1 — Architecture Checkpoint: 05–06 May 2026 (7 min presentation)
- Part 2 — Final Delivery: 02–03 June 2026 (15 min + live demo)

**Chosen scenario:** C — Omnichannel Commerce Core (VerdeMart Retail). nopCommerce becomes the commerce core for a retailer that also operates ERP, warehouse, POS, and shipping systems. The architectural challenge is keeping the commerce core useful and consistent when those surrounding systems degrade, lag, or contradict it.

The baseline nopCommerce code lives under `nopcommerce/`. Custom architecture work (services, Docker configs, docs, ADRs) should be added alongside or on top of that baseline.

---

## Build & Run

All commands run from `nopcommerce/src/` unless stated otherwise.

```bash
# Build entire solution
dotnet build NopCommerce.sln

# Run the web app (requires a configured database — see App_Data/appsettings.json after first run)
dotnet run --project Presentation/Nop.Web/Nop.Web.csproj

# Run with Docker (MSSQL)
cd nopcommerce && docker-compose up

# Run with PostgreSQL
cd nopcommerce && docker-compose -f postgresql-docker-compose.yml up

# Run with MySQL
cd nopcommerce && docker-compose -f mysql-docker-compose.yml up
```

**SDK requirement:** .NET 10 SDK (`10.0.100`, see `nopcommerce/global.json`). All projects target `net10.0`.

## Tests

Single test project using NUnit 4, Moq, and AwesomeAssertions. Tests use an in-memory SQLite provider — no external DB needed.

```bash
# Run all tests
dotnet test src/Tests/Nop.Tests/Nop.Tests.csproj

# Run a single test class
dotnet test src/Tests/Nop.Tests/Nop.Tests.csproj --filter "FullyQualifiedName~CustomerServiceTests"

# Run a specific test method
dotnet test src/Tests/Nop.Tests/Nop.Tests.csproj --filter "FullyQualifiedName~CustomerServiceTests.TestMethod"
```

---

## Architecture Overview

nopCommerce is a **modular monolith** with an onion/layered structure. The solution is at `nopcommerce/src/NopCommerce.sln`.

### Layer Dependencies (inner → outer)

```
Nop.Core  ←  Nop.Data  ←  Nop.Services  ←  Nop.Web.Framework  ←  Nop.Web
                                     ↑                    ↑
                                  Plugins             Plugins
```

| Project | Role |
|---|---|
| `Libraries/Nop.Core` | Domain entities (`BaseEntity`), interfaces, caching, events, configuration |
| `Libraries/Nop.Data` | LinqToDb ORM, FluentMigrator migrations, multi-DB providers, `EntityRepository<T>` |
| `Libraries/Nop.Services` | All business logic, organized by domain (Catalog, Orders, Customers, …) |
| `Presentation/Nop.Web.Framework` | ASP.NET Core middleware, routing base, AutoMapper, MVC infrastructure |
| `Presentation/Nop.Web` | MVC storefront + admin area (under `Areas/Admin/`), Razor views, model factories |
| `Plugins/Nop.Plugin.*` | Self-contained features deployed to `Nop.Web/Plugins/[SystemName]/` |

### Key Cross-Cutting Patterns

**Startup registration** — Every layer and plugin implements `INopStartup` (`Nop.Core.Infrastructure`). The `NopEngine` discovers all implementations via reflection (`ITypeFinder`) at boot and calls `ConfigureServices` / `Configure` in `Order` sequence. This is how plugins register their own DI bindings without modifying core.

**DI container** — Supports both Microsoft DI and Autofac (toggled by `CommonConfig.UseAutofac` in `appsettings.json`). Resolved via `EngineContext.Current.Resolve<T>()` or normal constructor injection.

**Repository pattern** — `EntityRepository<TEntity>` wraps LinqToDb and the cache layer. All data access goes through `IRepository<T>`. There is **no EF Core** — do not introduce it.

**Event system** — `IEventPublisher.PublishAsync<TEvent>()` fires domain events. Consumers implement `IConsumer<TEvent>` and are discovered automatically. Used for cache invalidation and cross-cutting reactions (e.g., `EntityUpdatedEvent<Product>`).

**Caching** — Two tiers: `IStaticCacheManager` (Redis or in-memory, long-lived) and `IShortTermCacheManager` (per-request). Cache keys use typed `CacheKey` objects with format strings. Every service defines its own `*CachingDefaults` class with key templates.

**Configuration** — Strongly typed config sections implement `IConfig` and are loaded from `appsettings.json`. Access via `AppSettings.Get<TConfig>()` or constructor-injected directly. Settings stored per-store in the DB use `ISettingService`.

**Schedule tasks** — Background jobs implement `IScheduleTask`, are stored in the `ScheduleTask` DB table, and run by `ITaskScheduler`. Register new tasks via a migration.

### Plugin System

- Each plugin is an independent `.csproj` in `src/Plugins/Nop.Plugin.<Group>.<Name>/`
- Requires a `plugin.json` declaring `SystemName`, `Group`, `FriendlyName`, `SupportedVersions`, `FileName`
- Output path configured to `$(SolutionDir)\Presentation\Nop.Web\Plugins\<SystemName>\`
- The `ClearPluginAssemblies` MSBuild target removes framework DLLs from the plugin output (prevents version conflicts)
- Plugins reference only `Nop.Web.Framework` — never `Nop.Web` directly

### Data Layer Details

- ORM: **LinqToDb** (not EF Core). Entity mappings are Fluent builders in `Nop.Data/Mapping/Builders/`
- Schema migrations: **FluentMigrator**, attributed with `[NopSchemaMigration(...)]`
- Supported databases: MSSQL, PostgreSQL, MySQL (runtime); SQLite (tests only via `SqLiteNopDataProvider`)
- Multi-store data isolation is handled via `IStoreMappingService` and the `IStoreMappingSupported` interface on entities

### Domain Model Shape

Domain entities extend `BaseEntity` (int `Id`). Cross-cutting concerns are marker interfaces:
- `ILocalizedEntity` — translatable fields
- `ISlugSupported` — SEO URL slugs
- `IAclSupported` — access control lists
- `IStoreMappingSupported` — per-store visibility
- `ISoftDeletedEntity` — logical deletion via `Deleted` flag
- `IDiscountSupported<TMapping>` — discount applicability

---

## Behavioral Guidelines

### Think Before Coding

Before implementing: state assumptions explicitly, surface tradeoffs, ask if multiple interpretations exist.

### Simplicity First

Minimum code that solves the problem. No speculative features, no single-use abstractions, no impossible-scenario error handling.

### Surgical Changes

Touch only what the task requires. Match existing style. Don't improve adjacent code. Remove only what your changes orphan.

### Goal-Driven Execution

Transform tasks into verifiable goals before starting. For multi-step tasks, write a brief plan with explicit verify steps.

# VerdeMart — Implementation Plan

Phase-by-phase implementation breakdown for the architectural evolution
of nopCommerce under Scenario C. Each phase maps to the roadmap in
`architecture-report.md` §9.

This plan is the engineering view — concrete work items, files to touch,
tests to write, exit criteria. It is intended to be executable: a future
contributor (or Claude Code) should be able to pick a phase and start
work without re-reading the whole report.

**Conventions used below**

- File paths starting with `src/` are relative to `nopcommerce/`
- New integration code lives under `src/Libraries/Nop.Services/Integration/`
  and `src/Libraries/Nop.Services/Inventory/`
- New entities live under `src/Libraries/Nop.Core/Domain/Integration/`
  and `src/Libraries/Nop.Core/Domain/Inventory/`
- All migrations are `FluentMigrator` migrations attributed with
  `[NopSchemaMigration(...)]` (see existing `Nop.Data/Migrations/`)
- All schedule tasks implement `IScheduleTask` and are inserted via
  migration into the `ScheduleTask` table
- Do **not** introduce EF Core. Data access is LinqToDb + `IRepository<T>`

---

## Phase 0 — Baseline

**Goal:** confirm the unmodified nopCommerce baseline runs locally and
the team has a shared development environment.

**Work items**

- [ ] Verify `dotnet build NopCommerce.sln` succeeds against the
      `nopcommerce/` baseline
- [ ] Stand up the default `docker-compose.yml` (MSSQL) and complete the
      first-run install wizard
- [ ] Snapshot the baseline DB schema (export from `App_Data/`) so we
      can diff later
- [ ] Capture a baseline screenshot of the product page (no stock
      indicator, no staleness)
- [ ] No code changes in this phase

**Exit criteria**

- All team members can run nopCommerce locally and place a test order
- A "Phase 0 baseline" tag exists in git (`git tag phase-0-baseline`)

---

## Phase 1 — Infrastructure Foundation

**Goal:** introduce the async message bus and stand up the surrounding
system surrogates. Commerce Core gains an outbound event channel but
no business behaviour changes yet.

### 1.1 — RabbitMQ in `docker-compose`

- [ ] Add a `rabbitmq` service to `nopcommerce/docker-compose.yml`
      (image `rabbitmq:3-management`, expose 5672 + 15672)
- [ ] Add `RABBITMQ_HOST`, `RABBITMQ_USER`, `RABBITMQ_PASSWORD` env
      vars; default to a dev user
- [ ] Create a new `appsettings.json` section `RabbitMqConfig` bound via
      a new `IConfig` implementation under
      `src/Libraries/Nop.Core/Configuration/RabbitMqConfig.cs`

### 1.2 — Surrounding-system surrogates

- [ ] Add a sibling `compose/` directory at the repo root with one
      compose file per surrogate so they can be brought up independently:
  - `compose/openboxes.yml` — OpenBoxes WMS (decide pinned tag during
    Phase 1, see ISSUE.md §5 — fall back to a thin HTTP stub if the full
    container is too heavy)
  - `compose/erpnext.yml` — ERPNext
  - `compose/ospos.yml` — OSPOS
  - `compose/keycloak.yml` — Keycloak with a `verdemart` realm seeded
  - `compose/wiremock.yml` — WireMock for the Carrier API, with a
    `carrier-rates.json` mappings file checked in
- [ ] Add a top-level `docker-compose.dev.yml` that composes them all
      via `extends`/`include`

### 1.3 — Outbound RabbitMQ publisher (no outbox yet — that's Phase 2)

- [ ] Add `src/Libraries/Nop.Services/Integration/Messaging/IRabbitMqPublisher.cs`
- [ ] Implement `RabbitMqPublisher` using the official `RabbitMQ.Client`
      NuGet package (latest 6.x)
- [ ] Register it via an `INopStartup` implementation
      (`IntegrationStartup.cs`) so existing DI patterns are reused
- [ ] Sanity test: a fire-and-forget publish from a temporary REST
      endpoint (admin-only) lands in the RabbitMQ management UI

**Exit criteria**

- `docker-compose -f docker-compose.yml -f docker-compose.dev.yml up`
  brings up nopCommerce + RabbitMQ + all surrogates
- A manually-triggered publish from nopCommerce reaches RabbitMQ within
  100 ms (verified via management UI)
- Existing nopCommerce tests still pass

**Verification (link to QAS)**

- Foundational only — no QAS measured yet

---

## Phase 2 — Reliable Event Publishing

**Goal:** orders produce durable events via the Outbox pattern. ERP
receives async notifications. Event delivery survives broker and ERP
outages.

**Drivers:** QAS-1, QAS-5 · **ADRs:** ADR-001, ADR-002

### 2.1 — Outbox table + migration

- [ ] Add domain entity
      `src/Libraries/Nop.Core/Domain/Integration/OutboxMessage.cs`
      with fields:
  - `Id` (int, PK, identity)
  - `EventId` (Guid, unique) — for downstream deduplication
  - `EventType` (string) — e.g. `OrderConfirmed`
  - `Payload` (string, JSON)
  - `CreatedOnUtc` (datetime)
  - `PublishedOnUtc` (datetime?, nullable)
  - `Attempts` (int, default 0)
  - `LastError` (string?, nullable)
- [ ] Add LinqToDb mapping
      `src/Libraries/Nop.Data/Mapping/Builders/Integration/OutboxMessageBuilder.cs`
- [ ] Add migration
      `src/Libraries/Nop.Data/Migrations/Integration/2026-XX-XX_AddOutboxMessage.cs`
      attributed `[NopSchemaMigration(typeof(OutboxMessage), ...)]`

### 2.2 — Outbox Writer service

- [ ] Add `IOutboxWriter` interface with `Task EnqueueAsync<TEvent>(TEvent evt, CancellationToken ct)`
- [ ] Implementation `OutboxWriter` that:
  1. Serializes the event to JSON
  2. Inserts an `OutboxMessage` row via `IRepository<OutboxMessage>`
  3. Participates in the ambient LinqToDb transaction (no new
     transaction created)
- [ ] Unit test: writing inside a transaction that rolls back also rolls
      back the outbox entry (use the in-memory SQLite test infra in
      `Nop.Tests`)

### 2.3 — Hook into `IOrderService.InsertOrderAsync`

- [ ] Locate the end of `OrderService.PlaceOrderAsync` after the order
      is persisted but before commit
- [ ] Call `_outboxWriter.EnqueueAsync(new OrderConfirmedEvent(order))`
- [ ] Define `OrderConfirmedEvent` in
      `src/Libraries/Nop.Core/Events/Integration/OrderConfirmedEvent.cs`
      (DTO — minimal serialisable order shape, **not** the full Order
      entity)
- [ ] Add an integration test in `Nop.Tests` that places an order and
      asserts a row appears in `OutboxMessage`

### 2.4 — Outbox Publisher schedule task

- [ ] Add `OutboxPublisherTask : IScheduleTask` under
      `src/Libraries/Nop.Services/Integration/Tasks/`
- [ ] Loop:
  1. Read up to N unpublished entries ordered by `Id`
  2. For each: publish to RabbitMQ exchange `commerce.events` with
     routing key `EventType`
  3. On success: set `PublishedOnUtc`
  4. On failure: increment `Attempts`, set `LastError`; back off
     exponentially; do not throw
- [ ] Insert a `ScheduleTask` row via migration (default interval 30 s)

### 2.5 — ERP consumer (in the ERPNext surrogate)

- [ ] Configure the ERPNext stub to consume `commerce.events` with
      routing key `OrderConfirmed` and persist into ERPNext's Sales
      Order doctype
- [ ] If using a thin stub instead of full ERPNext, write a minimal
      Python/Node consumer that just logs receipt and persists to a
      local SQLite

### 2.6 — Validate QAS-1 (manual)

- [ ] Bring down the ERP surrogate
- [ ] Place an order — checkout completes successfully
- [ ] Verify `OutboxMessage` row has `PublishedOnUtc` set (published to
      RabbitMQ) but ERP queue is backed up
- [ ] Bring ERP back up — verify the event is consumed within 5 min

**Exit criteria**

- QAS-1 acceptance test passes (zero lost orders during ERP outage)
- No regression in existing nopCommerce tests
- Outbox publisher runs continuously without errors over a 30-min soak

---

## Phase 3 — Cross-Channel Stock

**Goal:** Commerce Core holds a live replica of physical stock. WMS
changes propagate inbound. Reservation API enforces consistency across
web and POS. Staleness becomes observable.

**Drivers:** QAS-2, QAS-5 · **ADRs:** ADR-002, ADR-005

> Per the presentation, Phase 3 covers stock replica + WMS-inbound +
> staleness observability + the Reservation API. **Order State
> Visibility (QAS-4)** is part of Phase 4 — see §4.5.

### 3.1 — StockLedger entity + migration

- [ ] Extend the existing inventory representation rather than replacing
      it. Add a new entity
      `src/Libraries/Nop.Core/Domain/Inventory/StockLedgerEntry.cs`:
  - `Id`, `ProductId`, `WarehouseId`
  - `StockQuantity` (int)
  - `ReservedQuantity` (int)
  - `LastUpdatedAtUtc` (datetime)
  - `IsStale` (bool)
  - row-version / `ConcurrencyToken` (byte[]) for optimistic concurrency
- [ ] Migration creates the table and back-fills from the existing
      `Product.StockQuantity`
- [ ] Read path of `IProductService` updated to read available =
      `StockQuantity - ReservedQuantity` from `StockLedgerEntry`. Keep
      writes to `Product.StockQuantity` for backward compatibility — the
      ledger becomes the source of truth, the column becomes a
      denormalised cache

### 3.2 — ProcessedEvents table for idempotency (ADR-005)

- [ ] Entity `ProcessedEvent { Id, EventId (Guid, unique), ProcessedAtUtc }`
- [ ] Migration creates the table with a unique index on `EventId`
- [ ] Helper `IIdempotencyGuard.HasSeen(EventId)` /
      `MarkProcessed(EventId)` used by every inbound consumer in the
      same DB transaction as the business write

### 3.3 — WMS Event Consumer

- [ ] Add `WmsStockPickedConsumer : IConsumer<...>` under
      `src/Libraries/Nop.Services/Inventory/Consumers/`
- [ ] Subscribe to the RabbitMQ queue `wms.stock.picked`
- [ ] On message:
  1. Check `IIdempotencyGuard` — if seen, ack and skip
  2. Decrement `StockLedgerEntry.StockQuantity` atomically (optimistic
     concurrency retry on conflict, max 3 attempts)
  3. Mark `LastUpdatedAtUtc = now`, `IsStale = false`
  4. Persist `ProcessedEvent`
  5. Ack the RabbitMQ message
- [ ] Use a single hosted RabbitMQ consumer service registered via
      `INopStartup` so the consumer lives as long as the app

### 3.4 — Reservation API (QAS-2)

- [ ] New controller
      `src/Presentation/Nop.Web/Areas/Api/Inventory/ReservationsController.cs`
      with `POST /api/inventory/reservations`
- [ ] Request DTO: `{ productId, warehouseId, quantity, idempotencyKey }`
- [ ] Behaviour:
  1. Open transaction
  2. `SELECT ... FOR UPDATE` (or LinqToDb equivalent — row-level lock)
     on `StockLedgerEntry`
  3. If `StockQuantity - ReservedQuantity >= quantity`: increment
     `ReservedQuantity`, commit, return 200 with `reservationId`
  4. Else: 409 Conflict
- [ ] Load test: 50 concurrent requests for a single-unit item → exactly
      1× 200 and 49× 409 (this is the QAS-2 acceptance test)
- [ ] Wire the existing checkout path to call the Reservation API
      *before* `PlaceOrderAsync`
- [ ] Add a complementary endpoint to **release** a reservation (used
      when checkout abandons or order is cancelled)

### 3.5 — Staleness Checker

- [ ] `StalenessCheckerTask : IScheduleTask` runs every 60 s
- [ ] Marks `IsStale = true` on any `StockLedgerEntry` where
      `LastUpdatedAtUtc < now - threshold` (configurable, default
      10 min)
- [ ] Exposes a metric/log line per stale row for the dashboard

**Exit criteria**

- QAS-2 and QAS-5 acceptance tests pass (QAS-4 is exit criterion for
  Phase 4)
- WMS reconnection test: 2 min pause on OpenBoxes, buffered events
  drain in < 2 min with zero duplicate deductions
- No regression in existing nopCommerce tests

---

## Phase 4 — Resilience Layer

**Goal:** Commerce Core degrades gracefully when surrounding systems
fail. Carrier circuit breaker + cached fallback. Stale state is visible,
not silent. Cross-channel order visibility enabled for POS.

**Drivers:** QAS-3, QAS-4 · **ADRs:** ADR-003, ADR-005

### 4.1 — Carrier Adapter with circuit breaker (QAS-3)

- [ ] Add `ICarrierAdapter` with `Task<ShippingRate> GetRateAsync(...)`
- [ ] Implementation uses `Polly` (already a transitive dep or add
      explicitly) with a circuit-breaker policy:
  - Break threshold: 3 consecutive failures or 50% failure rate over
    10 s window
  - Break duration: 30 s
  - On break: invoke `IRateFallbackProvider`
- [ ] `IRateFallbackProvider` returns the most recent cached rate per
      shipping zone (kept in `IStaticCacheManager`)
- [ ] Wire the checkout shipping calculator to use `ICarrierAdapter`
      instead of any direct HTTP call

### 4.2 — Fallback rate cache refresher

- [ ] `CarrierRateRefreshTask : IScheduleTask` runs every 15 min while
      the circuit is closed; refreshes the per-zone cached rates

### 4.3 — Stale-stock indicator in storefront UI

- [ ] Modify the product page Razor view to show
      `"Stock as of {LastUpdatedAtUtc:hh:mm}; live data temporarily unavailable"`
      when `IsStale == true`
- [ ] Same indicator in cart and checkout
- [ ] Copy reviewed by team — present "as of" not "may be wrong" (per
      ADR-003 historical decision, now QAS-cross-cutting)

### 4.4 — Admin / Ops Dashboard

- [ ] New admin area page `/Admin/Operations`
- [ ] Three panels:
  1. **Stale stock** — list of `StockLedgerEntry` rows with
     `IsStale = true`, sortable by `LastUpdatedAtUtc`
  2. **Outbox depth** — count of `OutboxMessage` rows with
     `PublishedOnUtc IS NULL`, grouped by `EventType`; alert badge if
     oldest is > 5 min
  3. **Circuit-breaker state** — current state of the Carrier circuit
     (Closed / Open / Half-open) and time-in-state

### 4.5 — Cross-Channel Order Visibility (QAS-4)

- [ ] Consumer `WmsShipmentDispatchedConsumer` handles `TrackingUpdated`
      events and updates the order's `Shipment.TrackingNumber` and
      `Shipment.ShippedDateUtc`
- [ ] Add `GET /api/orders/{id}/status` returning the consolidated
      cross-channel order state
- [ ] Wire the endpoint into the OSPOS stub so a clerk can look up an
      online order from the in-store terminal
- [ ] Acceptance: from `TrackingUpdated` publication to API reflecting
      the change < 10 s under load (QAS-4)

**Exit criteria**

- QAS-3 acceptance test passes (97% checkout rate during carrier
  degradation, fallback < 100 ms)
- Ops Dashboard is reachable and panels render correct counts
- Stale indicator visible on a product page when staleness is induced
  (pause WMS for > 10 min and re-render the page)

---

## Phase 5 — Pressure Validation

**Goal:** failure scenarios exercised and measured. Recovery paths
verified. Evidence pack produced. Known limitations documented.

### 5.1 — Chaos / failure scripts

- [ ] `evidence/scripts/qas-1-erp-outage.sh` — stops ERPNext, runs N
      orders, restarts ERPNext, asserts all events delivered
- [ ] `evidence/scripts/qas-2-reservation-race.sh` — runs the concurrent
      reservation test using `wrk` or a custom k6 script
- [ ] `evidence/scripts/qas-3-carrier-degraded.sh` — uses WireMock admin
      API to inject 503 + 5 s delay for 30% of requests
- [ ] `evidence/scripts/qas-4-order-visibility.sh` — places order,
      publishes `TrackingUpdated` event, polls API for status reflection
- [ ] `evidence/scripts/qas-5-wms-reconnect.sh` — pauses OpenBoxes for
      2 min, then resumes; asserts reconciliation completes in < 2 min
      with zero duplicate deductions

### 5.2 — Measurement & evidence pack

- [ ] Each script writes a structured JSON result to `evidence/results/`
- [ ] A summary `evidence/REPORT.md` aggregates the runs (p50/p95/p99
      where applicable, pass/fail per QAS)
- [ ] Screenshots / GIF of the Ops Dashboard during each scenario

### 5.3 — Known limitations doc

- [ ] `docs/known-limitations.md` lists:
  - Single RabbitMQ instance (SPOF risk from architectural risks table)
  - Outbox publisher polling interval = floor on event-delivery latency
  - Fallback carrier rates may diverge from live rates over time
  - WireMock-based carrier does not replicate real-world rate variation
  - OpenBoxes / ERPNext surrogates differ from production deployments in
    schema and update frequency

**Exit criteria**

- Every QAS has a script that exercises it and a JSON evidence file
- `evidence/REPORT.md` exists and is referenced from the final report
- `known-limitations.md` is honest about what the demo does and does
  not prove

---

## Phase 6 — Demo

**Goal:** end-to-end flows rehearsed. Pressure point demonstrated live.
Architecture report and ADR set finalised.

### 6.1 — Demo script

- [ ] `docs/demo-script.md` with a step-by-step narrative:
  1. Show normal operation: place an order on the storefront, observe
     event flowing to ERPNext and order assigned to OpenBoxes
  2. Show cross-channel stock reservation: POS reserves last unit; web
     attempts to buy the same unit → 409 with friendly message
  3. Inject the pressure: kill the Carrier API (WireMock 503) — show
     circuit breaker tripping; checkout completes with fallback rate;
     dashboard shows circuit open
  4. Recovery: restore Carrier API; show half-open probe; circuit
     closes; live rates resume
  5. Stretch: pause OpenBoxes for 2 min; show outbox depth growing;
     resume; show reconciliation in < 2 min

### 6.2 — Rehearsal

- [ ] Full dry-run with timer: 15 min including demo
- [ ] One team member operates the demo, another narrates, two
      monitor logs/dashboard
- [ ] Identify the 2-3 places most likely to fail live; pre-record a
      backup video for each

### 6.3 — Document finalisation

- [ ] `architecture-report.md` updated with anything learned from
      Phase 5 evidence
- [ ] ADRs reviewed: any that were modified during implementation get
      a "Superseded by" / amendment note
- [ ] `evidence/REPORT.md` final
- [ ] Tag `phase-6-final` in git

**Exit criteria**

- Demo runs end-to-end in under 15 min without manual intervention
- All ADRs match the implemented system (no drift)
- Evidence pack is complete

---

## Cross-cutting work (any phase)

These items are not phase-bound but must be respected throughout:

- **Do not introduce EF Core.** Data access stays LinqToDb +
  `IRepository<T>`. Mappings go in
  `src/Libraries/Nop.Data/Mapping/Builders/`.
- **Use existing patterns.** New tasks → `IScheduleTask`; new consumers
  → `IConsumer<TEvent>` where applicable; new config → `IConfig`; new
  DI → `INopStartup`. Don't invent new infrastructure.
- **Migrations are forward-only and attributed.** Use
  `[NopSchemaMigration(typeof(EntityName), "yyyy/MM/dd HH:mm:ss")]`.
- **Plugins reference only `Nop.Web.Framework`.** Integration code that
  must be cross-cutting lives in `Nop.Services`, not in `Nop.Web`.
- **Internal `IEventPublisher` bus stays intact.** Outbox publishing is
  in addition to, not instead of, internal events (cache invalidation
  depends on the in-memory bus).
- **Tests:** every new service has at least one unit test in
  `src/Tests/Nop.Tests/`. Integration tests for cross-channel scenarios
  go in a new `Nop.IntegrationTests` project if the existing infra
  cannot host them.

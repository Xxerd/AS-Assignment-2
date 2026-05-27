# VerdeMart — Architecture Report
## Architectural Evolution of nopCommerce · Scenario C: Omnichannel Commerce Core

**Group Assignment 02 — Software Architectures · Master in Informatics Engineering · UA**
**Checkpoint Date:** 05/05/2026

**Authors:**
- Francisco Pinto — 113763
- Bruno Meixedo — 113372
- André Alves — 113962
- José Marques — 114321

---

## 1. Scenario Choice

**Chosen Scenario:** C — Omnichannel Commerce Core (VerdeMart Retail)

VerdeMart began as an online retailer running nopCommerce as a web storefront. The business now operates across multiple channels: web, physical store (POS), warehouse execution (WMS), back-office ERP, and shipping carriers. Each channel produces and consumes commerce data independently.

**The architectural problem is not integration. It is behavior under disagreement.**

When the WMS says stock is 0 but nopCommerce says 5, when the ERP is down during peak hours, when a POS sale happens offline — what does the commerce core do? That is the problem this evolution must solve.

### Importance from a Business Perspective

| Concern | Impact |
|---|---|
| **Overselling** | Stock inconsistency across channels causes fulfillment failures and customer refunds |
| **Outage** | An ERP outage should not block online checkouts — revenue loss is direct and measurable |
| **No Event Trail** | Without domain events, cross-channel reconciliation after failures is manual and error-prone |
| **Cross-channel visibility** | A customer who bought online and wants to return in-store cannot be served if order state is not visible cross-channel |
| **Operational observability** | Operations teams are blind to inconsistency unless the system makes it visible |

---

## 2. Current-State Analysis of nopCommerce

### How nopCommerce currently works

nopCommerce is a **modular monolith** with a layered/onion structure:

```
Nop.Core → Nop.Data → Nop.Services → Nop.Web.Framework → Nop.Web
```

All commerce data (products, inventory, orders, customers, shipments) is owned and managed internally. There are no integrations with external operational systems. The system is designed as a self-contained storefront.

### Gap Analysis

| nopCommerce Baseline | VerdeMart Need | Gap |
|---|---|---|
| Stock is a single `StockQuantity` field on `Product` entity, managed internally | Cross-channel stock visibility | No mechanism to receive or reflect WMS stock updates |
| Orders are created and tracked entirely in nopCommerce DB | ERP order notification | No event publishing to external systems |
| No concept of in-store sales | POS awareness | Online and physical stock are independent silos |
| `Shipment` entity tracks internal state only | Fulfillment tracking | No incoming updates from warehouse or carriers |
| Cache invalidation is internal | Stale data visibility | No staleness indicator when external data is outdated |

### Architectural seams identified (injection points)

These are the existing boundaries in nopCommerce where the integration layer can be inserted without breaking core logic:

- **`IInventoryService`** (`Nop.Services/Catalog/`) — all stock read/write operations pass through here
- **`IOrderService`** (`Nop.Services/Orders/`) — order creation and lifecycle
- **`IShipmentService`** (`Nop.Services/Shipping/`) — fulfillment state
- **`IEventPublisher`** (`Nop.Core/Events/`) — fires domain events; already used for cache invalidation. This is the outbound integration point.
- **`IConsumer<TEvent>`** — consumers are auto-discovered at startup; this is the inbound integration point for external events
- **`IScheduleTask`** — background jobs for periodic reconciliation and outbox publishing

### Pressure Points

| Pressure Point | Description |
|---|---|
| **Inventory Race Condition** | Web and POS channels can simultaneously reduce stock for the same item with no cross-channel reservation mechanism |
| **Stale Stock Visibility** | WMS updates (receipts, picks, damage writes) are not propagated back to nopCommerce in near-real-time |
| **Carrier API Fragility** | Synchronous carrier calls at checkout make shipping rate retrieval a single point of failure |
| **Synchronous ERP Coupling** | Any ERP call at order time creates a hard dependency — ERP downtime equals checkout failure |

---

## 3. Domain and Boundary Model

### Subdomains

| Subdomain | Type | Description |
|---|---|---|
| Catalog & Pricing | Core | Product data, pricing rules, promotions |
| Order Management | Core | Checkout, order lifecycle, payment |
| Inventory | Core (shared) | Stock levels, reservations |
| Fulfillment | Supporting | Pick/pack/ship execution |
| POS | Supporting | In-store sales and stock consumption. OSPOS stub. |
| ERP / Finance | Supporting | Financial records, purchase orders |
| Customer Identity | Generic | Customer data, authentication. Keycloak + nopCommerce customer model. |
| Carrier / Shipping | Generic | Rate retrieval and shipment tracking. Simulated via WireMock. |

### Bounded Contexts

```
┌────────────────────────────────────────────────────────────────┐
│                Commerce Core — nopCommerce                      │
│                                                                │
│  ┌─────────────┐  ┌──────────────┐  ┌──────────────────────┐  │
│  │  Catalog    │  │    Order     │  │  Inventory Replica   │  │
│  │ & Pricing   │  │ Management   │  │  (read-only, owned   │  │
│  │ (Core)      │  │ (Core)       │  │   by WMS truth)      │  │
│  └─────────────┘  └──────┬───────┘  └──────────────────────┘  │
│                          │                                     │
│  ┌─────────────┐    ┌────▼─────────────────────────────┐      │
│  │ Customer &  │    │  Integration Layer               │      │
│  │ Identity    │    │  Adapters + Outbox               │      │
│  │ (Keycloak + │    │  Publishes domain events ·       │      │
│  │ nopCommerce)│    │  at-least-once delivery          │      │
│  └─────────────┘    └────────────┬─────────────────────┘      │
└──────────────────────────────────┼─────────────────────────────┘
                                   │  publish
                                   ▼
                          ┌────────────────┐
                          │   RabbitMQ     │
                          │ Async event bus│
                          └───┬───┬────┬───┘
        order.placed ─────────┤   │    └─── shipment.requested
        stock.updated  ◄──────┘   ▼
                                  │
   ┌──────────────┬───────────────┼───────────────┬──────────────┐
   ▼              ▼               ▼               ▼              ▼
┌────────┐   ┌─────────┐    ┌─────────┐    ┌───────────┐   ┌──────────┐
│  WMS   │   │   ERP   │    │   POS   │    │  Carrier  │   │ Keycloak │
│OpenBoxes│  │ ERPNext │    │  OSPOS  │    │  WireMock │   │          │
└────────┘   └─────────┘    └─────────┘    └───────────┘   └──────────┘
```

### Data Ownership

| Data | Owned by |
|---|---|
| Product catalog | Commerce Core |
| Order state | Commerce Core |
| Physical stock (truth) | WMS (OpenBoxes) |
| Stock Replica (available to sell) | Commerce Core |
| Financial records | ERP (ERPNext) |
| Reservations | Commerce Core |
| Carrier rates | Carrier API |
| Customer identity | Keycloak + Commerce Core customer model |

---

## 4. Quality Attribute Scenarios

| ID | Attribute | Scenario | Measure |
|---|---|---|---|
| **QAS-1** | Availability | ERP Unavailability During Checkout | Checkout success ≥ 99%. ERP eventually consistent within 5 min of recovery. |
| **QAS-2** | Consistency | Cross-Channel Stock Reservation | Zero oversell. Reservation API P99 latency ≤ 200 ms. |
| **QAS-3** | Resilience | Carrier API Degradation at Checkout | Checkout rate ≥ 97% during degradation. Fallback in < 100 ms. |
| **QAS-4** | Traceability | Order State Visibility Across Channels | Order status reflects WMS events within 10 seconds. |
| **QAS-5** | Recoverability | Stock Reconciliation After WMS Reconnection | Full reconciliation in < 2 min. No duplicate stock deductions. |

---

## 5. Chosen Framework — ADD (Attribute-Driven Design)

### Why ADD

- Scenario C is defined by quality attribute tensions — availability vs. consistency, resilience vs. simplicity.
- ADD drives decomposition from QAS, not from functional features.
- ADD's iterative approach lets us keep the monolith and add only what the QAS demand.
- Explicit traceability from QAS to ADR to implementation.
- Lighter than TOGAF/ADM, more implementation-oriented. Compared to ACDM, quality attributes come first.

### Why not ADM

- More useful for larger enterprise systems
- Adds unnecessary overhead for a focused architectural evolution
- Too broad and high-level for the granularity required here

### Why not ACDM

- Stricter phases and heavier process
- Less directly focused on quality attributes

### How we applied ADD

1. Identified the primary quality attribute drivers from the scenario (QAS-1 to QAS-5)
2. Selected the architectural patterns that respond to each driver (outbox, circuit breaker on Carrier, async messaging, reservation API, staleness metadata)
3. Allocated responsibilities to elements (integration layer, message bus, adapters, inventory module)
4. Produced the target architecture below
5. Recorded each major decision as an ADR with rejected alternatives

---

## 6. Target Architecture

### Overview

nopCommerce remains the **commerce core monolith**. The evolution adds an **integration layer** inside the monolith and an **inventory module** that owns the cross-channel stock ledger and reservations. Surrounding systems are represented by real open-source stubs (OpenBoxes, ERPNext, OSPOS, Keycloak) and a WireMock carrier surrogate.

This is a deliberate scope decision (see ADR-004): extracting services from nopCommerce is not justified by the scenario. The architectural pressure is about _behavior under external system failure_, not about scaling catalog or checkout independently.

### Components

```
┌──────────────────────────────────────────────────────────────────────────┐
│                       Commerce Core (nopCommerce)                         │
│                                                                          │
│  ┌───────────┐  ┌───────────┐  ┌──────────────┐  ┌────────────────────┐ │
│  │  Catalog  │  │   Order   │  │   Carrier    │  │   Admin / Ops      │ │
│  │  Service  │  │  Service  │  │   Adapter    │  │   Dashboard        │ │
│  │           │  │           │  │ (circuit br.)│  │ (staleness, queue) │ │
│  └─────┬─────┘  └─────┬─────┘  └──────┬───────┘  └─────────┬──────────┘ │
│        │              │               │                    │            │
│  ┌─────┴──────────────┴───┐    ┌──────┴────────────────────┴─────────┐ │
│  │   Integration Layer    │    │        Inventory Module             │ │
│  │  ┌────────┐ ┌────────┐ │    │  ┌─────────────┐  ┌──────────────┐ │ │
│  │  │ Outbox │ │ Outbox │ │    │  │ Reservation │  │ WMS Event    │ │ │
│  │  │ Writer │ │Publisher│ │    │  │     API     │  │ Consumer     │ │ │
│  │  └────┬───┘ └────┬───┘ │    │  └──────┬──────┘  └──────┬───────┘ │ │
│  │       │  ┌───────┴───┐ │    │         │                │         │ │
│  │       │  │   Event   │ │    │         │     ┌──────────┴──────┐  │ │
│  │       │  │  Consumer │ │    │         │     │ Staleness Check │  │ │
│  │       │  └───────────┘ │    │         │     └─────────────────┘  │ │
│  └───────┼──────────┼─────┘    └─────────┼────────────┼─────────────┘ │
│          │          │                    │            │               │
│  ┌───────▼──────────▼────────────────────▼────────────▼─────────────┐ │
│  │                       nopCommerce DB                              │ │
│  │  StockLedger (StockQuantity, LastUpdatedAt, IsStale, Reservations)│ │
│  │  OutboxMessage   ·   ProcessedEvents                              │ │
│  └────────────────────────────────────────────────────────────────────┘│
└───────────────────────────┬──────────────────────────────────────────────┘
                            │  publish / consume
                            ▼
                      ┌──────────┐
                      │ RabbitMQ │
                      └────┬─────┘
            ┌──────────────┼──────────────┬────────────────┐
   StockPicked  OrderConfirmed   OrderAssigned    TrackingUpdated
            │              │              │                │
            ▼              ▼              ▼                ▼
   ┌─────────────────────────────────────────────────────────────┐
   │                    External Systems                          │
   │  ┌──────────┐  ┌────────┐  ┌────────┐  ┌────────────────┐  │
   │  │OpenBoxes │  │ERPNext │  │ OSPOS  │  │  Carrier API   │  │
   │  │  (WMS)   │  │ (ERP)  │  │ (POS)  │  │   (WireMock)   │  │
   │  └──────────┘  └────────┘  └────────┘  └────────────────┘  │
   │                          (HTTP rate retrieval / reserve)    │
   └──────────────────────────────────────────────────────────────┘
```

### Key Components

- **Integration Layer (inside nopCommerce):**
  - `Outbox Writer` — writes outbox entries in the same DB transaction as business operations
  - `Outbox Publisher` — `IScheduleTask` that polls unpublished entries and publishes to RabbitMQ
  - `Event Consumer` — receives inbound events from RabbitMQ (e.g., `OrderConfirmed` ack)
- **Inventory Module:**
  - `Reservation API` — synchronous endpoint for cross-channel stock reservation (QAS-2)
  - `WMS Event Consumer` — handles `StockPicked`, `TrackingUpdated` events from OpenBoxes
  - `Staleness Checker` — `IScheduleTask` that marks stock entries stale when `LastUpdatedAt` is older than the threshold
- **Carrier Adapter** — synchronous HTTP client with circuit breaker; falls back to cached/default rates (QAS-3)
- **Admin / Ops Dashboard** — surfaces staleness, outbox queue depth, and circuit-breaker state
- **External Stubs (Docker):**
  - OpenBoxes (WMS), ERPNext (ERP), OSPOS (POS), Keycloak (identity), WireMock (Carrier API)

### Event Flows

| Event | Direction | Mode | Purpose |
|---|---|---|---|
| `OrderConfirmed` / `OrderPlaced` | Out → ERP | Async (RabbitMQ + Outbox) | Notify ERPNext of completed orders |
| `OrderAssigned` | Out → WMS | Async (RabbitMQ + Outbox) | Hand off fulfillment to OpenBoxes |
| `StockPicked` | In ← WMS | Async (RabbitMQ) | Adjust stock replica after picks |
| `TrackingUpdated` | In ← WMS / Carrier | Async (RabbitMQ) | Update shipment status (QAS-4) |
| `Reserve Stock` | Sync HTTP | In | POS / Web call Reservation API (QAS-2) |
| `Rate retrieval` | Sync HTTP → Carrier | Out | Carrier Adapter with circuit breaker (QAS-3) |

### Synchronous vs Asynchronous Interactions

| Interaction | Mode | Reliability mechanism |
|---|---|---|
| Customer checkout | Sync (HTTP) | Transactional with outbox write |
| Order → ERP notification | Async (RabbitMQ) | Outbox pattern |
| WMS stock update → Core | Async (RabbitMQ) | At-least-once delivery, idempotent consumer |
| POS / Web stock reservation | Sync (HTTP) | Reservation API with atomic decrement |
| Core → WMS fulfillment request | Async (RabbitMQ) | Outbox + retry policy |
| Carrier rate retrieval | Sync (HTTP) | Circuit breaker + cached fallback |
| Staleness check | Internal (ScheduleTask) | Periodic background job |

### Cross-Cutting Concerns

- **Reliability:** Outbox pattern prevents dual-write loss; all outbound events are written transactionally before publishing
- **Resilience:** Circuit breaker on Carrier API; stock replica served with `IsStale` indicator when WMS lags
- **Consistency:** Reservation API enforces atomic stock decrement across channels (QAS-2)
- **Observability:** `LastUpdatedAt` + `IsStale` on every replicated data point; structured logging on all integration events; ops dashboard
- **Idempotency:** All inbound consumers are idempotent — processing the same message twice produces the same result (deduplication by `EventId` via `ProcessedEvents` table)
- **Recoverability:** WMS reconnection triggers reconciliation of buffered events without duplicates (QAS-5)

---

## 7. Architectural Decisions (ADRs)

ADRs are maintained in `docs/adr/`. Summary:

| ADR | Decision | Drivers | Status |
|---|---|---|---|
| ADR-001 | Async integration via RabbitMQ instead of synchronous HTTP | QAS-1, QAS-5 | Accepted |
| ADR-002 | Outbox pattern for reliable event publishing | QAS-1, QAS-5 | Accepted |
| ADR-003 | Circuit breaker on Carrier API with fallback rates | QAS-3 | Accepted |
| ADR-004 | Keep Commerce Core as a monolith; do not extract services | Scope constraint | Accepted |
| ADR-005 | Idempotent inbound consumers with deduplication by event ID | QAS-5 | Accepted |

ADR-001, ADR-002, and ADR-004 are the three checkpoint-mandated ADRs. ADR-003 and ADR-005 are supporting decisions that implement specific QAS responses.

See individual ADR files for context, rationale, and rejected alternatives.

---

## 8. Risk and Validation Plan

### Architectural Risks

| Decision | Risk | Severity | Description |
|---|---|---|---|
| Keep Commerce Core as a Monolith | Monolith becomes a bottleneck as omnichannel load grows | Medium | Accumulated coupling over time — new cross-channel requirements may silently break the "add only what QAS demands" principle |
| Outbox Pattern for Reliable Event Publishing | Outbox Relay Lag under Load | Medium | High order volume could create outbox backlog, delaying ERP/WMS notification |
| Asynchronous Integration via RabbitMQ | RabbitMQ Single Point of Failure | Medium | If RabbitMQ is unavailable, all async integrations stall (checkout still works via outbox) |
| Outbox Pattern for Reliable Event Publishing | WMS Event Ordering | Low-Medium | Out-of-order WMS events could corrupt inventory state |

### Validation Plan

| QAS | Steps | Assertion |
|---|---|---|
| **QAS-1 — ERP Availability** | Bring down ERPNext container during checkout load test | Orders complete. ERP records created after recovery. Zero lost orders. |
| **QAS-2 — Stock Consistency** | Run concurrent reservation test: 50 simultaneous requests for a single-unit item | Exactly 1 success (HTTP 200), 49 conflicts (HTTP 409). Zero oversell. |
| **QAS-3 — Carrier Resilience** | Configure WireMock with 5s delay and 503 responses for 30% of requests | Circuit opens after 3 failures. Fallback rates served. Checkout completes. |
| **QAS-4 — Order Visibility** | Place order, trigger WMS `ShipmentDispatched` event, query Commerce Core order status | Order status updated within 10 seconds of event publication. |
| **QAS-5 — WMS Reconnect** | Pause OpenBoxes for 2 minutes. Process picks locally. Reconnect. | Buffered events processed. Stock reconciled within 2 min. No duplicate deductions. |

---

## 9. Evolution Roadmap

```
Phase 0 — Baseline
  └── nopCommerce as-is. Single-channel web store. No event bus. No external integrations.
      No cross-channel stock visibility.

Phase 1 — Infrastructure Foundation
  └── Async message bus introduced. Surrounding system surrogates deployed
      (OpenBoxes, ERPNext, OSPOS, Keycloak, WireMock). Commerce Core gains an outbound event channel.

Phase 2 — Reliable Event Publishing
  └── Orders produce durable events via Outbox. ERP receives async notifications.
      Event delivery survives broker and ERP outages.

Phase 3 — Cross-Channel Stock
  └── Commerce Core holds a live replica of physical stock. WMS changes propagate inbound.
      Reservation API enforces consistency across web and POS. Staleness becomes observable.

Phase 4 — Resilience Layer
  └── Commerce Core degrades gracefully when surrounding systems fail. Carrier Adapter circuit
      breaker + fallback rates. Stale state is visible, not silent. Cross-channel order visibility
      enabled for POS.

Phase 5 — Pressure Validation
  └── Failure scenarios exercised and measured (per Validation Plan). Recovery paths verified.
      Evidence pack produced. Known limitations documented.

Phase 6 — Demo
  └── End-to-end flows rehearsed. Pressure point demonstrated live. Architecture report and
      ADR set finalised.
```

### Coexistence During Transition

- Internal `IEventPublisher` bus stays intact for cache invalidation and existing consumers — the outbox publisher adds RabbitMQ delivery alongside it, not instead of it
- External systems are stubs/surrogates (OpenBoxes, ERPNext, OSPOS, Keycloak, WireMock-Carrier) — interfaces are written so real systems could replace them without changing nopCommerce
- The `StockQuantity` read path remains unchanged from nopCommerce's perspective — the inventory module updates it; existing services do not change

---

## 10. Feasibility Spike

### Spike Goal

Validate the end-to-end path from order creation to RabbitMQ delivery using the outbox pattern, without breaking nopCommerce's existing internal event bus.

### Spike Scope

1. Create the `OutboxMessage` table via a FluentMigrator migration
2. Modify `IOrderService.InsertOrderAsync` to write an `OutboxMessage` entry in the same LinqToDb transaction as the order insert
3. Implement the Outbox Publisher `IScheduleTask` — polls unpublished entries and publishes to a RabbitMQ exchange
4. Verify `OrderPlacedEvent` appears in RabbitMQ management UI within the publisher polling interval after checkout
5. Measure end-to-end time from order creation to RabbitMQ delivery across 10 orders

### Spike Success Criteria

- [ ] `OutboxMessage` entry created in the same transaction as the order
- [ ] `OrderPlacedEvent` appears in RabbitMQ management UI within 60 s of order creation
- [ ] Existing internal `IConsumer<OrderPlacedEvent>` implementations still fire correctly
- [ ] No breaking changes to `Nop.Services` or `Nop.Core`
- [ ] Checkout response-time increase due to outbox write < 20 ms (p95)

### Risk if Spike Fails

If decorating `IEventPublisher` or writing through the existing service interface is not viable, the fallback is to publish from `IOrderService.InsertOrderAsync` directly after the DB write, using the outbox as the intermediate — this decouples the event publish from the DI event bus entirely.

---

## Appendix: Repository Structure

```
/
├── docs/
│   ├── architecture-report.md        ← this file
│   ├── ISSUE.md                      ← discrepancies found vs presentation
│   ├── adr/
│   │   ├── ADR-001-async-rabbitmq.md
│   │   ├── ADR-002-outbox-pattern.md
│   │   ├── ADR-003-circuit-breaker.md
│   │   ├── ADR-004-monolith-boundary.md
│   │   └── ADR-005-idempotent-consumers.md
│   └── diagrams/
│       └── target-architecture.drawio
├── nopcommerce/                      ← baseline nopCommerce (unmodified)
│   └── src/
│       └── ...
└── CLAUDE.md
```

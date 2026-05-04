# VerdeMart — Architecture Report
## Architectural Evolution of nopCommerce · Scenario C: Omnichannel Commerce Core

---

## 1. Scenario Choice

**Chosen Scenario:** C — Omnichannel Commerce Core (VerdeMart Retail)

VerdeMart began as an online retailer running nopCommerce as a web storefront. The business now operates across multiple channels: web, physical store (POS), warehouse execution (WMS), back-office ERP, and shipping carriers. Each channel produces and consumes commerce data independently.

**The architectural problem is not integration. It is behavior under disagreement.**

When the WMS says stock is 0 but nopCommerce says 5, when the ERP is down during peak hours, when a POS sale happens offline — what does the commerce core do? That is the problem this evolution must solve.

**Why this scenario matters from a business perspective:**

- Overselling due to stock inconsistency across channels causes fulfillment failures and customer refunds
- An ERP outage should not block online checkouts — revenue loss is direct and measurable
- A customer who bought online and wants to return in-store cannot be served if order state is not visible cross-channel
- Operations teams are blind to inconsistency unless the system makes it visible

---

## 2. Current-State Analysis of nopCommerce

### How nopCommerce currently works

nopCommerce is a **modular monolith** with a layered/onion structure:

```
Nop.Core → Nop.Data → Nop.Services → Nop.Web.Framework → Nop.Web
```

All commerce data (products, inventory, orders, customers, shipments) is owned and managed internally. There are no integrations with external operational systems. The system is designed as a self-contained storefront.

### How this conflicts with the VerdeMart scenario

| VerdeMart Need | nopCommerce Baseline | Gap |
|---|---|---|
| Cross-channel stock visibility | Stock is a single `StockQuantity` field on `Product` entity, managed internally | No mechanism to receive or reflect WMS stock updates |
| ERP order notification | Orders are created and tracked entirely in nopCommerce DB | No event publishing to external systems |
| POS awareness | No concept of in-store sales | Online and physical stock are independent silos |
| Fulfillment tracking | `Shipment` entity tracks internal state only | No incoming updates from warehouse or carriers |
| Stale data visibility | Cache invalidation is internal | No staleness indicator when external data is outdated |

### Architectural seams identified (injection points)

These are the existing boundaries in nopCommerce where the integration layer can be inserted without breaking core logic:

- **`IInventoryService`** (`Nop.Services/Catalog/`) — all stock read/write operations pass through here
- **`IOrderService`** (`Nop.Services/Orders/`) — order creation and lifecycle
- **`IShipmentService`** (`Nop.Services/Shipping/`) — fulfillment state
- **`IEventPublisher`** (`Nop.Core/Events/`) — fires domain events; already used for cache invalidation. This is the outbound integration point.
- **`IConsumer<TEvent>`** — consumers are auto-discovered at startup; this is the inbound integration point for external events
- **`IScheduleTask`** — background jobs for periodic reconciliation

### Pressure points

1. **Dual-write risk**: if an order is saved and then the ERP notification fails, the systems diverge silently
2. **Availability dependency**: if the WMS must confirm stock before checkout completes, WMS downtime blocks purchases
3. **No staleness signal**: cached stock has no age indicator — the UI cannot tell the customer that data may be stale
4. **No reconciliation**: there is no mechanism to detect and correct drift between nopCommerce stock and WMS stock

---

## 3. Domain and Boundary Model

### Relevant Subdomains

| Subdomain | Type | Description |
|---|---|---|
| Catalog & Pricing | Core | Product data, pricing rules, promotions |
| Order Management | Core | Checkout, order lifecycle, payment |
| Inventory | Core (shared) | Stock levels, reservations |
| Fulfillment | Supporting | Pick/pack/ship execution |
| ERP / Finance | Generic | Financial records, purchase orders |
| POS | Supporting | In-store sales and returns |
| Identity | Generic | Authentication across channels |

### Bounded Contexts

```
┌─────────────────────────────────────────────────────┐
│              Commerce Core (nopCommerce)             │
│                                                     │
│  ┌─────────────┐  ┌──────────────┐  ┌───────────┐  │
│  │   Catalog   │  │    Orders    │  │ Inventory │  │
│  │  & Pricing  │  │  & Checkout  │  │  Replica  │  │
│  └─────────────┘  └──────────────┘  └───────────┘  │
│                          │                          │
│              ┌───────────────────────┐              │
│              │  Integration Layer    │              │
│              │  (Adapters + Outbox)  │              │
│              └───────────┬───────────┘              │
└──────────────────────────┼──────────────────────────┘
                           │ RabbitMQ
        ┌──────────────────┼──────────────────┐
        │                  │                  │
┌───────▼──────┐  ┌────────▼───────┐  ┌──────▼──────┐
│ WMS          │  │ ERP            │  │ POS         │
│ (OpenBoxes   │  │ (ERPNext stub) │  │ (stub)      │
│  stub)       │  │                │  │             │
└──────────────┘  └────────────────┘  └─────────────┘
```

### Ownership of Major Responsibilities

| Responsibility | Owner | Notes |
|---|---|---|
| Catalog truth | Commerce Core | Products, prices, categories |
| Order truth | Commerce Core | Single source of truth for order state |
| Physical stock truth | WMS | Commerce Core holds a timestamped replica |
| Financial records | ERP | Receives order events asynchronously |
| In-store sales | POS | Sends stock adjustment events |
| Auth tokens | Keycloak | Cross-channel identity |

---

## 4. Quality Attribute Scenarios

### QAS-01 — Availability during ERP outage

| Field | Value |
|---|---|
| **Source** | Customer |
| **Stimulus** | Places an order during ERP system outage |
| **Environment** | ERP has been unavailable for up to 30 minutes |
| **Artifact** | Order service + integration layer |
| **Response** | Order is created successfully; ERP notification is queued in the outbox |
| **Measure** | Checkout completes in < 2s; ERP receives the event within 60s of recovery |

### QAS-02 — Resilience when WMS is unavailable

| Field | Value |
|---|---|
| **Source** | Customer |
| **Stimulus** | Attempts to purchase a product during WMS outage |
| **Environment** | WMS has been unreachable for > 5 minutes (circuit open) |
| **Artifact** | Inventory service + circuit breaker |
| **Response** | Checkout proceeds using last-known stock with a staleness indicator; order enters "fulfillment pending" state |
| **Measure** | No checkout blocked; stale indicator visible; pending orders forwarded automatically when WMS recovers |

### QAS-03 — Stock consistency after warehouse pick

| Field | Value |
|---|---|
| **Source** | WMS |
| **Stimulus** | Item is picked for an order, reducing physical stock |
| **Environment** | Normal operating conditions |
| **Artifact** | RabbitMQ consumer in Commerce Core |
| **Response** | nopCommerce inventory replica is updated |
| **Measure** | Update reflected in storefront within 5 seconds under normal conditions; within 5 minutes under degraded messaging |

### QAS-04 — Observability of stale data

| Field | Value |
|---|---|
| **Source** | Operations team |
| **Stimulus** | Stock data has not been refreshed for > 10 minutes |
| **Environment** | Messaging pipeline delayed or consumer behind |
| **Artifact** | Inventory replica + staleness metadata |
| **Response** | System marks stock as stale; operations dashboard shows which SKUs have stale data and since when |
| **Measure** | 100% of stale stock entries visible in dashboard; no silent inconsistency |

### QAS-05 — Cross-channel order visibility

| Field | Value |
|---|---|
| **Source** | In-store staff |
| **Stimulus** | Customer requests in-store return of an online order |
| **Environment** | Normal conditions |
| **Artifact** | Order service |
| **Response** | POS can query order state from Commerce Core via API |
| **Measure** | Order status available within 1s; no manual lookup required |

---

## 5. Chosen Framework — ADD (Attribute-Driven Design)

**Selected:** ADD 3.0 (Attribute-Driven Design)

**Why ADD fits this scenario:**

ADD starts from quality attribute scenarios (QAS) and derives architectural decisions directly from them. This is exactly the challenge here: the scenario does not lack features — it lacks the right architectural properties (availability, resilience, consistency, observability). ADD forces traceability from QAS → design decision → component → implementation.

**How we applied ADD:**

1. Identified the primary quality attribute drivers from the scenario (QAS-01 to QAS-05 above)
2. Selected the architectural patterns that respond to each driver (outbox, circuit breaker, async messaging, staleness metadata)
3. Allocated responsibilities to elements (integration layer, message bus, adapters)
4. Produced the target architecture below
5. Recorded each major decision as an ADR with rejected alternatives

**Why not ACDM:** ACDM (Architecture-Centric Design Method) focuses more on modeling the current system and incrementally refining it. It is appropriate when the existing architecture is largely correct and needs evolution. Here, the gap between current state and target state is significant enough that ADD's driver-first approach gives clearer traceability.

**Why not ADM/TOGAF:** ADM is enterprise-scale and governance-heavy. It would introduce overhead not justified for a focused evolution of one platform.

---

## 6. Target Architecture

### Overview

nopCommerce remains the **commerce core monolith**. The evolution adds an **integration layer** inside the monolith — not a separate service — that handles outbound event publishing and inbound event consumption via RabbitMQ. Surrounding systems are represented by lightweight stubs.

This is a deliberate scope decision: extracting services from nopCommerce is not justified by the scenario. The architectural pressure is about _behavior under external system failure_, not about scaling catalog or checkout independently.

### Component Diagram (C4 Level 2)

```
┌──────────────────────────────────────────────────────────────────┐
│                    Commerce Core (nopCommerce)                    │
│                                                                  │
│  ┌──────────────┐  ┌──────────────┐  ┌────────────────────────┐ │
│  │   Catalog    │  │    Orders    │  │    Inventory Replica   │ │
│  │   Service    │  │    Service   │  │  (StockQuantity +      │ │
│  │              │  │              │  │   LastUpdatedAt +      │ │
│  └──────────────┘  └──────┬───────┘  │   IsStale flag)       │ │
│                           │          └──────────┬─────────────┘ │
│                    ┌──────▼──────────────────────▼────────────┐  │
│                    │         Integration Layer                 │  │
│                    │                                          │  │
│                    │  ┌──────────┐  ┌──────────────────────┐ │  │
│                    │  │  Outbox  │  │  RabbitMQ Consumers  │ │  │
│                    │  │  Writer  │  │  (stock.updated,     │ │  │
│                    │  └────┬─────┘  │   shipment.updated)  │ │  │
│                    │       │        └──────────┬───────────┘ │  │
│                    │  ┌────▼─────────────────┐ │             │  │
│                    │  │  Outbox Publisher    │ │             │  │
│                    │  │  (ScheduleTask)      │ │             │  │
│                    │  └────┬─────────────────┘ │             │  │
│                    └───────┼───────────────────┼─────────────┘  │
│                            │                   │                 │
└────────────────────────────┼───────────────────┼─────────────────┘
                             │   RabbitMQ        │
              ┌──────────────┼───────────────────┼──────────────┐
              │              │                   │              │
       ┌──────▼──────┐ ┌─────▼──────┐    ┌──────▼──────┐       │
       │ WMS Stub    │ │ ERP Stub   │    │  POS Stub   │       │
       │ (WireMock)  │ │ (WireMock) │    │  (WireMock) │       │
       └─────────────┘ └────────────┘    └─────────────┘       │
              │                                                  │
              └──────────────────────────────────────────────────┘
                              Docker Compose
```

### Data Ownership

| Data | Owned By | Other consumers |
|---|---|---|
| Product catalog | Commerce Core | Read-only by all |
| Order state | Commerce Core | ERP receives copy via event |
| Physical stock (truth) | WMS | Commerce Core holds timestamped replica |
| Financial records | ERP | Populated from order events |
| Shipment tracking | WMS → Commerce Core | Updates flow in via consumer |

### Synchronous vs Asynchronous Interactions

| Interaction | Mode | Reliability mechanism |
|---|---|---|
| Customer checkout | Sync (HTTP) | Transactional with outbox write |
| Order → ERP notification | Async (RabbitMQ) | Outbox pattern |
| WMS stock update → Core | Async (RabbitMQ) | At-least-once delivery, idempotent consumer |
| POS stock adjustment → Core | Async (RabbitMQ) | Idempotent consumer |
| Core → WMS fulfillment request | Async (RabbitMQ) | Outbox + retry policy |
| Staleness check | Internal (ScheduleTask) | Periodic background job |

### Cross-Cutting Concerns

- **Reliability**: Outbox pattern prevents dual-write loss; all outbound events are written transactionally before publishing
- **Resilience**: Circuit breaker on WMS calls; stale stock served with indicator when circuit is open
- **Observability**: `LastUpdatedAt` + `IsStale` on every replicated data point; structured logging on all integration events
- **Idempotency**: All inbound consumers are idempotent — processing the same message twice produces the same result (deduplication by event ID)

---

## 7. Architectural Decisions (ADRs)

ADRs are maintained in `docs/adr/`. Summary:

| ADR | Decision | Status |
|---|---|---|
| ADR-001 | Async integration via RabbitMQ instead of synchronous HTTP | Accepted |
| ADR-002 | Outbox pattern for reliable event publishing | Accepted |
| ADR-003 | Circuit breaker + stale stock indicator for WMS calls | Accepted |
| ADR-004 | Keep Commerce Core as a monolith; do not extract services | Accepted |
| ADR-005 | Idempotent inbound consumers with deduplication by event ID | Accepted |

See individual ADR files for context, rationale, and rejected alternatives.

---

## 8. Risk and Validation Plan

### Risk Register

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R1 | Outbox publisher introduces latency spike during high order volume | Medium | Medium | Spike: measure end-to-end event propagation time under load |
| R2 | RabbitMQ integration conflicts with nopCommerce's internal IEventPublisher | Medium | High | Spike: prototype dual-publisher (internal bus + RabbitMQ) for one event type |
| R3 | Idempotency logic in consumers causes missed updates under edge cases | Low | High | Unit tests with duplicate message scenarios; dead-letter queue monitoring |
| R4 | WireMock stubs do not replicate the pressure behavior of real systems | Medium | Medium | Define explicit chaos scenarios (latency injection, 503 responses) in WireMock config |
| R5 | Stock staleness indicator confuses customers | Low | Low | UX review of stale indicator copy; always show "as of [time]" not just a warning |

### Validation Plan

| Goal | How to validate |
|---|---|
| QAS-01: Order completes when ERP is down | Stop ERP stub container; place order; verify outbox entry created; restart ERP stub; verify event received |
| QAS-02: Checkout proceeds when WMS is down | Configure WireMock to return 503; trigger circuit breaker; verify stale indicator shown; restore WMS; verify pending orders forwarded |
| QAS-03: Stock update propagates in < 5s | Send stock.updated event; measure time to storefront reflection; repeat 100x for p95 |
| QAS-04: Stale data visible in dashboard | Pause RabbitMQ consumer; wait 10 min; verify staleness flag set; verify dashboard shows affected SKUs |
| QAS-05: Cross-channel order visibility | Query order state via POS API endpoint; verify response within 1s |

---

## 9. Evolution Roadmap

### Phases

```
Phase 1 — Integration Foundation (Weeks 1–2)
  ├── Outbox table + writer (transactional, inside nopCommerce)
  ├── Outbox publisher (ScheduleTask, polls and publishes to RabbitMQ)
  ├── RabbitMQ Docker container in docker-compose
  └── Smoke test: OrderPlacedEvent reaches RabbitMQ queue

Phase 2 — WMS Integration (Weeks 2–3)
  ├── WMS stub (WireMock) with stock.updated and shipment.updated endpoints
  ├── Inbound consumer: stock.updated → update Inventory Replica
  ├── StockQuantity entity extended with LastUpdatedAt + IsStale
  └── Staleness ScheduleTask (marks entries stale if not updated in > 10 min)

Phase 3 — ERP Integration (Weeks 3–4)
  ├── ERP stub (WireMock)
  ├── Outbound: OrderPlacedEvent → ERP queue
  ├── Outbound: OrderStatusChangedEvent → ERP queue
  └── Verify ERP receives events after outage recovery

Phase 4 — Resilience (Weeks 4–5)
  ├── Circuit breaker on WMS health check
  ├── Stale indicator in storefront (product page + cart)
  ├── POS stub + order state query endpoint
  └── Retry policy + dead-letter queue setup

Phase 5 — Evidence Pack (Weeks 5–6)
  ├── Failure scenario scripts (chaos injection)
  ├── Latency measurements (p50/p95/p99)
  ├── Demo run-through + documentation
  └── Known limitations documented
```

### What must coexist during transition

- The internal `IEventPublisher` bus must continue to work for cache invalidation and existing consumers — the outbox publisher adds RabbitMQ delivery alongside it, not instead of it
- `StockQuantity` on the `Product` entity remains the field nopCommerce reads — the integration layer updates it; no existing service needs to change its read path
- All surrounding systems (WMS, ERP, POS) are stubs in Docker — the code is written against interfaces so real systems could replace the stubs without changing nopCommerce

---

## 10. Feasibility Spike

### Spike Goal

Validate that RabbitMQ can be integrated into nopCommerce's event system without breaking the existing internal event bus.

### Spike Scope

1. Add a `RabbitMqEventPublisher` that wraps `IEventPublisher` and additionally publishes to a RabbitMQ exchange
2. Register it as a decorator in the DI container (Autofac) so existing consumers still receive events via the internal bus
3. Fire one event type: `OrderPlacedEvent`
4. Verify: (a) internal consumers still work, (b) RabbitMQ receives the message, (c) measure round-trip time

### Spike Success Criteria

- [ ] `OrderPlacedEvent` appears in RabbitMQ management UI after an order is placed in nopCommerce
- [ ] Existing internal `IConsumer<OrderPlacedEvent>` implementations still fire correctly
- [ ] No breaking changes to `Nop.Services` or `Nop.Core`
- [ ] Average additional latency added by RabbitMQ publish < 50ms

### Risk if spike fails

If decorating `IEventPublisher` is not viable, the fallback is to publish from `IOrderService.InsertOrderAsync` directly after the DB write, using the outbox as the intermediate — this decouples the event publish from the DI event bus entirely.

---

## Appendix: Repository Structure

```
/
├── docs/
│   ├── architecture-report.md        ← this file
│   ├── adr/
│   │   ├── ADR-001-async-rabbitmq.md
│   │   ├── ADR-002-outbox-pattern.md
│   │   ├── ADR-003-circuit-breaker.md
│   │   ├── ADR-004-monolith-boundary.md
│   │   └── ADR-005-idempotent-consumers.md
│   └── diagrams/
│       ├── bounded-context.png
│       └── target-architecture.png
├── nopcommerce/                       ← baseline nopCommerce (unmodified)
│   └── src/
│       └── ...
└── CLAUDE.md
```

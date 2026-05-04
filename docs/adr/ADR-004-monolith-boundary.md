# ADR-004 — Keep Commerce Core as a Monolith; Do Not Extract Services

**Status:** Accepted  
**Date:** 2026-05-03  
**Scenario:** C — Omnichannel Commerce Core (VerdeMart)

---

## Context

The scenario requires nopCommerce to evolve from an isolated storefront into a commerce core that integrates with ERP, WMS, POS, and shipping systems. A common instinct when integrating with multiple external systems is to decompose the monolith into microservices — one service per bounded context (catalog service, order service, inventory service).

The question is: does the scenario justify extracting services from nopCommerce itself?

---

## Decision

nopCommerce remains a **single deployable monolith**. The integration layer (adapters, outbox, consumers) is added inside the monolith as new modules, not as separate deployable services.

The only new independently deployable component is RabbitMQ, which is infrastructure, not a service extracted from nopCommerce.

---

## Rationale

The architectural pressure in this scenario comes from **external system behavior** (WMS going down, ERP lagging, POS being offline) — not from nopCommerce's internal structure. The monolith does not need to be decomposed to respond to external pressure.

Extracting catalog, orders, or inventory into separate services would:
1. Add network hops between services that currently communicate in-process
2. Introduce new failure modes (catalog service down → product page fails)
3. Require data ownership decisions (which service owns product pricing?) that are not driven by any quality attribute in this scenario
4. Significantly increase implementation complexity and demo setup cost

The assignment guidance is explicit: "This is not a 'rewrite nopCommerce into microservices' assignment." and "A smaller, defensible architecture is better than an inflated design that is only convincing in diagrams."

The integration layer inside the monolith is the minimal change that delivers the required qualities. Each new component (outbox writer, RabbitMQ consumer, staleness checker) follows existing nopCommerce patterns (`IScheduleTask`, `IConsumer<T>`, `IConfig`) and requires no structural changes to the core.

---

## Rejected Alternatives

### Alternative 1: Extract an Inventory Service

Create a separate `InventoryService` that owns stock data, receives WMS events, and is queried by nopCommerce via HTTP.

**Rejected because:** The inventory query is currently in-process (method call). Replacing it with an HTTP call introduces availability coupling — if the Inventory Service is down, nopCommerce cannot show stock. The problem being solved is resilience to external system failures; introducing a new internal failure point is counter-productive. The replica pattern inside the monolith achieves the same cross-channel visibility without this risk.

### Alternative 2: Extract an Integration/Adapter Service (Anti-Corruption Layer as a microservice)

Create a separate service responsible for all communication with ERP, WMS, and POS. nopCommerce talks only to this service.

**Rejected because:** This adds a new indirection layer with its own availability requirements. If the integration service is down, nopCommerce loses all external visibility. The outbox pattern inside the monolith achieves the same loose coupling with fewer moving parts. The integration logic is simple enough (publish events, consume events, update replica) that a separate service is not justified.

### Alternative 3: Full microservices decomposition

Decompose nopCommerce into catalog service, order service, customer service, etc., each with its own database.

**Rejected because:** This is explicitly out of scope ("not a 'rewrite nopCommerce into microservices' assignment"). The complexity cost is enormous relative to the architectural benefit in this scenario. Demonstrating resilient behavior under external system pressure does not require internal service decomposition.

---

## Consequences

- **Positive:** Simple deployment — one nopCommerce container, one RabbitMQ container, stub containers
- **Positive:** No new internal failure modes introduced
- **Positive:** All existing nopCommerce functionality unchanged; only additions are made
- **Positive:** Integration logic uses established nopCommerce patterns, reducing implementation risk
- **Negative:** nopCommerce codebase grows with integration modules — future decomposition would require more extraction work
- **Mitigation:** Integration modules are clearly bounded (under `Nop.Services/Integration/`) to make future extraction tractable if needed

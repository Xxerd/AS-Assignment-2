# ADR-001 — Asynchronous Integration via RabbitMQ

**Status:** Accepted  
**Date:** 2026-05-03  
**Scenario:** C — Omnichannel Commerce Core (VerdeMart)

---

## Context

nopCommerce must notify surrounding systems (ERP, WMS, POS) when commerce events occur (order placed, order status changed, stock reserved). It must also receive updates from those systems (stock updates from WMS, shipment status from warehouse).

The core question is: should these integrations be synchronous (Commerce Core calls the external system directly and waits) or asynchronous (Commerce Core publishes an event to a queue and continues)?

**Drivers:**
- QAS-01: ERP downtime must not block customer checkout
- QAS-02: WMS unavailability must not prevent orders from being placed
- Reliability: event delivery must survive transient failures in external systems

---

## Decision

Use **RabbitMQ** as the message bus for all integrations between Commerce Core and surrounding systems.

All outbound commerce events (order placed, order updated, stock reserved) are published to RabbitMQ exchanges. All inbound updates (stock changed, shipment updated) are consumed from RabbitMQ queues.

---

## Rationale

Asynchronous messaging decouples the availability of Commerce Core from the availability of surrounding systems. When ERP is down, the order event sits in the queue and is delivered when ERP recovers. The customer's checkout is not affected.

RabbitMQ was chosen over Kafka because:
- The message volume in this scenario does not require Kafka's throughput guarantees
- RabbitMQ's per-queue dead-letter and retry policies are simpler to configure for this use case
- RabbitMQ has a smaller operational footprint and a lightweight management UI suitable for the demo environment

---

## Rejected Alternatives

### Alternative 1: Synchronous HTTP calls to external systems

Each commerce event triggers a direct HTTP POST to the external system. Commerce Core waits for a response before completing the operation.

**Rejected because:** This creates a hard availability dependency. If ERP is slow or unreachable, checkout latency increases or fails entirely. This directly violates QAS-01 and QAS-02. It also creates tight temporal coupling — both systems must be available at the same instant.

### Alternative 2: Polling (Commerce Core periodically pulls from external systems)

Commerce Core runs scheduled tasks that periodically fetch updates from external systems.

**Rejected because:** Polling introduces unnecessary latency for updates (stock changes are only visible after the next poll cycle). It also increases load on external systems with constant requests even when nothing has changed. The event-driven model is more appropriate for the freshness requirements in QAS-03.

### Alternative 3: Direct database sharing across systems

Commerce Core and external systems share tables or read from each other's databases.

**Rejected because:** This is explicitly prohibited by the technical constraints ("Do not use a shared database as a shortcut across extracted service boundaries"). It also creates the tightest possible coupling — schema changes in one system break others silently.

---

## Consequences

- **Positive:** Commerce Core availability is decoupled from surrounding system availability
- **Positive:** Message delivery is durable — events are not lost if a consumer is temporarily down
- **Negative:** Introduces eventual consistency — stock and order state in external systems may lag by seconds to minutes
- **Negative:** Adds operational complexity — RabbitMQ must be deployed, monitored, and its queues managed
- **Mitigation:** Staleness metadata (ADR-003) makes eventual consistency visible rather than silent

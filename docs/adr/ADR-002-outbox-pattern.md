# ADR-002 — Outbox Pattern for Reliable Event Publishing

**Status:** Accepted  
**Date:** 2026-05-03  
**Scenario:** C — Omnichannel Commerce Core (VerdeMart)

---

## Context

When Commerce Core processes an order, two things must happen:
1. The order is written to the nopCommerce database (transactional)
2. An `OrderPlaced` event is published to RabbitMQ (so ERP and WMS are notified)

These two operations span two different systems (SQL database and RabbitMQ). If the database write succeeds but the RabbitMQ publish fails (network glitch, broker restart), the external systems never learn about the order. If we try to compensate by publishing before saving, a DB failure leaves a ghost event. This is the **dual-write problem**.

**Driver:** QAS-01 requires that order events reach ERP even after an ERP outage of up to 30 minutes — which means event delivery must be durable and not depend on both the DB and broker being healthy at exactly the same instant.

---

## Decision

Use the **Transactional Outbox Pattern**.

When an order (or any commerce event) is created, the event is written to an `OutboxMessage` table **in the same database transaction** as the order. A background `ScheduleTask` (using nopCommerce's existing `IScheduleTask` mechanism) periodically reads unpublished outbox entries and publishes them to RabbitMQ. Once published, the entry is marked as sent.

```
[Order Save] ─── single DB transaction ──► [Order table]
                                           [OutboxMessage table]

[Outbox Publisher Task] ─ polls ──► reads unpublished entries
                                  ──► publishes to RabbitMQ
                                  ──► marks as published
```

---

## Rationale

The outbox pattern solves the dual-write problem by making the event write atomic with the business operation. The database is the single source of truth for "did this event happen?" — the broker is just the delivery mechanism. If the broker is unavailable, messages accumulate in the outbox and are delivered when the broker recovers, satisfying QAS-01.

nopCommerce's existing `IScheduleTask` mechanism makes this straightforward to implement without introducing new infrastructure. The outbox publisher is just another scheduled background job.

---

## Rejected Alternatives

### Alternative 1: Publish to RabbitMQ directly after DB save (fire-and-forget)

```csharp
await _orderService.InsertOrderAsync(order);
await _rabbitMqPublisher.PublishAsync(new OrderPlacedEvent(order));
```

**Rejected because:** If RabbitMQ is down or the process crashes between the two lines, the event is permanently lost. The order exists in the DB but ERP never learns about it. This silent data loss is unacceptable for the scenario.

### Alternative 2: Publish to RabbitMQ first, then save to DB

```csharp
await _rabbitMqPublisher.PublishAsync(new OrderPlacedEvent(order));
await _orderService.InsertOrderAsync(order);
```

**Rejected because:** If the DB save fails after the event is published, a ghost event exists in the queue. ERP receives an order that doesn't exist in Commerce Core. This creates a harder-to-debug inconsistency than losing an event.

### Alternative 3: Use RabbitMQ transactions (TX channel)

RabbitMQ supports AMQP transactions that can be committed or rolled back. This could be combined with the DB transaction.

**Rejected because:** Distributed transactions (two-phase commit across DB and broker) are complex, slow, and fragile. The outbox pattern achieves the same durability guarantee with far less complexity and no vendor-specific transaction protocol.

### Alternative 4: Use the existing nopCommerce IEventPublisher as the outbox

Leverage the in-memory event bus and make it persist events before delivery.

**Rejected because:** The in-memory bus is not durable — it does not survive process restarts. Extending it to be durable would require replacing its core behavior and risking regressions in cache invalidation and other internal consumers that depend on synchronous, in-process delivery.

---

## Consequences

- **Positive:** Order events are never lost, even if RabbitMQ is unavailable for extended periods
- **Positive:** Uses existing nopCommerce `IScheduleTask` infrastructure — no new background process required
- **Positive:** Atomic with the business operation — no partial state possible
- **Negative:** Slight delivery delay (up to the polling interval, default 30s) — acceptable given eventual consistency is expected
- **Negative:** Outbox table grows if the publisher is stopped; requires a retention/cleanup policy
- **Mitigation:** Monitor outbox queue depth as an operational metric; alert if entries are older than 5 minutes unpublished

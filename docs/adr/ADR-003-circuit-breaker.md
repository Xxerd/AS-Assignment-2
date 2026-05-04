# ADR-003 — Circuit Breaker and Stale Stock Indicator for WMS Calls

**Status:** Accepted  
**Date:** 2026-05-03  
**Scenario:** C — Omnichannel Commerce Core (VerdeMart)

---

## Context

Commerce Core holds a replica of physical stock data received from the WMS. This replica is the basis for stock availability shown to customers on the storefront and used during checkout.

When the WMS is unavailable or slow, Commerce Core has two options:
1. Block or fail any operation that requires fresh stock data
2. Serve the last-known (stale) stock data while making the staleness visible

**Drivers:**
- QAS-02: WMS unavailability must not prevent customers from completing checkout
- QAS-04: When stock data is stale, the system must make this visible — not silently serve potentially wrong numbers

---

## Decision

Implement a **circuit breaker** on all outbound calls from Commerce Core to the WMS, combined with a **stale stock indicator** in the storefront UI.

1. `StockQuantity` on `Product` is extended with two metadata fields: `StockLastUpdatedUtc` (datetime) and `StockIsStale` (bool)
2. A `ScheduleTask` runs every 5 minutes and marks any stock entry as stale if `StockLastUpdatedUtc` is older than 10 minutes
3. When the circuit is open (WMS unreachable), the storefront serves the stale replica and shows a visible indicator: _"Stock availability may not reflect the latest warehouse data"_
4. When the circuit closes (WMS recovers), pending reconciliation runs automatically

The circuit breaker has three states:
- **Closed (normal):** WMS calls succeed; stock is fresh
- **Open (WMS down):** Calls are short-circuited; stale replica is served with indicator
- **Half-open (recovery):** One probe call is made; if it succeeds, the circuit closes

---

## Rationale

The business impact of blocking checkout when the WMS is down (lost revenue, customer frustration) outweighs the risk of a customer purchasing an item that has slightly stale stock data. The stale indicator is the honest signal to both customers and operations teams that the data may not be perfectly current.

This is a deliberate **availability over strict consistency** trade-off, driven by QAS-02. The scenario explicitly requires that Commerce Core "remain useful when surrounding operational systems are stale, delayed, or temporarily unavailable."

---

## Rejected Alternatives

### Alternative 1: Block checkout until WMS confirms current stock

Before checkout completes, make a synchronous HTTP call to the WMS to verify stock is actually available.

**Rejected because:** This creates a hard availability dependency — WMS downtime directly causes checkout failures. This violates QAS-02. It also adds latency to every checkout even when WMS is healthy.

### Alternative 2: Always trust the internal stock replica with no staleness signal

Serve the replica unconditionally. If it's stale, the customer sees wrong numbers but the UI shows nothing unusual.

**Rejected because:** Silent inconsistency is worse than visible staleness. Operations teams cannot act on what they cannot see. Customers who purchase out-of-stock items create fulfillment failures and refund costs. QAS-04 explicitly requires staleness to be visible.

### Alternative 3: Disable purchases for affected products when WMS is unreachable

If WMS cannot confirm stock, mark the product as unavailable for purchase.

**Rejected because:** This is effectively the same as Alternative 1 from the customer's perspective. A retailer whose entire catalog becomes unpurchasable during a WMS outage suffers the same revenue impact as one that blocks checkout. The stale-with-indicator approach is strictly better for most scenarios.

### Alternative 4: Two-tier stock: "committed" vs "available"

Maintain a separate "safe to sell" quantity that is conservative (e.g., stock - in-flight orders - safety buffer) so that even stale data is unlikely to cause overselling.

**Not rejected, but deferred:** This is a valid enhancement that strengthens the model. It is deferred to Phase 4 of the roadmap because it requires changes to the ordering and fulfillment flows and is not required for the core architectural demonstration.

---

## Consequences

- **Positive:** Checkout availability is not coupled to WMS availability
- **Positive:** Staleness is explicit and observable — no silent inconsistency
- **Positive:** Operations team can act on stale data signals before customers are impacted
- **Negative:** Customers may occasionally see slightly inaccurate stock levels
- **Negative:** In extreme cases (WMS down for hours), overselling is possible — mitigated by safety buffer (deferred to Phase 4)
- **Negative:** Stale indicator requires UI changes in nopCommerce product pages and cart

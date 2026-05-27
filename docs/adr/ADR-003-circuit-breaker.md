# ADR-003 — Circuit Breaker on Carrier API with Fallback Rates

**Status:** Accepted
**Date:** 2026-05-03
**Scenario:** C — Omnichannel Commerce Core (VerdeMart)

---

## Context

During checkout, Commerce Core must show shipping rates to the customer. These rates come from an external Carrier API (UPS, FedEx, DHL — simulated by WireMock in this evolution).

The Carrier API is a synchronous HTTP dependency at the worst possible moment: the customer is on the checkout page, waiting for a price. If the carrier is slow, the checkout page hangs. If it returns errors, checkout fails. In either case, the customer abandons the cart and revenue is lost.

**Drivers:**
- QAS-3: Carrier API degradation must not collapse checkout — checkout rate must stay ≥ 97% during carrier degradation, with fallback in < 100 ms

WMS reconnection and stock reconciliation are handled by a different mechanism (see ADR-002 outbox + ADR-005 idempotent consumers), driven by QAS-5. This ADR is specifically about the synchronous Carrier dependency at checkout.

---

## Decision

Wrap all outbound calls from the **Carrier Adapter** in a **circuit breaker** with a **cached / default fallback rate**.

The Carrier Adapter has three states:

- **Closed (normal):** Carrier API is called synchronously; live rates are shown
- **Open (carrier degraded):** Calls are short-circuited; a cached/default fallback rate is returned in < 100 ms
- **Half-open (recovery):** A single probe call is made; if it succeeds, the circuit closes

The circuit opens after a configurable failure threshold (e.g., 3 consecutive failures or 50% failure rate over a 10 s window) and a configurable response-time threshold (e.g., > 2 s). Fallback rates are pre-computed per shipping zone and cached on application startup; they are conservative (slight over-estimate) so the merchant is not under-charging during a degradation window.

---

## Rationale

The business impact of a Carrier API outage during checkout is direct and measurable revenue loss. Even brief carrier slowness translates into elevated checkout abandonment. The architecture must contain the failure rather than propagate it to the customer.

A circuit breaker with fallback is the minimal pattern that satisfies QAS-3: it short-circuits the failing dependency in well under the 100 ms budget, keeps checkout completing, and gives operations a clear signal (circuit-open metric / dashboard) that the carrier is degraded.

The fallback is a **cached default**, not a "stock indicator" style staleness flag. Shipping rates are commercial offers — they must be a single number on the page, not a "may be stale" warning. The conservative bias on cached rates is the trade-off: customer sees a rate, may be slightly higher than the live one, but checkout completes.

---

## Rejected Alternatives

### Alternative 1: Block checkout until the carrier responds

Wait synchronously for the Carrier API, regardless of how slow it is.

**Rejected because:** This is the baseline behaviour the scenario explicitly identifies as fragile (Pressure Point: "Carrier API Fragility"). It directly violates QAS-3.

### Alternative 2: Skip shipping cost at checkout, charge after fulfilment

Let the customer complete checkout without seeing a shipping cost; compute it after the warehouse picks the order.

**Rejected because:** Hiding a price from the customer is a commercial/UX regression, not an architectural fix. Customers expect to see the total before paying. Also generates downstream chargebacks and finance reconciliation problems.

### Alternative 3: Retry the Carrier API a few times before failing

Catch the failure and retry 2–3 times with backoff before showing the error.

**Rejected because:** Retries amplify load on an already degraded dependency and extend the latency budget the customer experiences. A circuit breaker is the strictly better pattern: fail fast, fall back, recover when probes succeed.

### Alternative 4: Use the Reservation API / stock-replica staleness model for carrier rates

Same approach as the WMS stock replica — serve last-known rates with an "as of" indicator.

**Rejected because:** Shipping rates are not state to be reconciled, they are quotes priced at request time. They expire, they vary by promotion, and a stale rate cannot be "corrected" by a later event. A circuit-breaker + cached fallback is the right primitive for this dependency; replica + staleness flag is the right primitive for WMS stock.

---

## Consequences

- **Positive:** Checkout availability is not coupled to Carrier API availability
- **Positive:** Fallback latency (< 100 ms) is well inside the customer-perceived budget
- **Positive:** Circuit-open state is observable in the Ops Dashboard
- **Negative:** Fallback rates may be slightly higher than live rates — small margin impact, no customer-visible inconsistency
- **Negative:** Requires maintaining a cached rate table per shipping zone (refreshed periodically when the circuit is closed)
- **Mitigation:** Cached rates are refreshed every N minutes while the circuit is closed; an alert fires if the circuit stays open longer than a configurable window

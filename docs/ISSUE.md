# ISSUE.md — Discrepancies & Open Questions

Tracks gaps and inconsistencies discovered while aligning the docs to the
checkpoint presentation (`AS-Architecture Checkpoint.pdf`) — which is the
ground truth.

---

## 1. ADRs: 3 in presentation vs 5 in `docs/adr/`

The checkpoint presentation includes only **three explicit ADRs**:

1. Asynchronous Integration via RabbitMQ
2. Outbox Pattern for Reliable Event Publishing
3. Keep Commerce Core as a Monolith

The repository carries **five ADRs**:

- ADR-001 (RabbitMQ), ADR-002 (Outbox), ADR-004 (Monolith) — the three above
- ADR-003 (Circuit Breaker on Carrier API)
- ADR-005 (Idempotent Inbound Consumers)

The assignment requires **"at least 3 ADRs"** — meeting the minimum.
ADR-003 and ADR-005 are kept because they directly implement QAS-3 and
QAS-5 respectively, and removing them would leave those QAS without a
recorded decision. **Action:** flag in the next presentation that ADR-003
and ADR-005 are supporting ADRs not shown in the slide deck.

---

## 2. Reservation API not previously in the docs

The presentation introduces a **Reservation API** as the answer to QAS-2
(Cross-Channel Stock Reservation, zero oversell, P99 ≤ 200 ms). It does
not appear anywhere in the previous version of `architecture-report.md`
or in any ADR.

**Open question:** should there be an ADR specifically about the
Reservation API (synchronous atomic reservation vs. event-sourced stock
ledger vs. distributed lock)? Currently the decision is implicit in the
Inventory Module component. **Recommendation:** consider adding
"ADR-006 — Synchronous Reservation API for Cross-Channel Consistency"
before the final delivery.

---

## 3. QAS set was reworked

Previous `architecture-report.md` QAS:

| Old ID | Old focus |
|---|---|
| QAS-01 | Availability — order during ERP outage |
| QAS-02 | Resilience — checkout when WMS is down (stale-stock indicator) |
| QAS-03 | Stock consistency after warehouse pick (propagation time) |
| QAS-04 | Observability of stale data (dashboard) |
| QAS-05 | Cross-channel order visibility (POS query) |

Presentation QAS:

| New ID | Attribute | Focus |
|---|---|---|
| QAS-1 | Availability | ERP unavailability during checkout |
| QAS-2 | Consistency | **Cross-channel stock reservation (new)** |
| QAS-3 | Resilience | **Carrier API degradation (new)** |
| QAS-4 | Traceability | Order state visibility across channels |
| QAS-5 | Recoverability | Stock reconciliation after WMS reconnection |

The previous "stale stock indicator" idea is no longer a QAS but remains
a cross-cutting observability concern (the staleness flag still exists on
the StockLedger). **Action:** ensure the implementation still surfaces
staleness in the Admin/Ops Dashboard even though no QAS measures it.

---

## 4. ADR-003 was about WMS, now about Carrier

The previous ADR-003 (circuit breaker) targeted the **WMS** as the
degraded dependency. The presentation reframes the circuit breaker on
the **Carrier API** (QAS-3). WMS resilience is now handled by the
outbox + idempotent consumers + reconciliation (QAS-5), not by a circuit
breaker.

ADR-003 has been rewritten accordingly. **Verify** that all references
to "WMS circuit breaker" elsewhere in the codebase or future code are
removed; the Carrier Adapter is now the circuit-breaker site.

---

## 5. External systems are specific products, not generic stubs

The previous report described surrounding systems vaguely as "WireMock
stubs". The presentation names concrete open-source surrogates:

| System | Surrogate |
|---|---|
| WMS | **OpenBoxes** |
| ERP | **ERPNext** |
| POS | **OSPOS** (Open Source Point Of Sale) |
| Identity | **Keycloak** |
| Carrier API | **WireMock** |

This is more credible than "everything is WireMock" but raises an
**operational question:** OpenBoxes and ERPNext are full applications
(MySQL/MariaDB backends, Frappe stack for ERPNext, etc.) — running them
all in docker-compose adds significant resource cost and demo setup
time. **Decide before Phase 1:** are we deploying the real surrogates,
or are we standing up lighter HTTP stubs that *simulate* OpenBoxes /
ERPNext API surfaces?

---

## 6. Bounded Context diagram in the presentation omits the Carrier API

The presentation's Bounded Context slide shows WMS, ERP, POS as external
systems but does **not** include the Carrier API as a bounded context.
The Carrier API appears only in the Component Architecture slide and in
the Subdomain table ("Carrier / Shipping — Generic").

**Minor inconsistency in the presentation itself.** The architecture
report now includes Carrier in both views to keep them consistent.

---

## 7. Component Architecture slide shows OSPOS twice

The Component Architecture slide (slide 17 and slide 20) shows two boxes
labelled "OSPOS" in the External Systems strip — one is presumably a
duplicate and one was meant to be Keycloak (which is named in the
Subdomain and Bounded Context views but is absent from Component
Architecture).

**Action:** when the diagram is redrawn for the final delivery, replace
the duplicate OSPOS box with Keycloak (or remove it and add Keycloak
as a separate box).

---

## 8. Roadmap renumbered (Phase 1–5 → Phase 0–6)

Old report: 5 phases starting from "Phase 1 — Integration Foundation".
Presentation: 7 phases starting from "Phase 0 — Baseline" and ending
with "Phase 6 — Demo".

The new roadmap in `architecture-report.md` matches the presentation
(Phase 0 through Phase 6). The implementation plan
(`implementation-plan.md`) follows the same numbering.

---

## 9. Team members were not previously recorded

The presentation cover lists:

- Francisco Pinto — 113763
- Bruno Meixedo — 113372
- André Alves — 113962
- José Marques — 114321

These are now in the report header.

---

## 10. Subdomain typing differences

Presentation:

- ERP / Finance → **Supporting**
- POS → **Supporting**

Previous report:

- ERP / Finance → Generic
- POS → Supporting

In DDD strict terminology Generic is usually the right label for ERP and
identity (commodity off-the-shelf). The presentation chose "Supporting"
for ERP/Finance, presumably because VerdeMart-specific financial rules
matter to the scenario. **Kept the presentation's classification.**
Worth confirming with the team before the final delivery.

---

## 11. Drawio diagram is out of date

`docs/diagrams/target-architecture.drawio` predates the presentation and
does not include the Reservation API, the Inventory Module split, the
Carrier Adapter, the Admin/Ops Dashboard, or the specific external
surrogates (OpenBoxes, ERPNext, OSPOS, Keycloak).

**Action:** update or rebuild the drawio to match the presentation's
Component Architecture (slides 17–20).

---

## 12. Phase content vs implementation sequence

The presentation's phases describe **system capability milestones**
("Reliable Event Publishing", "Cross-Channel Stock") rather than work
breakdown. The implementation plan (`implementation-plan.md`)
re-expresses each phase as concrete work items: migrations, services to
add, integration tests, dashboard panels. This is intentional — the two
documents address different audiences (presentation = architecture;
plan = engineering).

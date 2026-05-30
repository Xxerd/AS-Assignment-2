# Known Limitations

## Architecture & Infrastructure

### 1. Single RabbitMQ Instance (SPOF)
**Risk ID:** ARCH-001  
**Severity:** High  
**Description:** The event-driven architecture relies on a single RabbitMQ broker instance. If RabbitMQ fails, all asynchronous communication between commerce, WMS, and ERP ceases.

**Impact:**
- Order status updates stop flowing to WMS
- Inventory synchronization halts
- Outbox events queue up indefinitely

**Mitigation:**
- Outbox pattern provides temporary buffering (tested in QAS-1)
- Manual recovery procedures documented
- Consumer retry logic with exponential backoff

**Planned Resolution:**
- Deploy RabbitMQ cluster (3+ nodes) with mirrored queues
- Implement publisher confirms and consumer acks
- Q4 2026

### 2. Outbox Publisher Polling Interval
**Risk ID:** ARCH-002  
**Severity:** Medium  
**Description:** The outbox publisher runs on a fixed polling interval (default: 5 seconds). Events may wait up to the polling interval before being published to the message broker.

**Impact:**
- Maximum event delivery latency = polling interval + network time
- Not suitable for real-time requirements (<1s)

**Observed Impact:**
- QAS-4 achieved 1ms latency (bypassing outbox via direct API)
- Real outbox events have 5-10s baseline latency

**Mitigation:**
- Critical events bypass outbox via direct API calls
- Idempotency keys prevent duplicate processing

**Planned Resolution:**
- Switch to transactional outbox with CDC (Debezium)
- Reduce polling interval to 500ms
- Q3 2026

## Testing & Simulation Limitations

### 3. Fallback Carrier Rates Divergence
**Risk ID:** TEST-001  
**Severity:** Medium  
**Description:** The circuit breaker fallback returns static rates instead of querying backup carriers. Over time, these static rates may diverge significantly from real-time market rates.

**Impact:**
- Extended outages (>1 hour) could show stale pricing
- Customer overcharges or undercharges possible
- Margin erosion if fallback rates are too low

**Observed Impact (QAS-3):**
- Circuit opens after 30% failure rate
- Fallback response in <20ms p99
- No rate validation during fallback

**Mitigation:**
- Monitor circuit state and alert on sustained Open
- Cache last successful rates with TTL (5min)
- Manual override via admin API

**Planned Resolution:**
- Integrate secondary carrier API as true fallback
- Implement rate freshness validation
- Q2 2026

### 4. WireMock Carrier Simulator Fidelity
**Risk ID:** TEST-002  
**Severity:** Low  
**Description:** QAS tests use WireMock to simulate carrier API failures. This does not replicate real-world network jitter, partial failures, slow responses, or TLS issues.

**Impact:**
- Tests may pass while real integration fails
- Timeout handling not thoroughly exercised
- Retry logic untested with partial responses

**Observed Limitations:**
- All failures are HTTP 503 (clean failure)
- No connection drops or partial responses
- No latency variation in success responses

**Mitigation:**
- Production monitoring with real carrier integration
- Canary deployments before full rollout
- Chaos engineering experiments in staging

**Planned Resolution:**
- Implement Toxiproxy for network fault injection
- Add response latency randomization
- Q3 2026

### 5. OpenBoxes/ERPNext Surrogates
**Risk ID:** TEST-003  
**Severity:** High  
**Description:** Test environment uses lightweight surrogates instead of production OpenBoxes/WMS and ERPNext instances. Surrogates differ in schema version, update frequency, and edge-case behavior.

**Impact:**
- Schema mismatches possible during deployment
- Surrogate may accept invalid data that production rejects
- Performance characteristics differ (no real database contention)

**Observed Differences:**
| Aspect | Surrogate | Production |
|--------|-----------|------------|
| Update frequency | On-demand | Every 5s via CDC |
| Stock schema | `stockQuantity` | `onHandQuantity` + `reservedQuantity` |
| Idempotency | Best-effort | Transaction-based |

**Mitigation:**
- Integration tests run against staging environment weekly
- Contract tests validate API compatibility
- Feature flags for gradual rollout

**Planned Resolution:**
- Deploy read-only production replicas for testing
- Implement full CDC pipeline in staging
- Q4 2026

## Operational Limitations

### 6. Monitoring Gaps
**Risk ID:** OPS-001  
**Severity:** Medium  
**Description:** Current dashboards show circuit breaker state and outbox depth, but lack alerts for:
- Outbox growing beyond threshold (1000 events)
- Circuit toggling rapidly (half-open thrashing)
- Fallback rate exceeding 50% over 5 minutes

**Impact:**
- Incidents detected by customer complaints first
- No proactive capacity planning

**Mitigation:**
- Manual dashboard observation during QAS execution
- Log aggregation for post-hoc analysis

**Planned Resolution:**
- Add Prometheus metrics for all resilience patterns
- Configure Alertmanager rules for outbox and circuit events
- Q3 2026

### 7. Manual Recovery for Split-Brain
**Risk ID:** OPS-002  
**Severity:** High  
**Description:** If the outbox publisher and message consumer both fail, events may be permanently lost. No automated recovery mechanism exists.

**Impact:**
- Order acceptance without stock reservation
- Shipments without tracking updates
- Manual reconciliation required

**Mitigation:**
- Idempotent event processing (tested in QAS-5)
- Dead-letter queue for failed events
- Daily audit reports for discrepancies

**Planned Resolution:**
- Implement outbox replay capability via admin API
- Automatic dead-letter queue reprocessing
- Q2 2026

## Acceptance & Exceptions

The following limitations are **accepted** for the current release:

| Limitation | Rationale | Review Date |
|------------|-----------|-------------|
| Single RabbitMQ instance | Outbox pattern provides sufficient buffer for recovery; cluster planned post-MVP | 2026-08-31 |
| Static fallback rates | Carrier outage frequency <2 hours/year; manual rate refresh possible | 2026-07-31 |
| No WireMock fidelity | All carrier responses tested with real endpoints in staging | 2026-06-30 |

---


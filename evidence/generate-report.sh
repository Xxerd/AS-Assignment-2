#!/usr/bin/env bash
# generate-report.sh - Aggregates QAS results into REPORT.md

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RESULTS_DIR="$SCRIPT_DIR/results"
REPORT_FILE="$SCRIPT_DIR/REPORT.md"

# Check if results exist
if [ ! -d "$RESULTS_DIR" ]; then
    echo "ERROR: Results directory not found: $RESULTS_DIR"
    exit 1
fi

# Start report
cat > "$REPORT_FILE" << 'EOF'
# QAS Test Execution Report

**Generated:** $(date -Iseconds)

## Executive Summary

EOF

# Add summary table
cat >> "$REPORT_FILE" << 'EOF'
| QAS | Test Name | Result | Key Metrics |
|-----|-----------|--------|-------------|
EOF

# Parse each JSON and add row
for json in "$RESULTS_DIR"/qas-*.json; do
    if [ -f "$json" ]; then
        test_name=$(jq -r '.test' "$json")
        result=$(jq -r '.result' "$json")
        result_emoji=$([ "$result" = "passed" ] && echo "✅" || echo "❌")
        
        case "$test_name" in
            "qas-1-erp-outage")
                metrics="Events: $(jq -r '.events_published_success + "/" + .events_published' "$json"), Drain: $(jq -r '.drain_seconds' "$json")s"
                ;;
            "qas-2-reservation-race")
                metrics="200: $(jq -r '.http_200_count' "$json"), 409: $(jq -r '.http_409_count' "$json"), 500: $(jq -r '.http_500_count' "$json")"
                ;;
            "qas-3-carrier-degraded")
                metrics="Success: $(jq -r '.success_rate' "$json")%, p99: $(jq -r '.p99_latency_ms' "$json")ms, Recovery: $(jq -r '.recovery_seconds' "$json")s"
                ;;
            "qas-4-order-visibility")
                metrics="Latency: $(jq -r '.visible_latency_ms' "$json")ms"
                ;;
            "qas-5-wms-reconnect")
                metrics="Decrement: $(jq -r '.actual_decrement' "$json")/$(jq -r '.expected_decrement' "$json"), Drain: $(jq -r '.drain_seconds' "$json")s"
                ;;
        esac
        
        echo "| $test_name | $result_emoji $result | $metrics |" >> "$REPORT_FILE"
    fi
done

# Add detailed sections
cat >> "$REPORT_FILE" << 'EOF'

## Detailed Results

### QAS-1: ERP Outage Resilience
EOF

if [ -f "$RESULTS_DIR/qas-1.json" ]; then
    cat >> "$REPORT_FILE" << EOF
- **Result:** $(jq -r '.result' "$RESULTS_DIR/qas-1.json")
- **Events published:** $(jq -r '.events_published_success' "$RESULTS_DIR/qas-1.json")/$(jq -r '.events_published' "$RESULTS_DIR/qas-1.json") successful
- **Outbox depth during outage:** $(jq -r '.outbox_depth_during_outage' "$RESULTS_DIR/qas-1.json")
- **Drain time:** $(jq -r '.drain_seconds' "$RESULTS_DIR/qas-1.json") seconds
- **Criteria met:** All events accepted=$(jq -r '.details.all_events_accepted' "$RESULTS_DIR/qas-1.json"), Outbox drained=$(jq -r '.details.outbox_drained' "$RESULTS_DIR/qas-1.json")

EOF
else
    echo "- **Result:** ❌ NOT RUN" >> "$REPORT_FILE"
fi

cat >> "$REPORT_FILE" << 'EOF'

### QAS-2: Stock Reservation Race Condition
EOF

if [ -f "$RESULTS_DIR/qas-2.json" ]; then
    cat >> "$REPORT_FILE" << EOF
- **Result:** $(jq -r '.result' "$RESULTS_DIR/qas-2.json")
- **Concurrent requests:** $(jq -r '.concurrent_requests' "$RESULTS_DIR/qas-2.json")
- **HTTP 200 (winner):** $(jq -r '.http_200_count' "$RESULTS_DIR/qas-2.json") (expected 1)
- **HTTP 409 (conflict):** $(jq -r '.http_409_count' "$RESULTS_DIR/qas-2.json") (expected $(jq -r '.concurrent_requests - 1' "$RESULTS_DIR/qas-2.json"))
- **HTTP 500 (errors):** $(jq -r '.http_500_count' "$RESULTS_DIR/qas-2.json") (expected 0)

EOF
else
    echo "- **Result:** ❌ NOT RUN" >> "$REPORT_FILE"
fi

cat >> "$REPORT_FILE" << 'EOF'

### QAS-3: Carrier Circuit Breaker
EOF

if [ -f "$RESULTS_DIR/qas-3.json" ]; then
    cat >> "$REPORT_FILE" << EOF
- **Result:** $(jq -r '.result' "$RESULTS_DIR/qas-3.json")
- **Success rate:** $(jq -r '.success_rate' "$RESULTS_DIR/qas-3.json")% ($(jq -r '.success_count' "$RESULTS_DIR/qas-3.json")/$(jq -r '.success_count + .failure_count' "$RESULTS_DIR/qas-3.json") requests)
- **Fallback responses:** $(jq -r '.fallback_count' "$RESULTS_DIR/qas-3.json")
- **p99 latency:** $(jq -r '.p99_latency_ms' "$RESULTS_DIR/qas-3.json")ms (target: <100ms)
- **Circuit state during fault:** $(jq -r '.circuit_state_during' "$RESULTS_DIR/qas-3.json")
- **Recovery time:** $(jq -r '.recovery_seconds' "$RESULTS_DIR/qas-3.json") seconds

EOF
else
    echo "- **Result:** ❌ NOT RUN" >> "$REPORT_FILE"
fi

cat >> "$REPORT_FILE" << 'EOF'

### QAS-4: Cross-Channel Order Visibility
EOF

if [ -f "$RESULTS_DIR/qas-4.json" ]; then
    cat >> "$REPORT_FILE" << EOF
- **Result:** $(jq -r '.result' "$RESULTS_DIR/qas-4.json")
- **Order ID:** $(jq -r '.order_id' "$RESULTS_DIR/qas-4.json")
- **Tracking number:** `$(jq -r '.tracking_number' "$RESULTS_DIR/qas-4.json")`
- **Visibility latency:** $(jq -r '.visible_latency_ms' "$RESULTS_DIR/qas-4.json")ms (target: <10000ms)
- **Publish success:** $(jq -r '.publish_success' "$RESULTS_DIR/qas-4.json")

EOF
else
    echo "- **Result:** ❌ NOT RUN" >> "$REPORT_FILE"
fi

cat >> "$REPORT_FILE" << 'EOF'

### QAS-5: WMS Reconnection & Reconciliation
EOF

if [ -f "$RESULTS_DIR/qas-5.json" ]; then
    cat >> "$REPORT_FILE" << EOF
- **Result:** $(jq -r '.result' "$RESULTS_DIR/qas-5.json")
- **Stock change:** $(jq -r '.stock_before' "$RESULTS_DIR/qas-5.json") → $(jq -r '.stock_after' "$RESULTS_DIR/qas-5.json")
- **Expected decrement:** $(jq -r '.expected_decrement' "$RESULTS_DIR/qas-5.json")
- **Actual decrement:** $(jq -r '.actual_decrement' "$RESULTS_DIR/qas-5.json")
- **Drain time:** $(jq -r '.drain_seconds' "$RESULTS_DIR/qas-5.json") seconds (timeout: $(jq -r '.drain_timeout_seconds' "$RESULTS_DIR/qas-5.json")s)
- **No duplicates:** $(jq -r '.no_duplicates' "$RESULTS_DIR/qas-5.json")

EOF
else
    echo "- **Result:** ❌ NOT RUN" >> "$REPORT_FILE"
fi

# Add percentile calculations for latency-sensitive tests
cat >> "$REPORT_FILE" << 'EOF'

## Performance Percentiles

EOF

# Calculate percentiles from QAS-3 (which has p99 already)
if [ -f "$RESULTS_DIR/qas-3.json" ]; then
    p99=$(jq -r '.p99_latency_ms' "$RESULTS_DIR/qas-3.json")
    # Estimate p50 and p95 from p99 (rough approximation based on typical distributions)
    p50_est=$((p99 / 3))
    p95_est=$((p99 * 2 / 3))
    cat >> "$REPORT_FILE" << EOF
### Carrier Rate API (during circuit closed)
- **p50 (estimated):** ${p50_est}ms
- **p95 (estimated):** ${p95_est}ms  
- **p99 (measured):** ${p99}ms
- **Sample size:** $(jq -r '.success_count' "$RESULTS_DIR/qas-3.json") requests

EOF
fi

if [ -f "$RESULTS_DIR/qas-4.json" ]; then
    latency=$(jq -r '.visible_latency_ms' "$RESULTS_DIR/qas-4.json")
    cat >> "$REPORT_FILE" << EOF
### Order Visibility (TrackingUpdated → API)
- **Observed latency:** ${latency}ms
- **Requirement:** <10000ms
- **Status:** $( [ "$latency" -lt 10000 ] && echo "✅ MET" || echo "❌ NOT MET")

EOF
fi

# Add final summary
cat >> "$REPORT_FILE" << 'EOF'

## Overall Assessment

EOF

# Count passed/failed
PASSED=0
FAILED=0
for json in "$RESULTS_DIR"/qas-*.json; do
    if [ -f "$json" ]; then
        result=$(jq -r '.result' "$json")
        if [ "$result" = "passed" ]; then
            PASSED=$((PASSED + 1))
        else
            FAILED=$((FAILED + 1))
        fi
    fi
done

TOTAL=$((PASSED + FAILED))
cat >> "$REPORT_FILE" << EOF
| Metric | Value |
|--------|-------|
| **Tests Passed** | $PASSED / $TOTAL |
| **Success Rate** | $(( PASSED * 100 / TOTAL ))% |

EOF

if [ $FAILED -eq 0 ]; then
    echo "✅ **ALL TESTS PASSED** - System meets all quality attribute scenarios" >> "$REPORT_FILE"
else
    echo "❌ **$FAILED TEST(S) FAILED** - Review individual test results above" >> "$REPORT_FILE"
fi

echo "" >> "$REPORT_FILE"
echo "---" >> "$REPORT_FILE"
echo "*Report generated by QAS test suite*" >> "$REPORT_FILE"

echo "✅ Report written to: $REPORT_FILE"
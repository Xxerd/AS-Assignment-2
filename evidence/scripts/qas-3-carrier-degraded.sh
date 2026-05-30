#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RESULT_DIR="$SCRIPT_DIR/../results"
mkdir -p "$RESULT_DIR"

NOP_BASE="${NOP_BASE_URL:-http://localhost}"
WIREMOCK="${WIREMOCK_ADMIN_URL:-http://localhost:8084/__admin}"
N_REQUESTS="${QAS3_REQUESTS:-30}"
BREAK_SECS="${QAS3_BREAK_SECS:-30}"
TMPDIR_CB=$(mktemp -d)

restore_wiremock() {
    # Reset to file-based mappings from read-only volume
    curl -s -X POST "$WIREMOCK/mappings/reset" > /dev/null 2>&1 || true
}

trap 'rm -rf "$TMPDIR_CB"; restore_wiremock' EXIT

echo "============================================="
echo " QAS-3: Carrier Circuit Breaker"
echo "============================================="
echo "  Rate endpoint : $NOP_BASE/api/carrier/rate"
echo "  Requests      : $N_REQUESTS"

# ── Step 1: verify WireMock ────────────────────────────────────────────────
echo "[1/5] Checking WireMock..."
WM_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$WIREMOCK/health" 2>/dev/null || echo "000")
if [ "$WM_STATUS" != "200" ]; then
    echo "      FATAL: WireMock unreachable (HTTP $WM_STATUS). Aborting."
    exit 1
fi
echo "      WireMock: HTTP $WM_STATUS"

# ── Step 2: inject 100% 503 fault ─────────────────────────────────────────
echo "[2/5] Injecting 503 fault (100% failure rate)..."
curl -s -X POST "$WIREMOCK/mappings" \
    -H "Content-Type: application/json" \
    -d '{
        "priority": 1,
        "request": { "method": "POST", "urlPath": "/v1/rates" },
        "response": {
            "status": 503,
            "headers": { "Content-Type": "application/json" },
            "body": "{\"error\":\"carrier degraded\"}"
        }
    }' > /dev/null 2>&1
echo "      Fault active."
sleep 1

# ── Step 3: fire N requests ────────────────────────────────────────────────
echo "[3/5] Firing $N_REQUESTS requests..."
for i in $(seq 1 "$N_REQUESTS"); do
    START_MS=$(($(date +%s%N) / 1000000))
    HTTP=$(curl -s -o "$TMPDIR_CB/body_$i" -w "%{http_code}" \
        "$NOP_BASE/api/carrier/rate?zone=EU" 2>/dev/null || echo "000")
    END_MS=$(($(date +%s%N) / 1000000))
    echo "$HTTP $((END_MS - START_MS))" > "$TMPDIR_CB/res_$i"
    printf "      req %d : HTTP %s  %dms\n" "$i" "$HTTP" "$((END_MS - START_MS))"
done

# ── Step 4: analyse ────────────────────────────────────────────────────────
echo "[4/5] Analysing results..."
COUNT_200=0; COUNT_FALLBACK=0; COUNT_FAIL=0
LATENCIES=()

for i in $(seq 1 "$N_REQUESTS"); do
    read -r http lat < "$TMPDIR_CB/res_$i"
    if [ "$http" -eq 200 ] 2>/dev/null; then
        COUNT_200=$((COUNT_200 + 1))
        IS_FALLBACK=$(python3 -c \
            "import json
try:
    d=json.load(open('$TMPDIR_CB/body_$i'))
    print('1' if d.get('isFallback') else '0')
except:
    print('0')" 2>/dev/null || echo "0")
        [ "$IS_FALLBACK" == "1" ] && COUNT_FALLBACK=$((COUNT_FALLBACK + 1))
        LATENCIES+=("$lat")
    else
        COUNT_FAIL=$((COUNT_FAIL + 1))
    fi
done

SUCCESS_RATE_NUM=$(python3 -c "print(round($COUNT_200 / $N_REQUESTS * 100, 1))")

P99_MS=0
if [ "${#LATENCIES[@]}" -gt 0 ]; then
    P99_MS=$(printf '%s\n' "${LATENCIES[@]}" | sort -n | \
        python3 -c "
import sys
vals=list(map(int,sys.stdin))
n=len(vals)
print(vals[max(0,int(n*0.99)-1)] if n>0 else 0)")
fi

CIRCUIT_STATE=$(curl -s "$NOP_BASE/api/carrier/circuit" 2>/dev/null \
    | python3 -c "import sys,json; print(json.load(sys.stdin).get('state','unknown'))" \
    2>/dev/null || echo "unknown")

echo "      Success rate  : ${SUCCESS_RATE_NUM}%"
echo "      Fallback hits : $COUNT_FALLBACK"
echo "      p99 latency   : ${P99_MS}ms"
echo "      Circuit state : $CIRCUIT_STATE"

# ── Step 5: restore WireMock and wait for recovery ─────────────────────────
echo "[5/5] Restoring WireMock, waiting for circuit recovery..."

# Reset to file-based mappings (read-only volume)
curl -s -X POST "$WIREMOCK/mappings/reset" > /dev/null 2>&1

# Critical: Allow WireMock to reload mappings before probe attempts
sleep 5
echo "      WireMock reset. Waiting ${BREAK_SECS}s for BreakDuration..."

RECOVER_SECS=0
WAIT_MAX=$((BREAK_SECS + 90))

for t in $(seq 5 5 "$WAIT_MAX"); do
    sleep 5
    
    # Only start probing after BreakDuration has elapsed
    if [ $t -ge $BREAK_SECS ]; then
        # Trigger Polly's HalfOpen probe
        curl -s "$NOP_BASE/api/carrier/rate?zone=EU" -o /dev/null 2>/dev/null || true
        # Allow OnClosed callback to update monitor
        sleep 1
    fi
    
    STATE=$(curl -s "$NOP_BASE/api/carrier/circuit" 2>/dev/null \
        | python3 -c "import sys,json; print(json.load(sys.stdin).get('state','unknown'))" \
        2>/dev/null || echo "unknown")
    echo "      t+${t}s — circuit: $STATE"
    
    if [ "$STATE" == "Closed" ]; then
        RECOVER_SECS=$t
        break
    fi
done

# ── Result ─────────────────────────────────────────────────────────────────
PASS="false"
SUCCESS_OK=$(python3 -c "print('true' if float('$SUCCESS_RATE_NUM') >= 97 else 'false')")
LATENCY_OK=$([ "$P99_MS" -lt 100 ] && echo true || echo false)
RECOVER_OK=$([ "$RECOVER_SECS" -gt 0 ] && echo true || echo false)

[ "$SUCCESS_OK" == "true" ] && [ "$LATENCY_OK" == "true" ] && [ "$RECOVER_OK" == "true" ] && PASS="true"

echo ""
echo "============================================="
echo " QAS-3 Result: $([ "$PASS" == "true" ] && echo PASSED || echo FAILED)"
echo "============================================="

cat > "$RESULT_DIR/qas-3.json" << EOF
{
  "test": "qas-3-carrier-degraded",
  "result": "$( [ "$PASS" == "true" ] && echo passed || echo failed )",
  "completed_at": "$(date -Iseconds)",
  "success_rate": $SUCCESS_RATE_NUM,
  "success_count": $COUNT_200,
  "failure_count": $COUNT_FAIL,
  "fallback_count": $COUNT_FALLBACK,
  "p99_latency_ms": $P99_MS,
  "circuit_state_during": "$CIRCUIT_STATE",
  "recovery_seconds": $RECOVER_SECS,
  "details": {
    "success_rate_at_least_97pct": $SUCCESS_OK,
    "fallback_under_100ms":        $LATENCY_OK,
    "circuit_recovered":           $RECOVER_OK
  }
}
EOF
echo "    Written → $RESULT_DIR/qas-3.json"
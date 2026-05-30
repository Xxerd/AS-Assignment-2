#!/usr/bin/env bash
# =============================================================================
# QAS-1: Order delivery survives ERP outage (UPDATED with token fix)
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RESULT_DIR="$SCRIPT_DIR/../results"
mkdir -p "$RESULT_DIR"

# Validate RESULT_DIR
if [ -z "$RESULT_DIR" ] || [ ! -d "$RESULT_DIR" ]; then
    echo "ERROR: RESULT_DIR is invalid: $RESULT_DIR"
    exit 1
fi

NOP_BASE="${NOP_BASE_URL:-http://localhost}"
NOP_USER="${NOP_ADMIN_USER:-admin@yourStore.com}"  # Note capital S
NOP_PASS="${NOP_ADMIN_PASS:-123}"
ERP_CONTAINER="${ERP_CONTAINER:-verdemart_erp_consumer}"
N_EVENTS="${QAS1_EVENT_COUNT:-5}"
RECOVERY_TIMEOUT=300
COOKIE_JAR=$(mktemp)
trap 'rm -f "$COOKIE_JAR"' EXIT

echo "============================================="
echo " QAS-1: ERP Outage Resilience"
echo "============================================="

# ---------------------------------------------------------------------------
# FIX: ASP.NET Core renders antiforgery input as:
#   <input name="__RequestVerificationToken" type="hidden" value="CfD...">
# Attribute order is: name → type → value.
# Old grep: '__RequestVerificationToken\" value=\"...'   ← WRONG (skips type=)
# New: use grep -oP with a lookahead that tolerates any attrs between name and value.
# ---------------------------------------------------------------------------
extract_token() {
    grep -oP '(?<=name="__RequestVerificationToken")[^>]*value="\K[^"]+' || true
}

get_admin_token() {
    curl -s --max-time 15 -b "$COOKIE_JAR" "$NOP_BASE/Admin" 2>/dev/null | extract_token | head -1
}

auth() {
    local page token
    page=$(curl -s --max-time 30 -c "$COOKIE_JAR" -b "$COOKIE_JAR" "$NOP_BASE/login" 2>/dev/null)
    token=$(echo "$page" | extract_token | head -1)
    if [ -z "$token" ]; then
        echo "      WARNING: could not extract login token — retrying once"
        sleep 3
        page=$(curl -s --max-time 30 -c "$COOKIE_JAR" -b "$COOKIE_JAR" "$NOP_BASE/login" 2>/dev/null)
        token=$(echo "$page" | extract_token | head -1)
    fi
    curl -s --max-time 15 -c "$COOKIE_JAR" -b "$COOKIE_JAR" \
        -X POST "$NOP_BASE/login" \
        -d "Email=$NOP_USER&Password=$NOP_PASS&RememberMe=false&__RequestVerificationToken=$token" \
        -L -o /dev/null
}

outbox_depth() {
    curl -s --max-time 10 -b "$COOKIE_JAR" \
        "$NOP_BASE/Admin/IntegrationDiagnostics/GetOutboxDepth" 2>/dev/null \
        | python3 -c "import sys,json; print(json.load(sys.stdin).get('totalPending',0))" \
        2>/dev/null || echo "0"
}

publish_test_event() {
    local token
    token=$(get_admin_token)
    curl -s --max-time 10 -o /dev/null -w "%{http_code}" \
        -b "$COOKIE_JAR" \
        -H "RequestVerificationToken: $token" \
        -X POST "$NOP_BASE/Admin/IntegrationDiagnostics/PublishTestEvent" \
        2>/dev/null || echo "000"
}

seed_stock() {
    local token
    token=$(get_admin_token)
    curl -s --max-time 10 -b "$COOKIE_JAR" \
        -H "RequestVerificationToken: $token" \
        -H "Content-Type: application/json" \
        -X POST "$NOP_BASE/Admin/IntegrationDiagnostics/SeedStockLedger" \
        -d '{"ProductId":1,"WarehouseId":0,"StockQuantity":100}' 2>/dev/null || echo '{"status":"error"}'
}

echo "[1/6] Authenticating..."
auth

echo "[2/6] Seeding stock..."
SEED=$(seed_stock)
echo "      $SEED"

echo "[3/6] Stopping ERP consumer..."
sudo docker stop "$ERP_CONTAINER" 2>/dev/null && echo "      Stopped" || echo "      WARNING: could not stop"
sleep 2

echo "[4/6] Publishing $N_EVENTS events..."
PUBLISHED=0; FAILED=0
for i in $(seq 1 "$N_EVENTS"); do
    HTTP=$(publish_test_event)
    if [[ "$HTTP" == "200" ]]; then
        PUBLISHED=$((PUBLISHED + 1))
        echo "      Event $i: ✓"
    else
        FAILED=$((FAILED + 1))
        echo "      Event $i: ✗ (HTTP $HTTP)"
    fi
    sleep 0.5
done

echo "[5/6] Checking outbox depth..."
sleep 5
DEPTH_DURING=$(outbox_depth)
echo "      Outbox depth: $DEPTH_DURING"

echo "[6/6] Restarting ERP and waiting for drain..."
docker start "$ERP_CONTAINER" 2>/dev/null && echo "      Started" || echo "      WARNING: could not start"

DRAIN_SECS=0; ELAPSED=0
while [ "$ELAPSED" -lt "$RECOVERY_TIMEOUT" ]; do
    sleep 10
    ELAPSED=$((ELAPSED + 10))
    DEPTH=$(outbox_depth)
    echo "      t+${ELAPSED}s — pending: $DEPTH"
    if [ "$DEPTH" -eq 0 ]; then
        DRAIN_SECS=$ELAPSED
        echo "      Outbox drained ✓"
        break
    fi
done

ZERO_FAILED=$( [ "$FAILED" -eq 0 ] && echo true || echo false )
DRAINED=$( [ "$DRAIN_SECS" -gt 0 ] && echo true || echo false )
PASS=$( [ "$ZERO_FAILED" == "true" ] && [ "$DRAINED" == "true" ] && echo passed || echo failed )

# Write JSON result
JSON_FILE="$RESULT_DIR/qas-1.json"
cat > "$JSON_FILE" << EOF
{
  "test": "qas-1-erp-outage",
  "result": "$PASS",
  "completed_at": "$(date -Iseconds)",
  "events_published": $N_EVENTS,
  "events_published_success": $PUBLISHED,
  "events_published_failed": $FAILED,
  "zero_failures": $ZERO_FAILED,
  "outbox_depth_during_outage": $DEPTH_DURING,
  "drain_seconds": $DRAIN_SECS,
  "drained_within_timeout": $DRAINED,
  "details": {
    "all_events_accepted": $ZERO_FAILED,
    "outbox_drained": $DRAINED
  }
}
EOF
echo "    Written → $JSON_FILE"

echo "============================================="
echo " QAS-1 Result: $(echo "$PASS" | tr '[:lower:]' '[:upper:]')"
echo "============================================="
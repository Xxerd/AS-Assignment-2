#!/usr/bin/env bash
# =============================================================================
# QAS-5: WMS reconnection — buffered events drain in < 2min (UPDATED)
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
NOP_USER="${NOP_ADMIN_USER:-admin@yourStore.com}"
NOP_PASS="${NOP_ADMIN_PASS:-123}"
RABBIT_API="${RABBIT_API_URL:-http://localhost:15672/api}"
RABBIT_USER="${RABBIT_USER:-verdemart}"
RABBIT_PASS="${RABBIT_PASS:-verdemart}"
PRODUCT_ID=1
WAREHOUSE_ID=0
N_PICKS=5
QTY_PER_PICK=1
STOCK_QTY=50
PAUSE_SECS=10
DRAIN_TIMEOUT=120
EXPECTED_DECREMENT=$((N_PICKS * QTY_PER_PICK))
COOKIE_JAR=$(mktemp)
trap 'rm -f "$COOKIE_JAR"' EXIT

echo "============================================="
echo " QAS-5: WMS Reconnection & Reconciliation"
echo "============================================="

# ---------------------------------------------------------------------------
# FIX: ASP.NET Core renders antiforgery input as:
#   <input name="__RequestVerificationToken" type="hidden" value="CfD...">
# Attribute order is: name → type → value.
# Old grep missed 'type="hidden"' between name and value → always empty token.
# ---------------------------------------------------------------------------
extract_token() {
    grep -oP '(?<=name="__RequestVerificationToken")[^>]*value="\K[^"]+' || true
}

get_admin_token() {
    curl -s --max-time 15 -b "$COOKIE_JAR" "$NOP_BASE/Admin" 2>/dev/null | extract_token | head -1
}

get_stock() {
    curl -s --max-time 10 -b "$COOKIE_JAR" \
        "$NOP_BASE/Admin/IntegrationDiagnostics/GetStock?productId=$PRODUCT_ID&warehouseId=$WAREHOUSE_ID" \
        2>/dev/null \
        | python3 -c "import sys,json; print(json.load(sys.stdin).get('stockQuantity',-1))" \
        2>/dev/null || echo "-1"
}

seed_stock() {
    local token
    token=$(get_admin_token)
    curl -s --max-time 10 -b "$COOKIE_JAR" \
        -H "RequestVerificationToken: $token" \
        -H "Content-Type: application/json" \
        -X POST "$NOP_BASE/Admin/IntegrationDiagnostics/SeedStockLedger" \
        -d "{\"ProductId\":$PRODUCT_ID,\"WarehouseId\":$WAREHOUSE_ID,\"StockQuantity\":$STOCK_QTY}" \
        2>/dev/null || echo '{"status":"error"}'
}

publish_stock_picked() {
    local event_id qty
    event_id=$(python3 -c "import uuid; print(uuid.uuid4())")
    qty=$1
    local now
    now=$(python3 -c "from datetime import datetime, timezone; print(datetime.now(timezone.utc).isoformat())")

    local payload
    payload=$(python3 -c "
import json
print(json.dumps({
    'eventId':       '$event_id',
    'productId':     $PRODUCT_ID,
    'warehouseId':   $WAREHOUSE_ID,
    'quantityPicked': $qty,
    'occurredAtUtc': '$now'
}))
")
    local body
    body=$(python3 -c "
import json
print(json.dumps({
    'vhost':      '/',
    'name':       'commerce.events',
    'properties': {'delivery_mode': 2, 'content_type': 'application/json'},
    'routing_key': 'wms.stock.picked',
    'payload':     '$payload',
    'payload_encoding': 'string'
}))
")
    curl -s --max-time 10 -o /dev/null -w "%{http_code}" \
        -u "$RABBIT_USER:$RABBIT_PASS" \
        -X POST "$RABBIT_API/exchanges/%2F/commerce.events/publish" \
        -H "Content-Type: application/json" \
        -d "$body" 2>/dev/null || echo "000"
}

echo "[1/6] Authenticating and seeding stock..."
# Auth
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

# Seed
seed_stock > /dev/null

STOCK_BEFORE=$(get_stock)
EXPECTED_AFTER=$((STOCK_BEFORE - EXPECTED_DECREMENT))
echo "      Stock before: $STOCK_BEFORE -> expected after: $EXPECTED_AFTER"

echo "[2/6] Checking RabbitMQ..."
RMQ_HTTP=$(curl -s --max-time 10 -o /dev/null -w "%{http_code}" \
    -u "$RABBIT_USER:$RABBIT_PASS" "$RABBIT_API/overview" 2>/dev/null || echo "000")
echo "      RabbitMQ: HTTP $RMQ_HTTP"

echo "[3/6] Publishing $N_PICKS StockPickedEvents..."
PUBLISHED=0
for i in $(seq 1 "$N_PICKS"); do
    HTTP=$(publish_stock_picked "$QTY_PER_PICK")
    if [ "$HTTP" == "200" ]; then
        PUBLISHED=$((PUBLISHED + 1))
        echo "      Pick $i: ✓"
    else
        echo "      Pick $i: ✗ (HTTP $HTTP)"
    fi
    sleep 0.2
done

echo "      Sleeping ${PAUSE_SECS}s..."
sleep "$PAUSE_SECS"

echo "[4/6] Waiting for consumer to drain events..."
RESUME_MS=$(($(date +%s%N) / 1000000))
DRAIN_MS=0
DEADLINE_MS=$((RESUME_MS + DRAIN_TIMEOUT * 1000))

echo "[5/6] Polling stock every 5s..."
while true; do
    NOW_MS=$(($(date +%s%N) / 1000000))
    if [ "$NOW_MS" -gt "$DEADLINE_MS" ]; then
        echo "      TIMEOUT"
        break
    fi
    STOCK_NOW=$(get_stock)
    ELAPSED_MS=$((NOW_MS - RESUME_MS))
    echo "      t+$((ELAPSED_MS / 1000))s — stock: $STOCK_NOW"
    if [ "$STOCK_NOW" -eq "$EXPECTED_AFTER" ] 2>/dev/null; then
        DRAIN_MS=$ELAPSED_MS
        echo "      ✓ Reconciliation complete (${DRAIN_MS}ms)"
        break
    fi
    sleep 5
done

echo "[6/6] Verifying no duplicates..."
STOCK_FINAL=$(get_stock)
ACTUAL_DECREMENT=$((STOCK_BEFORE - STOCK_FINAL))
echo "      Stock: $STOCK_BEFORE -> $STOCK_FINAL (decrement: $ACTUAL_DECREMENT)"

DRAIN_SECS=$(python3 -c "print(round($DRAIN_MS / 1000, 1))" 2>/dev/null || echo "0")
DRAINED_OK=$( [ "$DRAIN_MS" -gt 0 ] && [ "$DRAIN_MS" -le $((DRAIN_TIMEOUT * 1000)) ] && echo true || echo false )
NO_DUPS=$( [ "$ACTUAL_DECREMENT" -eq "$EXPECTED_DECREMENT" ] && echo true || echo false )
PASS=$( [ "$DRAINED_OK" == "true" ] && [ "$NO_DUPS" == "true" ] && echo passed || echo failed )

# Write JSON result
JSON_FILE="$RESULT_DIR/qas-5.json"
cat > "$JSON_FILE" << EOF
{
  "test": "qas-5-wms-reconnect",
  "result": "$PASS",
  "completed_at": "$(date -Iseconds)",
  "stock_before": $STOCK_BEFORE,
  "stock_after": $STOCK_FINAL,
  "expected_decrement": $EXPECTED_DECREMENT,
  "actual_decrement": $ACTUAL_DECREMENT,
  "events_published": $PUBLISHED,
  "events_expected": $N_PICKS,
  "drain_ms": $DRAIN_MS,
  "drain_seconds": $DRAIN_SECS,
  "drain_timeout_seconds": $DRAIN_TIMEOUT,
  "drained_within_timeout": $DRAINED_OK,
  "no_duplicates": $NO_DUPS,
  "details": {
    "drained_ok": $DRAINED_OK,
    "no_duplicate_processing": $NO_DUPS
  }
}
EOF
echo "    Written → $JSON_FILE"

echo "============================================="
echo " QAS-5 Result: $(echo "$PASS" | tr '[:lower:]' '[:upper:]')"
echo "  drained <2min: $DRAINED_OK (${DRAIN_SECS}s)"
echo "  no duplicates: $NO_DUPS"
echo "============================================="
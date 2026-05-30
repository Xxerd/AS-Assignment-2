#!/usr/bin/env bash
# =============================================================================
# QAS-4: Cross-channel order visibility — TrackingUpdated → API < 10s (UPDATED)
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
RABBIT_API="${RABBIT_API_URL:-http://localhost:15672/api}"
RABBIT_USER="${RABBIT_USER:-verdemart}"
RABBIT_PASS="${RABBIT_PASS:-verdemart}"
ORDER_ID="${QAS4_ORDER_ID:-1}"
TRACKING_NUMBER="VM-QAS4-$(date +%s)"
TIMEOUT_MS=10000

echo "============================================="
echo " QAS-4: Cross-Channel Order Visibility"
echo "============================================="
echo "  Order ID    : $ORDER_ID"
echo "  Tracking    : $TRACKING_NUMBER"
echo ""

# First, find an existing order ID if default doesn't work
if [ "$ORDER_ID" == "1" ]; then
    # Try to find any order
    ORDER_CHECK=$(curl -s "$NOP_BASE/api/orders/1/status" 2>/dev/null | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('orderId',0))" 2>/dev/null || echo "0")
    if [ "$ORDER_CHECK" == "0" ]; then
        echo "WARNING: Order 1 not found. Please set QAS4_ORDER_ID to an existing order ID"
        echo "You can create an order via: curl -X POST http://localhost/api/shoppingcart/add..."
    fi
fi

echo "[1/3] Checking order $ORDER_ID..."
ORDER_HTTP=$(curl -s -o /dev/null -w "%{http_code}" \
    "$NOP_BASE/api/orders/$ORDER_ID/status" 2>/dev/null || echo "000")
echo "      HTTP $ORDER_HTTP"

echo "[2/3] Publishing TrackingUpdatedEvent to RabbitMQ..."
EVENT_ID=$(python3 -c "import uuid; print(uuid.uuid4())")
NOW_UTC=$(python3 -c "from datetime import datetime, timezone; print(datetime.now(timezone.utc).isoformat())")

PAYLOAD=$(python3 -c "
import json
print(json.dumps({
    'eventId':        '$EVENT_ID',
    'orderId':        $ORDER_ID,
    'trackingNumber': '$TRACKING_NUMBER',
    'shippedAtUtc':   '$NOW_UTC',
    'occurredAtUtc':  '$NOW_UTC'
}))
")

PUBLISH_BODY=$(python3 -c "
import json
print(json.dumps({
    'vhost':      '/',
    'name':       'commerce.events',
    'properties': {'delivery_mode': 2, 'content_type': 'application/json'},
    'routing_key': 'wms.shipment.dispatched',
    'payload':     '$PAYLOAD',
    'payload_encoding': 'string'
}))
")

PUBLISH_HTTP=$(curl -s -o /dev/null -w "%{http_code}" \
    -u "$RABBIT_USER:$RABBIT_PASS" \
    -X POST "$RABBIT_API/exchanges/%2F/commerce.events/publish" \
    -H "Content-Type: application/json" \
    -d "$PUBLISH_BODY" 2>/dev/null || echo "000")
echo "      Publish: HTTP $PUBLISH_HTTP"

if [ "$PUBLISH_HTTP" != "200" ]; then
    echo "ERROR: Could not publish to RabbitMQ"
    PASS="failed"
    VISIBLE_MS=0
else
    PUBLISH_MS=$(($(date +%s%N) / 1000000))
    echo "[3/3] Polling for tracking number (timeout: 10s)..."
    VISIBLE_MS=0
    DEADLINE_MS=$((PUBLISH_MS + TIMEOUT_MS))

    while true; do
        NOW_MS=$(($(date +%s%N) / 1000000))
        if [ "$NOW_MS" -gt "$DEADLINE_MS" ]; then
            echo "      TIMEOUT — tracking not reflected"
            break
        fi

        TRACKING_IN_API=$(curl -s "$NOP_BASE/api/orders/$ORDER_ID/status" 2>/dev/null \
            | python3 -c "import sys,json; d=json.load(sys.stdin); s=d.get('shipment',{}); print(s.get('trackingNumber',''))" 2>/dev/null || echo "")

        ELAPSED_MS=$((NOW_MS - PUBLISH_MS))
        echo "      t+${ELAPSED_MS}ms — tracking: '${TRACKING_IN_API}'"

        if [ "$TRACKING_IN_API" == "$TRACKING_NUMBER" ]; then
            VISIBLE_MS=$ELAPSED_MS
            echo "      ✓ Visible in ${VISIBLE_MS}ms"
            break
        fi
        sleep 0.5
    done
    PASS=$( [ "$VISIBLE_MS" -gt 0 ] && [ "$VISIBLE_MS" -lt 10000 ] && echo passed || echo failed )
fi

# Write JSON result
JSON_FILE="$RESULT_DIR/qas-4.json"
if [ "$PUBLISH_HTTP" != "200" ]; then
    cat > "$JSON_FILE" << EOF
{
  "test": "qas-4-order-visibility",
  "result": "failed",
  "completed_at": "$(date -Iseconds)",
  "order_id": $ORDER_ID,
  "tracking_number": "$TRACKING_NUMBER",
  "publish_success": false,
  "publish_http_status": $PUBLISH_HTTP,
  "visible_latency_ms": 0,
  "error": "Failed to publish to RabbitMQ"
}
EOF
else
    cat > "$JSON_FILE" << EOF
{
  "test": "qas-4-order-visibility",
  "result": "$PASS",
  "completed_at": "$(date -Iseconds)",
  "order_id": $ORDER_ID,
  "tracking_number": "$TRACKING_NUMBER",
  "publish_success": true,
  "visible_latency_ms": $VISIBLE_MS,
  "within_10s": $( [ "$VISIBLE_MS" -gt 0 ] && [ "$VISIBLE_MS" -lt 10000 ] && echo true || echo false ),
  "details": {
    "max_allowed_ms": 10000
  }
}
EOF
fi
echo "    Written → $JSON_FILE"

echo "============================================="
echo " QAS-4 Result: $(echo "$PASS" | tr '[:lower:]' '[:upper:]')"
echo "  latency: ${VISIBLE_MS}ms"
echo "============================================="
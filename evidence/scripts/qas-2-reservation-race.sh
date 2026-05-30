#!/usr/bin/env bash
# =============================================================================
# QAS-2: Cross-channel stock reservation race condition (UPDATED with token fix)
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
PRODUCT_ID=1
WAREHOUSE_ID=0
CONCURRENT=50
TMPDIR_RACE=$(mktemp -d)
COOKIE_JAR=$(mktemp)
trap 'rm -rf "$TMPDIR_RACE" "$COOKIE_JAR"' EXIT

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

seed_stock() {
    local token
    token=$(get_admin_token)
    curl -s --max-time 10 -b "$COOKIE_JAR" \
        -H "RequestVerificationToken: $token" \
        -H "Content-Type: application/json" \
        -X POST "$NOP_BASE/Admin/IntegrationDiagnostics/SeedStockLedger" \
        -d '{"ProductId":1,"WarehouseId":0,"StockQuantity":1}'
}

get_stock() {
    curl -s --max-time 10 -b "$COOKIE_JAR" \
        "$NOP_BASE/Admin/IntegrationDiagnostics/GetStock?productId=1&warehouseId=0" \
        2>/dev/null | python3 -c "import sys,json; print(json.load(sys.stdin).get('availableQuantity',0))" 2>/dev/null || echo "0"
}

echo "============================================="
echo " QAS-2: Concurrent Stock Reservation Race"
echo "============================================="

echo "[1/4] Authenticating..."
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

echo "[2/4] Seeding 1 unit..."
seed_stock > /dev/null
AVAILABLE=$(get_stock)
echo "      Available stock: $AVAILABLE"

echo "[3/4] Firing $CONCURRENT concurrent requests..."
for i in $(seq 1 "$CONCURRENT"); do
    (
        IDEM_KEY=$(python3 -c "import uuid; print(uuid.uuid4())")
        HTTP=$(curl -s --max-time 10 -o /dev/null -w "%{http_code}" \
            -X POST "$NOP_BASE/api/inventory/reservations" \
            -H "Content-Type: application/json" \
            -d "{\"reservationId\":\"$IDEM_KEY\",\"productId\":$PRODUCT_ID,\"warehouseId\":$WAREHOUSE_ID,\"quantity\":1}" \
            2>/dev/null || echo "000")
        echo "$HTTP" > "$TMPDIR_RACE/r_$i"
    ) &
done
wait

echo "[4/4] Results:"
COUNT_200=0; COUNT_409=0; COUNT_500=0
for i in $(seq 1 "$CONCURRENT"); do
    CODE=$(cat "$TMPDIR_RACE/r_$i" 2>/dev/null || echo "000")
    case "$CODE" in
        200|201) COUNT_200=$((COUNT_200 + 1)) ;;
        409) COUNT_409=$((COUNT_409 + 1)) ;;
        500) COUNT_500=$((COUNT_500 + 1)) ;;
    esac
done

echo "      HTTP 200: $COUNT_200 (expected: 1)"
echo "      HTTP 409: $COUNT_409 (expected: $((CONCURRENT - 1)))"
echo "      HTTP 500: $COUNT_500 (expected: 0)"

ONE_WINNER=$( [ "$COUNT_200" -eq 1 ] && echo true || echo false )
ZERO_ERRORS=$( [ "$COUNT_500" -eq 0 ] && echo true || echo false )
PASS=$( [ "$ONE_WINNER" == "true" ] && [ "$ZERO_ERRORS" == "true" ] && echo passed || echo failed )

# Write JSON result
JSON_FILE="$RESULT_DIR/qas-2.json"
cat > "$JSON_FILE" << EOF
{
  "test": "qas-2-reservation-race",
  "result": "$PASS",
  "completed_at": "$(date -Iseconds)",
  "concurrent_requests": $CONCURRENT,
  "http_200_count": $COUNT_200,
  "http_409_count": $COUNT_409,
  "http_500_count": $COUNT_500,
  "exactly_one_winner": $ONE_WINNER,
  "no_server_errors": $ZERO_ERRORS,
  "details": {
    "expected_200": 1,
    "expected_409": $((CONCURRENT - 1)),
    "expected_500": 0
  }
}
EOF
echo "    Written → $JSON_FILE"

echo "============================================="
echo " QAS-2 Result: $(echo "$PASS" | tr '[:lower:]' '[:upper:]')"
echo "============================================="
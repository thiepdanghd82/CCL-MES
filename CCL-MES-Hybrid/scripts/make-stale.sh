#!/usr/bin/env bash
# P10.7a-1.3 — operator-facing helper that simulates "another shift's
# kiosk just advanced this WO." Runs server-side via curl so the
# RowVersion gets bumped without involving the Catalyst client. The
# operator then taps Accept in the app and watches the VN
# state-conflict banner appear.
#
# This is the ONE manual side-task the operator runs during the
# Catalyst checkpoint. Everything else is automated.
#
# Usage:
#   bash scripts/make-stale.sh <WO_NUMBER> [--url http://127.0.0.1:5100]
# Example:
#   bash scripts/make-stale.sh WO-26-3683
#
# Output: human-readable confirmation that the WO's ETag has changed,
# plus a one-line instruction in Vietnamese telling the operator
# what to do next.

set -u
set +e

API_URL="${API_URL:-http://127.0.0.1:5100}"
CCL_USER="${CCL_USER:-admin}"
CCL_PWD="${CCL_PWD:-admin}"

WO_NO="${1:-}"
shift 2>/dev/null || true
for arg in "$@"; do
    case "$arg" in
        --url=*)   API_URL="${arg#--url=}" ;;
        --url)     shift; API_URL="$1" ;;
        --user=*)  CCL_USER="${arg#--user=}" ;;
        --pwd=*)   CCL_PWD="${arg#--pwd=}" ;;
    esac
done

if [[ -z "$WO_NO" ]]; then
    cat <<USAGE
Usage: $0 <WO_NUMBER> [--url http://host:port] [--user admin] [--pwd admin]

Example:
  bash scripts/make-stale.sh WO-26-3683
  bash scripts/make-stale.sh WO-26-3683 --url http://10.0.5.12:5100
USAGE
    exit 2
fi

echo "════════════════════════════════════════════════════════════════════"
echo "  make-stale — simulates another operator advancing WO $WO_NO"
echo "════════════════════════════════════════════════════════════════════"
echo "  API:  $API_URL"
echo "  WO:   $WO_NO"
echo "  As:   $CCL_USER"
echo ""

# 1. Login.
LOGIN=$(curl -sS -X POST "$API_URL/api/v2/auth/login" \
    -H "Content-Type: application/json" \
    -d "{\"username\":\"$CCL_USER\",\"password\":\"$CCL_PWD\"}")
TOKEN=$(echo "$LOGIN" | python3 -c "
import sys, json
try: print(json.load(sys.stdin).get('accessToken',''))
except Exception: pass
" 2>/dev/null)

if [[ -z "$TOKEN" || "$TOKEN" == "null" ]]; then
    echo "❌ Login failed. Server response:"
    echo "$LOGIN" | head -3
    echo ""
    echo "Common causes:"
    echo "  - Server not running on $API_URL"
    echo "  - DB has pending migrations (see server boot log for WARNING)"
    echo "  - Wrong credentials (default admin/admin; override with --user/--pwd)"
    exit 1
fi
echo "  ✓ Login OK (token len=${#TOKEN})"

# 2. GET summary → capture ETag + WO id.
SUMMARY=$(curl -sS -H "Authorization: Bearer $TOKEN" \
    "$API_URL/api/v2/work-orders/by-no/$WO_NO/summary")
WO_ID=$(echo "$SUMMARY" | python3 -c "
import sys, json
try: print(json.load(sys.stdin).get('id',''))
except Exception: pass
" 2>/dev/null)
OLD_ETAG=$(echo "$SUMMARY" | python3 -c "
import sys, json
try: print(json.load(sys.stdin).get('eTag',''))
except Exception: pass
" 2>/dev/null)

if [[ -z "$WO_ID" || "$WO_ID" == "null" ]]; then
    echo "❌ WO '$WO_NO' not found on the server. Response head:"
    echo "$SUMMARY" | head -3
    exit 1
fi
echo "  ✓ Summary fetched (id=$WO_ID, etag=${OLD_ETAG:0:12}…)"

# 3. Advance — same path the Catalyst client would use, just from
#    the wire. The trigger bumps RowVersion → ETag changes.
ADVANCE=$(curl -sS -X POST "$API_URL/api/v2/work-orders/$WO_ID/advance" \
    -H "Authorization: Bearer $TOKEN" \
    -H "If-Match: \"$OLD_ETAG\"" \
    -H "Idempotency-Key: $(uuidgen)")
NEW_ETAG=$(echo "$ADVANCE" | python3 -c "
import sys, json
try: print(json.load(sys.stdin).get('eTag',''))
except Exception: pass
" 2>/dev/null)
ADVANCE_OK=$(echo "$ADVANCE" | python3 -c "
import sys, json
try: print(json.load(sys.stdin).get('ok', False))
except Exception: pass
" 2>/dev/null)

if [[ -z "$NEW_ETAG" || "$NEW_ETAG" == "$OLD_ETAG" ]]; then
    echo "⚠️  Advance call did not bump the ETag. Response head:"
    echo "$ADVANCE" | head -3
    echo ""
    echo "Note: if the WO is at a state-machine guard (e.g. PrePressCheck"
    echo "without ProductRevisionId), the server returns 200 with ok=false"
    echo "and does NOT bump RowVersion. Use a different WO at a step that"
    echo "advances unconditionally (e.g. ReadyToRun → Running)."
    exit 1
fi

echo "  ✓ Server-side advance fired"
echo "    old etag: $OLD_ETAG"
echo "    new etag: $NEW_ETAG"
echo "    ok flag : $ADVANCE_OK"
echo ""
echo "════════════════════════════════════════════════════════════════════"
echo "  ⚠️  WO $WO_NO is now STALE in the Catalyst app's cache."
echo ""
echo "  Quay lại ứng dụng Catalyst, KHÔNG quét lại — bấm 'Nhận / Bắt đầu'"
echo "  bằng ETag cũ. Bạn sẽ thấy banner vàng:"
echo ""
echo "      Một thao tác khác đã cập nhật WO này. Bấm 'Nhận / Bắt đầu'"
echo "      lần nữa để thử lại với phiên bản mới nhất."
echo ""
echo "  Đó là checkpoint mục 5 của 7a-1.3 — xác nhận bằng mắt là ĐỦ."
echo "════════════════════════════════════════════════════════════════════"

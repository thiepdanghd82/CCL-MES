#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# gate-materials-ready — WorkOrders.MaterialsReady phải khớp với các dòng nó
# tóm tắt. Không dòng nào còn Pending mà WO vẫn tự nhận "vật tư đã sẵn sàng".
#
# SỰ CỐ 2026-09-10:
#   Đo trên DB live: 42 dòng WoMaterials thuộc 5 WO ở trạng thái Pending, KHÔNG
#   AI từng ký (CheckedBy NULL), trong khi WO cha mang MaterialsReady=1. Cờ ấy
#   chính là điều kiện cổng PREPRESS → SETTING (WorkOrderStateMachine:45), nên
#   5 WO đó đi tiếp mà chưa dòng vật tư nào được kiểm.
#
#   RCA đã chứng minh KHÔNG có đường nào trong app tạo ra nổi trạng thái đó:
#     · /advance đọc MaterialsReady, mà chỉ rollup ghi được cờ ấy
#     · force-phase từ chối vì ô này là RequiresCondition, không RecoveryOnly
#     · nút "Unlock" của app legacy có ghi WO_FLAGS_UPDATE — 0 dòng trong DB
#   Bằng chứng: 0 dòng WoStatusHistories, 0 dòng WO_PREPRESS_*, 0 WO_ADVANCE,
#   và 2/5 WO còn UpdatedAt NULL. Tức trạng thái vào bằng SQL TRỰC TIẾP.
#
#   Cùng hạng với sự cố CurrentStep='Done' (19/08) mà gate-enum-integrity canh:
#   rác đi vòng qua đường có audit, nên CI không bao giờ thấy. Test đơn vị vô
#   dụng ở đây — luật rollup vốn ĐÚNG; cái sai nằm trong DỮ LIỆU.
#
# VÌ SAO BẤT BIẾN NÀY ĐÚNG:
#   MaterialsReadinessRollup.IsLineReady đòi m.Status == Ok. Nên allOk == true
#   ⇒ mọi dòng Ok ⇒ không dòng nào Pending. Một dòng Pending dưới WO báo Ready
#   là mâu thuẫn, KHÔNG phải một ca nghiệp vụ hợp lệ.
#
# Ratchet: baseline chỉ được GIẢM. Tăng baseline phải kèm giải thích trong
# cùng PR — xem CLAUDE.md §0 STOP-gate.
#
# Ba trạng thái, cố ý không phải hai:
#   exit 0  sạch (hoặc không có DB để quét — nói rõ là BỎ QUA)
#   exit 1  số dòng lệch VƯỢT baseline
#   exit 2  không kết luận được. "Không kiểm được" KHÔNG phải "đã kiểm và sạch".
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

# 2026-09-10: 42 dòng lệch đã đi theo 27 WO test bị xoá; DB live về 0.
BASELINE=0

here="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$here/../.." && pwd)"
DEFAULT_DB="$ROOT/data/ccl_mes.db"

SQL_COUNT="SELECT COUNT(*) FROM WorkOrders w
           JOIN WoMaterials m ON m.WorkOrderId = w.Id
           WHERE w.MaterialsReady = 1 AND m.Status = 'Pending';"

count_divergent() {
  sqlite3 "file:$1?mode=ro" "$SQL_COUNT" 2>/dev/null
}

# ── self-test ───────────────────────────────────────────────────────────────
if [ "${1:-}" = "--self-test" ]; then
  db="${MES_MATERIALS_READY_DB:-$DEFAULT_DB}"
  if [ ! -f "$db" ] || ! command -v sqlite3 >/dev/null 2>&1; then
    echo "[gate:materials-ready] self-test: BỎ QUA (thiếu DB fixture / sqlite3)"; exit 0
  fi
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  cp "$db" "$tmp/probe.db"

  # Dựng đúng hình dạng của sự cố trên BẢN SAO: một dòng Pending nằm dưới một
  # WO mang MaterialsReady=1. Bắt được thì gate mới có giá trị.
  sqlite3 "$tmp/probe.db" "
    UPDATE WorkOrders SET MaterialsReady = 1
      WHERE Id = (SELECT WorkOrderId FROM WoMaterials LIMIT 1);
    UPDATE WoMaterials SET Status = 'Pending'
      WHERE Id = (SELECT Id FROM WoMaterials LIMIT 1);" >/dev/null 2>&1

  n="$(count_divergent "$tmp/probe.db")"
  if [ "${n:-0}" -gt "$BASELINE" ]; then
    echo "[gate:materials-ready] self-test OK (tiêm 1 dòng Pending dưới WO Ready ⇒ bắt được, n=$n)"
    exit 0
  fi
  echo "[gate:materials-ready] self-test FAILED — tiêm mâu thuẫn mà gate không thấy (n=${n:-?})"
  exit 1
fi

# ── chạy thật ───────────────────────────────────────────────────────────────
DB="${1:-${MES_MATERIALS_READY_DB:-$DEFAULT_DB}}"

if ! command -v sqlite3 >/dev/null 2>&1; then
  echo "[gate:materials-ready] BỎ QUA — không có sqlite3 trên máy này."; exit 0
fi
if [ ! -f "$DB" ]; then
  echo "[gate:materials-ready] BỎ QUA — không thấy DB ${DB#$ROOT/}"
  echo "  (*.db bị .gitignore loại; bản clone mới / CI không có. Chĩa gate vào DB khác"
  echo "   bằng MES_MATERIALS_READY_DB=<path> hoặc tham số 1.)"
  exit 0
fi

n="$(count_divergent "$DB")"
if [ -z "$n" ]; then
  echo "[gate:materials-ready:FAIL] KHÔNG đọc được ${DB#$ROOT/} (khoá / thiếu bảng / lạc hậu migration)."
  echo "  Không kiểm được KHÔNG phải là đạt."
  exit 2
fi

echo "[gate:materials-ready] dòng Pending dưới WO báo Ready = $n (baseline $BASELINE)"
if [ "$n" -gt "$BASELINE" ]; then
  echo "[gate:materials-ready:FAIL] cờ MaterialsReady KHÔNG khớp với các dòng nó tóm tắt."
  sqlite3 "file:$DB?mode=ro" "
    SELECT '  WO '||w.Id||' '||w.WoNo||' ('||w.MesPhase||') — '||COUNT(*)||' dòng Pending'
    FROM WorkOrders w JOIN WoMaterials m ON m.WorkOrderId = w.Id
    WHERE w.MaterialsReady = 1 AND m.Status = 'Pending'
    GROUP BY w.Id;" 2>/dev/null
  echo "  Sửa DỮ LIỆU, đừng nới baseline: cho rollup chạy lại bằng một lần ghi qua"
  echo "  PUT /work-orders/{id}/materials/{idx}, hoặc đưa dòng về đúng trạng thái thật."
  echo "  Cờ này là điều kiện cổng PREPRESS → SETTING — sai nó là hàng đi tiếp mà chưa ai kiểm."
  exit 1
fi

echo "[gate:materials-ready:OK] cờ khớp với dòng — không WO nào tự nhận sẵn sàng."
exit 0

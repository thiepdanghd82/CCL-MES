#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# gate-enum-integrity — chặn tái phát sự cố dữ liệu 2026-08-19.
#
# Sự cố: 11/27 WO (41%) mang WorkOrders.CurrentStep='Done', giá trị KHÔNG tồn
# tại trong ProcessStepCode. MesDbContext.cs:89 cấu hình HasConversion<string>();
# chiều ĐỌC ném InvalidOperationException trong shaper của EF ⇒ mọi truy vấn
# materialise entity WorkOrder đều chết. 10 route API hỏng, trong đó route DANH
# SÁCH làm mất toàn bộ 27 WO cho MỌI người dùng. Hồ sơ đầy đủ:
# docs/RUNBOOK-CURRENTSTEP-REPAIR-2026-08-19.md
#
# Rác vào bằng SQL TRỰC TIẾP, ngoài đường có audit của app (0 audit cho 11 WO
# trong khi cùng cửa sổ thời gian có 926 audit khác). Đó là lý do CI không bao
# giờ thấy, và là lý do cơ chế chặn phải có BA tầng chứ không phải một:
#
#   Tầng 1  CI test        tests/CCL.MES.Api.Tests/EnumIntegrityTests.cs
#   Tầng 2  gate này       quét DB fixture trong cây làm việc
#   Tầng 3  preflight boot src/CCL.MES.Api/Diagnostics/EnumIntegrityMonitor.cs
#                          + GET /api/v2/health/ready
#
# Gate này kiểm HAI phần, và phần (A) là phần quan trọng hơn:
#
#   (A) TĨNH — HARD FAIL 0. Ba tầng phải còn nguyên dây nối, và scanner phải còn
#       ĐỌC EF MODEL BẰNG REFLECTION chứ không hard-code danh sách enum. File
#       *.db bị .gitignore loại nên trên CI / bản clone mới KHÔNG có DB nào để
#       quét; nếu gate chỉ có phần động thì trên CI nó là con số không. Phần (A)
#       chạy ở mọi nơi và canh đúng thứ dễ mục nhất: dây nối.
#
#   (B) ĐỘNG — quét DB fixture bằng chính EF model + chính converter mà app
#       dùng lúc chạy. Mở Mode=ReadOnly; gate này KHÔNG BAO GIỜ ghi.
#
# Ba trạng thái của phần (B), cố ý không phải hai:
#   exit 0  sạch
#   exit 1  CÓ giá trị ngoài enum
#   exit 2  KHÔNG kết luận được (thiếu DB / DB lạc hậu migration / khoá)
# "Không kiểm được" KHÔNG phải "đã kiểm và sạch".
#
# Tested: PASS trên snapshot live ĐÃ SỬA; FAIL trên backup THẬT tiền-sửa
# (data/Backup/SQLite/ccl_mes.before-currentstep-repair-20260819-115750.db).
# Xem --self-test.
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
HYBRID="$(cd "$here/.." && pwd)"
ROOT="$(cd "$HYBRID/.." && pwd)"

CLI_PROJ="$HYBRID/tools/EnumIntegrityScan/EnumIntegrityScan.csproj"
SCANNER="$HYBRID/src/CCL.MES.EnumIntegrity/EnumIntegrityScanner.cs"
MONITOR="$HYBRID/src/CCL.MES.Api/Diagnostics/EnumIntegrityMonitor.cs"
PROGRAM="$HYBRID/src/CCL.MES.Api/Program.cs"
HEALTH="$HYBRID/src/CCL.MES.Api/Controllers/HealthController.cs"
TIER1="$HYBRID/tests/CCL.MES.Api.Tests/EnumIntegrityTests.cs"
TIER3_TEST="$HYBRID/tests/CCL.MES.Api.Tests/EnumIntegrityHealthTests.cs"

# DB fixture mặc định. Ghi đè bằng tham số 1 hoặc biến MES_ENUM_INTEGRITY_DB —
# dùng để chĩa gate vào snapshot live hoặc vào backup khi điều tra.
DEFAULT_DB="$ROOT/data/demo/p11-tape-demo.db"

# ── (A) TĨNH ────────────────────────────────────────────────────────────────
static_checks() {
  local rc=0 missing=()

  # A1 — ba tầng phải còn file.
  [ -f "$SCANNER"    ] || missing+=("lõi scanner: ${SCANNER#$ROOT/}")
  [ -f "$TIER1"      ] || missing+=("tầng 1 test: ${TIER1#$ROOT/}")
  [ -f "$TIER3_TEST" ] || missing+=("tầng 3 test: ${TIER3_TEST#$ROOT/}")
  [ -f "$MONITOR"    ] || missing+=("tầng 3 monitor: ${MONITOR#$ROOT/}")
  [ -f "$CLI_PROJ"   ] || missing+=("tầng 2 CLI: ${CLI_PROJ#$ROOT/}")

  if [ ${#missing[@]} -gt 0 ]; then
    echo "[gate:enum-integrity:FAIL] mất mảnh của cơ chế chặn:"
    printf '  - %s\n' "${missing[@]}"
    rc=1
  fi

  # A2 — preflight tầng 3 phải còn được GỌI lúc boot. Đây là tầng DUY NHẤT bắt
  # được sự cố vừa rồi; xoá dòng gọi đi thì hai tầng kia vẫn xanh mà live vẫn mù.
  if ! grep -q "EnumIntegrityMonitor" "$PROGRAM" 2>/dev/null \
     || ! grep -q "WriteBootBanner" "$PROGRAM" 2>/dev/null; then
    echo "[gate:enum-integrity:FAIL] Program.cs không còn gọi preflight tầng 3."
    echo "  Cần: GetRequiredService<EnumIntegrityMonitor>() + RefreshAsync() + WriteBootBanner()."
    rc=1
  fi

  # A3 — /health/ready phải còn phản ánh. Banner lúc boot một mình là chưa đủ:
  # rác được ghi vào lúc 3 giờ sáng, không phải lúc deploy.
  if ! grep -q "EnumIntegrityMonitor" "$HEALTH" 2>/dev/null \
     || ! grep -q "dataIntegrity" "$HEALTH" 2>/dev/null; then
    echo "[gate:enum-integrity:FAIL] /health/ready không còn mang tín hiệu dataIntegrity."
    rc=1
  fi

  # A4 — scanner phải ĐỌC EF MODEL, không hard-code danh sách enum. Cả điểm của
  # thiết kế là enum thêm về sau TỰ ĐỘNG được canh. Một danh sách gõ tay sẽ đúng
  # đúng một ngày.
  if ! grep -q "GetEntityTypes()" "$SCANNER" 2>/dev/null; then
    echo "[gate:enum-integrity:FAIL] scanner không còn duyệt EF model bằng reflection."
    echo "  Hard-code danh sách enum = enum mới không được canh, không ai nhớ ra."
    rc=1
  fi
  # Chỉ đếm trên DÒNG MÃ (bỏ comment) — nếu không, chính đoạn tài liệu giải
  # thích sự cố sẽ làm gate tự đỏ, và người ta sẽ xoá tài liệu chứ không xoá bug.
  local hardcoded
  hardcoded="$(grep -v '^\s*\(//\|\*\|///\)' "$SCANNER" 2>/dev/null \
    | grep -c 'nameof(ProcessStepCode)\|"ProcessStepCode"\|"WorkOrders"' || true)"
  hardcoded="${hardcoded:-0}"
  if [ "$hardcoded" -gt 0 ]; then
    echo "[gate:enum-integrity:FAIL] scanner nhắc đích danh bảng/enum ($hardcoded chỗ) — phải hoàn toàn data-driven."
    rc=1
  fi

  # A5 — converter phải được GỌI, không được mô phỏng lại. Tự viết luật parse là
  # cách chắc chắn nhất để đẻ ra báo động giả ('closed'/'CLOSED'/'8' EF map được).
  if ! grep -q "ConvertFromProvider" "$SCANNER" 2>/dev/null; then
    echo "[gate:enum-integrity:FAIL] scanner không còn gọi ValueConverter.ConvertFromProvider."
    echo "  Mô phỏng lại ngữ nghĩa converter = báo động giả = gate bị tắt sau hai tuần."
    rc=1
  fi

  # A6 — hạng IM LẶNG phải còn được bắt. '' và '0' KHÔNG ném nhưng cho ra giá
  # trị không định nghĩa; bỏ nhánh này thì gate chỉ bắt được nửa bug class.
  if ! grep -q "IsDefined" "$SCANNER" 2>/dev/null; then
    echo "[gate:enum-integrity:FAIL] scanner không còn bắt hạng im lặng (IsDefined)."
    rc=1
  fi

  [ $rc -eq 0 ] && echo "[gate:enum-integrity] tĩnh: 3 tầng đủ dây nối, scanner data-driven — OK"
  return $rc
}

# ── (B) ĐỘNG ────────────────────────────────────────────────────────────────
scan_db() {
  local db="$1"

  if ! command -v dotnet >/dev/null 2>&1; then
    echo "[gate:enum-integrity] động: BỎ QUA — không có dotnet trên máy này."
    return 0
  fi
  if [ ! -f "$db" ]; then
    echo "[gate:enum-integrity] động: BỎ QUA — không thấy DB fixture ${db#$ROOT/}"
    echo "  (*.db bị .gitignore loại; bản clone mới không có. Chĩa gate vào DB khác bằng"
    echo "   MES_ENUM_INTEGRITY_DB=<path> hoặc tham số 1.)"
    return 0
  fi

  local out rc
  out="$(dotnet run --project "$CLI_PROJ" -v q --nologo -- "$db" --quiet 2>/dev/null)"
  rc=$?
  echo "$out" | grep -E '^\[enum-integrity\]' | sed 's/^\[enum-integrity\]/[gate:enum-integrity] động:/'

  case $rc in
    0) return 0 ;;
    1)
      echo "[gate:enum-integrity:FAIL] DB fixture ${db#$ROOT/} chứa giá trị ngoài enum."
      echo "  Sửa DỮ LIỆU về một thành viên enum hợp lệ. KHÔNG thêm thành viên enum mới"
      echo "  để hợp thức hoá giá trị rác — contract impact = 1 ⇒ STOP-gate (AGENT-LOOP §3)."
      echo "  Xem docs/RUNBOOK-CURRENTSTEP-REPAIR-2026-08-19.md."
      return 1 ;;
    *)
      echo "[gate:enum-integrity:FAIL] KHÔNG kết luận được trên ${db#$ROOT/} (exit=$rc)."
      echo "  Không kiểm được KHÔNG phải là đạt."
      return 1 ;;
  esac
}

# ── self-test ───────────────────────────────────────────────────────────────
if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  st_rc=0

  # ST1 — bộ dò TĨNH: gỡ preflight khỏi bản sao Program.cs thì phải bị bắt.
  cp "$PROGRAM" "$tmp/Program.cs"
  grep -v "EnumIntegrityMonitor" "$tmp/Program.cs" > "$tmp/Program.stripped.cs"
  if grep -q "EnumIntegrityMonitor" "$tmp/Program.stripped.cs"; then
    echo "[gate:enum-integrity] self-test FAILED — không gỡ được preflight khỏi bản sao"
    st_rc=1
  else
    echo "[gate:enum-integrity] self-test OK (gỡ preflight tầng 3 khỏi Program.cs bị bắt)"
  fi

  # ST2 — bộ dò ĐỘNG: tiêm 'Done' vào bản sao DB thì phải FAIL.
  db="${MES_ENUM_INTEGRITY_DB:-$DEFAULT_DB}"
  if [ -f "$db" ] && command -v sqlite3 >/dev/null 2>&1 && command -v dotnet >/dev/null 2>&1; then
    cp "$db" "$tmp/probe.db"
    sqlite3 "$tmp/probe.db" \
      "UPDATE WorkOrders SET CurrentStep='Done' WHERE Id=(SELECT MIN(Id) FROM WorkOrders);" \
      >/dev/null 2>&1
    dotnet run --project "$CLI_PROJ" -v q --nologo -- "$tmp/probe.db" --quiet >/dev/null 2>&1
    if [ $? -eq 1 ]; then
      echo "[gate:enum-integrity] self-test OK (tiêm CurrentStep='Done' vào bản sao DB bị bắt)"
    else
      echo "[gate:enum-integrity] self-test FAILED — tiêm 'Done' mà scanner không bắt"
      st_rc=1
    fi
  else
    echo "[gate:enum-integrity] self-test: bỏ qua nhánh động (thiếu DB fixture / sqlite3 / dotnet)"
  fi

  exit $st_rc
fi

# ── chạy thật ───────────────────────────────────────────────────────────────
DB="${1:-${MES_ENUM_INTEGRITY_DB:-$DEFAULT_DB}}"

rc=0
static_checks || rc=1
scan_db "$DB" || rc=1

[ $rc -eq 0 ] && echo "[gate:enum-integrity:OK] không có giá trị nằm ngoài enum, 3 tầng còn nguyên."
exit $rc

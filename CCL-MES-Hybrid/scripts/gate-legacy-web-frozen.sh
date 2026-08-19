#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Đợt 1 · C2 — app Blazor Server legacy (:5050) ĐÃ ĐÓNG BĂNG ngày 2026-08-19.
#
# Gate này canh hai đường hồi quy, cả hai đều HARD FAIL:
#
#   (1) i18n — không được thêm key MỚI vào src/CCL.MES.Web/Resources/
#       SharedResource[.vi].resx. App cũ đóng băng ⇒ mọi chuỗi hiển thị mới
#       phải vào TranslationCatalog của Hybrid (skill cmes-i18n-parity).
#       Không phải ratchet đếm số: so nguyên TẬP KEY với baseline đã chốt, nên
#       "xoá 1 thêm 1" vẫn bị bắt. Xoá key thì được — đó là đi đúng hướng.
#
#   (2) launcher — START_SERVER.command / .bat phải còn cổng chặn
#       MES_LEGACY_WEB_FORCE=1, và cổng đó phải nằm TRƯỚC lệnh `dotnet run`.
#       Chạy nhầm phải DỪNG, không được âm thầm mở cổng 5050 ra LAN nhà máy.
#       Kiểm cả tĩnh (anchor + điều kiện) lẫn động (chạy thật, đòi rc=2).
#
# Vì sao HARD FAIL chứ không ratchet: đóng băng là quyết định đã duyệt của
# Henry (2026-08-19, xác nhận không còn ai ở nhà máy dùng :5050), không phải
# một khoản nợ kỹ thuật đang trả dần. Muốn mở lại ⇒ STOP-gate, hỏi Henry.
#
# Tài liệu: CCL-MES-Hybrid/docs/CUTOVER-LEGACY-WEB-FREEZE-2026-08-19.md
#
# Usage:
#   bash CCL-MES-Hybrid/scripts/gate-legacy-web-frozen.sh
#   bash CCL-MES-Hybrid/scripts/gate-legacy-web-frozen.sh --self-test
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

# Đếm thật ngày 2026-08-19 trên commit 75a6fb7 (tag legacy-web-last-serving):
#   grep -c '<data name=' src/CCL.MES.Web/Resources/SharedResource.resx    → 1045
#   grep -c '<data name=' src/CCL.MES.Web/Resources/SharedResource.vi.resx → 1045
# Hai tập key TRÙNG KHỚP hoàn toàn (comm hai chiều rỗng, không key trùng lặp).
BASELINE_RESX_KEYS=1045

here="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$here/../.." && pwd)"
WEB="$ROOT/src/CCL.MES.Web"
RESX_EN="$WEB/Resources/SharedResource.resx"
RESX_VI="$WEB/Resources/SharedResource.vi.resx"
BASE_KEYS="$here/baselines/legacy-web-resx-keys.txt"
LAUNCH_SH="$ROOT/START_SERVER.command"
LAUNCH_BAT="$ROOT/START_SERVER.bat"

ANCHOR='GATE-ANCHOR: legacy-web-force-guard'
FORCE_VAR='MES_LEGACY_WEB_FORCE'

# Trích key từ một .resx, sort ổn định (LC_ALL=C để khớp baseline).
extract_keys() { sed -n 's/.*<data name="\([^"]*\)".*/\1/p' "$1" | LC_ALL=C sort -u; }

# ── self-test: chứng minh detector thật sự bắt được key mới ──────────────────
if [ "${1:-}" = "--self-test" ]; then
  [ -f "$RESX_EN" ] || { echo "[gate:legacy-frozen] self-test SKIP — không thấy $RESX_EN"; exit 0; }
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  cp "$RESX_EN" "$tmp/x.resx"
  before="$(extract_keys "$tmp/x.resx" | wc -l | tr -d ' ')"
  # chèn đúng một key mới ngay trước </root>
  sed -i.bak 's#</root>#  <data name="selftest.injected" xml:space="preserve"><value>x</value></data>\n</root>#' "$tmp/x.resx"
  after="$(extract_keys "$tmp/x.resx" | wc -l | tr -d ' ')"
  added="$(extract_keys "$tmp/x.resx" | LC_ALL=C comm -13 "$BASE_KEYS" - | tr '\n' ' ')"
  if [ "$after" -gt "$before" ] && [ -n "$added" ]; then
    echo "[gate:legacy-frozen] self-test OK — key mới bị bắt: $before -> $after, added = $added"
    exit 0
  fi
  echo "[gate:legacy-frozen] self-test FAILED — detector không bắt được key mới"
  exit 1
fi

rc=0

# ── 1/2 · i18n — .resx đóng băng, không nhận key mới ─────────────────────────
if [ ! -d "$WEB" ]; then
  echo "[gate:legacy-frozen] ⊘ src/CCL.MES.Web đã bị gỡ khỏi cây — đợt xoá thật đã chạy."
  echo "                     Gỡ luôn gate này khỏi gate-all.sh trong cùng PR đó."
else
  if [ ! -f "$RESX_EN" ] || [ ! -f "$RESX_VI" ]; then
    echo "[gate:legacy-frozen:FAIL] thiếu SharedResource.resx hoặc SharedResource.vi.resx"
    echo "  Đóng băng nghĩa là GIỮ NGUYÊN, không xoá lẻ từng tệp. Khôi phục từ tag legacy-web-last-serving."
    rc=1
  elif [ ! -f "$BASE_KEYS" ]; then
    echo "[gate:legacy-frozen:FAIL] thiếu baseline $BASE_KEYS"
    rc=1
  else
    tmpk="$(mktemp -d)"; trap 'rm -rf "$tmpk"' EXIT
    extract_keys "$RESX_EN" > "$tmpk/en"
    extract_keys "$RESX_VI" > "$tmpk/vi"
    n_en="$(wc -l < "$tmpk/en" | tr -d ' ')"
    n_vi="$(wc -l < "$tmpk/vi" | tr -d ' ')"
    echo "[gate:legacy-frozen] key .resx      EN=$n_en  VI=$n_vi   (baseline đóng băng $BASELINE_RESX_KEYS)"

    added_en="$(LC_ALL=C comm -13 "$BASE_KEYS" "$tmpk/en")"
    added_vi="$(LC_ALL=C comm -13 "$BASE_KEYS" "$tmpk/vi")"
    if [ -n "$added_en" ] || [ -n "$added_vi" ]; then
      echo "[gate:legacy-frozen:FAIL] key i18n MỚI trong .resx của app đã đóng băng:"
      [ -n "$added_en" ] && echo "$added_en" | sed 's/^/    + EN  /'
      [ -n "$added_vi" ] && echo "$added_vi" | sed 's/^/    + VI  /'
      echo "  App :5050 ngừng phục vụ từ 2026-08-19 — chuỗi mới KHÔNG bao giờ hiển thị ở đó."
      echo "  Đưa chuỗi vào CCL.MES.Hybrid.Client/Localization/TranslationCatalog.*.cs (đủ VI + EN),"
      echo "  xem skill cmes-i18n-parity. Rồi hoàn nguyên .resx."
      rc=1
    fi

    if [ "$n_en" -gt "$BASELINE_RESX_KEYS" ] || [ "$n_vi" -gt "$BASELINE_RESX_KEYS" ]; then
      echo "[gate:legacy-frozen:FAIL] số key .resx vượt baseline $BASELINE_RESX_KEYS (EN=$n_en VI=$n_vi)."
      rc=1
    fi

    only_en="$(LC_ALL=C comm -23 "$tmpk/en" "$tmpk/vi")"
    only_vi="$(LC_ALL=C comm -13 "$tmpk/en" "$tmpk/vi")"
    if [ -n "$only_en" ] || [ -n "$only_vi" ]; then
      echo "[gate:legacy-frozen:FAIL] .resx EN/VI lệch nhau — bản đóng băng phải giữ nguyên parity:"
      [ -n "$only_en" ] && echo "$only_en" | sed 's/^/    chỉ EN  /'
      [ -n "$only_vi" ] && echo "$only_vi" | sed 's/^/    chỉ VI  /'
      rc=1
    fi

    if [ "$n_en" -lt "$BASELINE_RESX_KEYS" ]; then
      echo "[gate:legacy-frozen] ℹ  $((BASELINE_RESX_KEYS - n_en)) key đã bị xoá khỏi .resx — hướng đúng."
      echo "                     Hạ BASELINE_RESX_KEYS + cập nhật $BASE_KEYS trong CÙNG PR đó."
    fi
  fi
fi

# ── 2/2 · launcher — cổng force còn nguyên, nằm trước `dotnet run` ───────────
check_launcher_static() {
  local f="$1" label="$2" runpat="$3" cond="$4"
  if [ ! -f "$f" ]; then
    echo "[gate:legacy-frozen:FAIL] thiếu launcher $label"
    return 1
  fi
  local lrc=0
  if ! grep -qF "$ANCHOR" "$f"; then
    echo "[gate:legacy-frozen:FAIL] $label mất anchor '$ANCHOR' — cổng đóng băng đã bị gỡ."
    lrc=1
  fi
  if ! grep -qF "$cond" "$f"; then
    echo "[gate:legacy-frozen:FAIL] $label mất điều kiện chặn: $cond"
    echo "  Launcher không được chạy khi thiếu $FORCE_VAR=1."
    lrc=1
  fi
  local a_line r_line
  a_line="$(grep -nF "$ANCHOR" "$f" | head -1 | cut -d: -f1)"
  r_line="$(grep -nF "$runpat" "$f" | head -1 | cut -d: -f1)"
  if [ -n "$r_line" ]; then
    if [ -z "$a_line" ] || [ "$a_line" -ge "$r_line" ]; then
      echo "[gate:legacy-frozen:FAIL] $label — cổng force (dòng ${a_line:-none}) không nằm trước"
      echo "  lệnh khởi động '$runpat' (dòng $r_line). Cổng đặt sau = vô nghĩa."
      lrc=1
    fi
  fi
  return $lrc
}

check_launcher_static "$LAUNCH_SH"  "START_SERVER.command" \
  'dotnet run --project src/CCL.MES.Web' \
  '"${MES_LEGACY_WEB_FORCE:-}" != "1"' || rc=1
check_launcher_static "$LAUNCH_BAT" "START_SERVER.bat" \
  'dotnet run --project src\CCL.MES.Web' \
  'if not "%MES_LEGACY_WEB_FORCE%"=="1"' || rc=1

# Kiểm ĐỘNG: chạy thật launcher macOS KHÔNG có biến — phải dừng với rc=2 và in
# cảnh báo song ngữ. </dev/null để `read` bị bỏ qua (khối guard dùng [ -t 0 ]).
#
# BA LỚP CHẶN — bắt buộc, đã trả giá để biết (xem LESSONS-LEARNED L48):
#   1. MES_LEGACY_WEB_DRYRUN=1 — nếu ai đó vô hiệu hoá cổng force, launcher
#      dừng ở nhánh dry-run thay vì `dotnet run`.
#   2. MES_DATA_DIR=<tmp>       — kể cả boot lọt vẫn KHÔNG chạm data/ccl_mes.db.
#   3. watchdog 20s             — treo = FAIL, không phải gate ngồi chờ mãi.
# Bỏ bất kỳ lớp nào: gate tự nó khởi động app đã đóng băng lên LIVE DB.
if [ -f "$LAUNCH_SH" ]; then
  probe="$(mktemp -d)"
  ( env -u MES_LEGACY_WEB_FORCE MES_LEGACY_WEB_DRYRUN=1 MES_DATA_DIR="$probe/data" \
        bash "$LAUNCH_SH" </dev/null >"$probe/out" 2>&1; echo $? >"$probe/rc" ) &
  kid=$!
  for _ in $(seq 1 40); do
    kill -0 "$kid" 2>/dev/null || break
    sleep 0.5
  done
  if kill -0 "$kid" 2>/dev/null; then
    pkill -TERM -P "$kid" 2>/dev/null; kill -TERM "$kid" 2>/dev/null; sleep 1
    pkill -KILL -P "$kid" 2>/dev/null; kill -KILL "$kid" 2>/dev/null
    echo '[gate:legacy-frozen:FAIL] START_SERVER.command KHÔNG dừng trong 20s khi thiếu MES_LEGACY_WEB_FORCE.'
    echo '  Launcher đã đóng băng phải thoát ngay, không được đi tiếp tới `dotnet run`.'
    rc=1
  fi
  wait "$kid" 2>/dev/null
  out="$(cat "$probe/out" 2>/dev/null)"
  lrc="$(cat "$probe/rc" 2>/dev/null || echo 99)"
  rm -rf "$probe"
  if [ "$lrc" -ne 2 ]; then
    echo "[gate:legacy-frozen:FAIL] chạy START_SERVER.command KHÔNG có $FORCE_VAR trả rc=$lrc, cần rc=2."
    rc=1
  fi
  if ! printf '%s' "$out" | grep -q 'NGỪNG PHỤC VỤ'; then
    echo "[gate:legacy-frozen:FAIL] cảnh báo tiếng Việt biến mất khỏi START_SERVER.command."
    rc=1
  fi
  if ! printf '%s' "$out" | grep -q 'RETIRED'; then
    echo "[gate:legacy-frozen:FAIL] cảnh báo tiếng Anh biến mất khỏi START_SERVER.command."
    rc=1
  fi
  if [ "$rc" -eq 0 ]; then
    echo "[gate:legacy-frozen] launcher      chạy không biến → rc=2 + cảnh báo VI/EN ✓"
  fi
fi

if [ $rc -eq 0 ]; then
  echo "[gate:legacy-frozen:OK] app :5050 vẫn đóng băng — .resx không nhận key mới, launcher vẫn chặn."
fi
exit $rc

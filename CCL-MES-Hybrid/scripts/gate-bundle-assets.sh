#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# gate-bundle-assets — bản .app đem chạy phải mang ĐÚNG stylesheet của repo.
# Chặn cái bẫy "DLL mới, tài nguyên tĩnh cũ": bundle mở được, không lỗi, không
# cảnh báo — chỉ có giao diện vỡ vì markup mới không tìm thấy class nào khớp.
#
# SỰ CỐ 2026-09-11:
#   Thiệp báo dashboard IQC và NG/claim hỏng: Pareto mất cột, xu hướng theo
#   tháng đổ thành một cột số trần, bảng NCC thành văn bản thô.
#
#   Đo trên bundle đang chạy:
#     app.css trong bundle  362.403 byte   ·  app.css trong nguồn  428.542 byte
#     .iqc-ng-tbar · .iqc-ng-rank · .iqc-v2-kpi   → KHÔNG có trong bundle
#   CSS của hai dashboard ấy vào repo 09-09 (56ace7e, 2fdaeaa); CSS trong bundle
#   dừng ở 05-09. Bốn ngày lệch.
#
#   RCA: `dotnet build -c Release -f net10.0-maccatalyst` KHÔNG gom static web
#   asset của Razor class library vào .app — nó chỉ cập nhật DLL. Lệnh dựng đúng
#   là `dotnet publish` (skill cmes-macos-ship). `-t:Rebuild` không cứu; xoá
#   bin+obj rồi build lại cũng không (đã thử cả hai, wwwroot vẫn đúng 1 file).
#
#   Chừng nào DLL và CSS cùng cũ thì chẳng ai thấy gì. Vừa build lại DLL là
#   markup mới gặp stylesheet cũ ⇒ vỡ. Nói cách khác: build một nửa nguy hiểm
#   hơn không build.
#
# VÌ SAO SO NỘI DUNG, KHÔNG SO GIỜ:
#   Bản nháp đầu của gate này so mtime (CSS phải mới bằng DLL) và **báo oan
#   ngay bản publish tốt**: khâu copy giữ nguyên mtime của file nguồn, nên CSS
#   trong bundle lành vẫn "cũ hơn" DLL vừa biên dịch. Giờ giấc không nói lên
#   điều gì ở đây; chỉ sha256 mới nói.
#
# PHẠM VI — cố ý hẹp, để gate không kêu oan rồi bị ngó lơ:
#   · CHỈ soi bundle publish `bin/Release/net10.0-maccatalyst/CCL MES.app`.
#     Đó là bản đem chạy và đem ship.
#   · Thư mục RID (`maccatalyst-arm64|x64/`) là sản phẩm phụ của `dotnet build`,
#     thiếu wwwroot là ĐÚNG THIẾT KẾ — nhưng nó mở được, và tôi đã mở nhầm đúng
#     nó ngày 11-09. Nên vẫn in cảnh báo, không tính là hỏng.
#   · Bỏ qua Debug: không ai đem Debug ra xưởng.
#
# Ba trạng thái, cố ý không phải hai:
#   exit 0  sạch (hoặc chưa publish lần nào — nói rõ là BỎ QUA)
#   exit 1  bundle publish mang stylesheet KHÁC nguồn
#   exit 2  không kết luận được. "Không kiểm được" KHÔNG phải "đã kiểm và sạch".
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SRC_DIR="$ROOT/CCL-MES-Hybrid/src/CCL.MES.Hybrid.Razor/wwwroot/css"
BIN="$ROOT/CCL-MES-Hybrid/src/CCL.MES.Hybrid/bin"
PUB="$BIN/Release/net10.0-maccatalyst/CCL MES.app"
REL="_content/CCL.MES.Hybrid.Razor/css"
SHEETS=(app.css ix.css)

sha() { shasum -a 256 "$1" 2>/dev/null | cut -d' ' -f1; }

# Soi một bundle publish. In chẩn đoán; trả 0 sạch / 1 lệch.
check_publish() {
  local app="$1" bad=0 b s
  for f in "${SHEETS[@]}"; do
    b="$app/Contents/Resources/wwwroot/$REL/$f"
    s="$SRC_DIR/$f"
    [ -f "$s" ] || continue
    if [ ! -f "$b" ]; then
      echo "  ✗ thiếu hẳn $REL/$f — bundle dựng bằng \`dotnet build\`, không phải \`publish\`."
      bad=1; continue
    fi
    if [ "$(sha "$b")" != "$(sha "$s")" ]; then
      echo "  ✗ $f LỆCH nguồn — bundle $(wc -c <"$b" | tr -d ' ') byte / nguồn $(wc -c <"$s" | tr -d ' ') byte"
      bad=1; continue
    fi
    echo "  ✓ $f khớp nguồn"
  done
  return "$bad"
}

# ── self-test: tiêm đúng các ca hỏng, gate phải bắt được ────────────────────
if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  fail=0
  SRC_DIR="$tmp/src"; mkdir -p "$SRC_DIR"
  for f in "${SHEETS[@]}"; do echo "/* nguồn thật */" > "$SRC_DIR/$f"; done

  mk() { mkdir -p "$1/Contents/Resources/wwwroot/$REL"; }

  # ❶ bundle thiếu hẳn stylesheet (ca `dotnet build`)
  mk "$tmp/a.app"
  check_publish "$tmp/a.app" >/dev/null 2>&1 \
    && { echo "[gate:bundle-assets] self-test FAILED — không bắt được bundle thiếu CSS"; fail=1; }

  # ❷ bundle có stylesheet nhưng nội dung CŨ (ca đã bẫy thật ngày 11-09)
  mk "$tmp/b.app"
  for f in "${SHEETS[@]}"; do echo "/* bản 05-09 */" > "$tmp/b.app/Contents/Resources/wwwroot/$REL/$f"; done
  check_publish "$tmp/b.app" >/dev/null 2>&1 \
    && { echo "[gate:bundle-assets] self-test FAILED — không bắt được CSS lệch nội dung"; fail=1; }

  # ❸ bundle lành — phải ĐẠT, kể cả khi mtime cũ hơn (chống bản nháp báo oan)
  mk "$tmp/c.app"
  for f in "${SHEETS[@]}"; do
    cp "$SRC_DIR/$f" "$tmp/c.app/Contents/Resources/wwwroot/$REL/$f"
    touch -t 202001010000 "$tmp/c.app/Contents/Resources/wwwroot/$REL/$f"
  done
  check_publish "$tmp/c.app" >/dev/null 2>&1 \
    || { echo "[gate:bundle-assets] self-test FAILED — báo oan bundle lành (mtime cũ vẫn phải đạt)"; fail=1; }

  [ "$fail" -eq 0 ] && echo "[gate:bundle-assets] self-test OK — bắt 2 ca hỏng, không kêu oan ca lành."
  exit "$fail"
fi

# ── chạy thật ───────────────────────────────────────────────────────────────
command -v shasum >/dev/null 2>&1 || { echo "[gate:bundle-assets:FAIL] không có shasum — không kiểm được."; exit 2; }
[ -f "$SRC_DIR/app.css" ] || { echo "[gate:bundle-assets:FAIL] không thấy app.css nguồn — không kiểm được."; exit 2; }

# Cảnh báo (không tính hỏng): bundle RID của `dotnet build` — mở được mà thiếu wwwroot.
while IFS= read -r rid; do
  [ -f "$rid/Contents/Resources/wwwroot/$REL/app.css" ] && continue
  echo "[gate:bundle-assets] ⚠ ĐỪNG MỞ ${rid#$ROOT/}"
  echo "    đây là sản phẩm phụ của \`dotnet build\`, thiếu wwwroot — mở nó là thấy giao diện vỡ."
done < <(find "$BIN/Release" -maxdepth 3 -path "*maccatalyst-*" -name "*.app" -type d 2>/dev/null)

if [ ! -d "$PUB" ]; then
  echo "[gate:bundle-assets] BỎ QUA — chưa publish bản Release nào."
  echo "  (dựng bằng: dotnet publish CCL-MES-Hybrid/src/CCL.MES.Hybrid/CCL.MES.Hybrid.csproj -c Release -f net10.0-maccatalyst)"
  exit 0
fi

echo "[gate:bundle-assets] soi ${PUB#$ROOT/}"
if ! check_publish "$PUB"; then
  echo "[gate:bundle-assets:FAIL] bundle đem chạy mang stylesheet KHÁC repo."
  echo "  Markup trong DLL và class trong CSS lệch nhau = giao diện vỡ mà không một dòng lỗi nào."
  echo "  Dựng lại bằng lệnh ĐÚNG (\`dotnet build\` KHÔNG gom static web asset):"
  echo "    dotnet publish CCL-MES-Hybrid/src/CCL.MES.Hybrid/CCL.MES.Hybrid.csproj -c Release -f net10.0-maccatalyst"
  exit 1
fi

echo "[gate:bundle-assets:OK] bundle publish mang đúng stylesheet của repo."
exit 0

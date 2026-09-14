#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# gate-422-error-shape — mọi đường ghi phải ĐỌC ĐÚNG hình dạng thân 422, nếu
# không người đứng máy mất sạch lý do thật và chỉ còn câu "báo IT".
#
# LUẬT (một câu):
#   `WoMutationControllerBase.Invalid()` trả `ApiError {code,message}` cho 422,
#   còn 200/409 trả envelope `{ok,errorCode}`. HAI HÌNH DẠNG KHÁC NHAU trên
#   cùng một endpoint. Hàm nào nhận 422 mà không đọc qua `ApiError` thì
#   `ErrorCode` ra null ⇒ UI hiện "Máy chủ trả về Ok=false nhưng không có mã
#   lỗi — báo IT" cho MỌI lỗi nghiệp vụ.
#
# SỰ CỐ 2026-09-14:
#   Thiệp gõ sai mật khẩu ký IPQC và được bảo đi "báo IT". Server đã nói rõ
#   `ipqc.signature_invalid` và đã ghi đúng dòng audit WO_IPQC_SIGN_DENIED —
#   lỗi nằm TRỌN ở client. Đo được: 3/5 helper thiếu ánh xạ (IPQC · vật tư
#   IPQC · FQC/OQC), tức mọi lỗi 422 trên TẤT CẢ màn QC đều câm.
#
#   Đây là **L54 tái phát**. L54 đã sửa đúng ca này cho SettingChecks từ trước,
#   chú thích còn ghi nguyên văn "not 'no error code — report to IT'" — nhưng
#   sửa một chỗ rồi để đó. Lesson không có máy canh thì chỉ là văn xuôi: bốn
#   helper viết sau cùng cứ chép lại đúng cái sai.
#
# Ba trạng thái, cố ý không phải hai:
#   exit 0  mọi helper đọc đúng
#   exit 1  có helper nhận 422 mà không qua ApiError
#   exit 2  không kết luận được (không thấy file nguồn)
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SRC="$ROOT/CCL-MES-Hybrid/src/CCL.MES.Hybrid.Client/CclApiClient.cs"

scan() {  # $1 = file; in một dòng "TÊN_HÀM|OK|BAD" cho mỗi hàm nhận 422
  python3 - "$1" <<'PY'
import re, sys
src = open(sys.argv[1]).read().splitlines()
# Mỗi khai báo method (có thân) mở một vùng; vùng kết thúc ở khai báo kế tiếp.
decl = re.compile(r'^\s*(?:public|private|protected|internal)\s+.*\b(\w+)\s*\(')
starts = [(i, m.group(1)) for i, l in enumerate(src) if (m := decl.match(l))]
for idx, (i, name) in enumerate(starts):
    end = starts[idx + 1][0] if idx + 1 < len(starts) else len(src)
    body = "\n".join(src[i:end])
    if 'UnprocessableEntity' not in body:
        continue
    ok = 'ReadFromJsonAsync<ApiError>' in body
    print(f"{name}|{'OK' if ok else 'BAD'}|{i + 1}")
PY
}

# ── self-test: tiêm một helper thiếu ánh xạ, gate phải bắt ──────────────────
if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  fail=0

  cat > "$tmp/bad.cs" <<'EOF'
    private async Task<XSetResponse> SendXMutationAsync(HttpMethod m, string p, CancellationToken ct)
    {
        using var resp = await _http.SendAsync(msg, ct);
        if (resp.StatusCode == HttpStatusCode.OK
            || resp.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var body = await resp.Content.ReadFromJsonAsync<XSetResponse>(cancellationToken: ct);
            return body ?? new XSetResponse { Ok = false };
        }
        return await ReadAsAsync<XSetResponse>(resp, ct);
    }
EOF
  scan "$tmp/bad.cs" | grep -q "|BAD|" \
    || { echo "[gate:422-error-shape] self-test FAILED — không bắt được helper thiếu ánh xạ"; fail=1; }

  cat > "$tmp/good.cs" <<'EOF'
    private async Task<XSetResponse> SendXMutationAsync(HttpMethod m, string p, CancellationToken ct)
    {
        using var resp = await _http.SendAsync(msg, ct);
        if (resp.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var err = await resp.Content.ReadFromJsonAsync<ApiError>(cancellationToken: ct);
            return new XSetResponse { Ok = false, ErrorCode = err?.Code ?? "http.422" };
        }
        if (resp.StatusCode == HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadFromJsonAsync<XSetResponse>(cancellationToken: ct);
            return body ?? new XSetResponse { Ok = false };
        }
        return await ReadAsAsync<XSetResponse>(resp, ct);
    }
EOF
  scan "$tmp/good.cs" | grep -q "|BAD|" \
    && { echo "[gate:422-error-shape] self-test FAILED — báo oan helper đã ánh xạ đúng"; fail=1; }

  [ "$fail" -eq 0 ] && echo "[gate:422-error-shape] self-test OK — bắt helper thiếu, không kêu oan helper đúng."
  exit "$fail"
fi

# ── chạy thật ───────────────────────────────────────────────────────────────
[ -f "$SRC" ] || { echo "[gate:422-error-shape:FAIL] không thấy CclApiClient.cs — không kiểm được."; exit 2; }
command -v python3 >/dev/null 2>&1 || { echo "[gate:422-error-shape:FAIL] không có python3."; exit 2; }

rows="$(scan "$SRC")"
[ -n "$rows" ] || { echo "[gate:422-error-shape:FAIL] không thấy hàm nào xử lý 422 — nghi parser hỏng."; exit 2; }

bad=0; tot=0
while IFS='|' read -r name st line; do
  tot=$((tot + 1))
  [ "$st" = "OK" ] && continue
  bad=$((bad + 1))
  echo "  ✗ $name  (CclApiClient.cs:$line)"
done <<< "$rows"

if [ "$bad" -gt 0 ]; then
  echo "[gate:422-error-shape:FAIL] $bad/$tot đường ghi đọc SAI thân 422."
  echo "  Thân 422 là ApiError {code,message}, KHÔNG phải envelope {ok,errorCode}."
  echo "  Đọc nhầm ⇒ ErrorCode null ⇒ màn hình nuốt lý do thật và chỉ nói \"báo IT\"."
  echo "  Mẫu đúng (xem SendSettingMutationAsync, L54):"
  echo "    if (resp.StatusCode == HttpStatusCode.UnprocessableEntity)"
  echo "    {"
  echo "        var err = await resp.Content.ReadFromJsonAsync<ApiError>(cancellationToken: ct);"
  echo "        return new XSetResponse { Ok = false,"
  echo "            ErrorCode = string.IsNullOrEmpty(err?.Code) ? \"http.422\" : err.Code };"
  echo "    }"
  exit 1
fi

echo "[gate:422-error-shape:OK] $tot/$tot đường ghi đọc đúng thân 422."
exit 0

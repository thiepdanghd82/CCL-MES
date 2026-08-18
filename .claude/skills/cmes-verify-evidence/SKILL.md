---
name: cmes-verify-evidence
description: >
  Định nghĩa "xong" của CCL-MES — pha AUDIT (gate tĩnh) và pha VERIFY (chạy
  thật, dán output). Dùng trước khi tuyên bố bất kỳ thay đổi nào hoàn tất,
  khi review PR, hoặc khi debug. Không có output thật = chưa xong.
---

# CMES verify — bằng chứng, không phải lời khai

**Rule (enforced):** "đã test rồi" không phải bằng chứng. Bằng chứng là
**lệnh + output của lệnh đó**, dán nguyên văn.

## Hai pha khác nhau — đừng gộp

| | Pha 4 AUDIT | Pha 5 VERIFY |
|---|---|---|
| Bản chất | tĩnh — grep, gate, ratchet | động — chạy thật |
| Lệnh | `bash CCL-MES-Hybrid/scripts/gate-all.sh` | test / curl / sqlite3 / screenshot |
| Trả lời | "có vi phạm luật đã biết không?" | "nó có thật sự chạy không?" |

Gate xanh **không** chứng minh tính năng chạy. Test xanh **không** chứng minh
không vi phạm luật kiến trúc. Cần **cả hai**.

## Bảng bằng chứng theo loại thay đổi

| Loại | Bằng chứng bắt buộc |
|---|---|
| Schema | `.schema <bảng>` trước/sau · rowcount · SHA256 · `__EFMigrationsHistory` |
| State machine | output parity test · bảng `from → to → guard → error` |
| API | `curl` thật: happy path **và** 403 **và** 409, kèm status + body |
| UI | screenshot **2 density** (`office` + `shopfloor`) |
| Gate mới | chuỗi **PASS → FAIL (inject vi phạm) → PASS** |
| Concurrency | soak N thread → đúng 1 winner, N-1 × 409 |
| Debug | lệnh **chứng minh** nguyên nhân + output của nó |

## RCA proven — luật cho pha debug

1. Nêu giả thuyết ("binary cũ còn chạy").
2. Tìm **lệnh chứng minh giả thuyết đúng hoặc sai**.
3. Chạy, dán output thật.
4. **Chỉ khi đó** mới viết fix.

Mẫu đã chứng minh hiệu quả (sự cố Settings 404):
```
$ lsof -nP -iTCP:5100 -sTCP:LISTEN
COMMAND     PID    USER   FD   TYPE  ... 127.0.0.1:5100 (LISTEN)
$ ps aux | grep CCL.MES.Api | grep -v grep
thiepdt 81851 ... 1:38PM   ← process khởi động TRƯỚC khi commit fix được push
```
Giả thuyết trở thành **proven** vì giờ khởi động process có trước commit.
Bản RCA hoàn chỉnh: 4 đoạn + output. Không có chữ "most likely" nào.

## Bẫy đã trả giá khi verify

- **Binary cũ (L7):** luôn `lsof` cổng + so giờ khởi động process với giờ
  commit trước khi kết luận "code không chạy".
- **Sai DB:** script tự pin DB của nó, in `[ctx] DB=` ở đầu (R7). Verify trên
  DB khác với DB app đang dùng = verify vô nghĩa.
- **Im lặng = chưa chạy (S12):** script phải in `[N/total]` mỗi bước và
  **luôn** in SUMMARY. Script chạy xong không in gì thì không ai biết nó
  có chạy không.
- **Giữ artifact khi FAIL (S10):** đừng xoá `api.log` / TMP_DIR trước khi
  người vận hành kịp đọc.
- **WebView stale (L36):** resize cửa sổ MAUI bằng script cho viewport số cũ
  → dải trống giả. Kéo tay thật để xác nhận.

## Checklist trước khi nói "xong"

- [ ] `bash CCL-MES-Hybrid/scripts/gate-all.sh` — dán SUMMARY
- [ ] Test của vùng vừa sửa chạy thật — dán dòng kết quả
- [ ] Bằng chứng đúng loại theo bảng trên đã dán
- [ ] Đã kiểm nhánh FAIL, không chỉ nhánh thành công
- [ ] Nếu là bug class mới ≥2h ⇒ lesson + cơ chế chặn cùng PR

## Do NOT

- Viết "verified" mà không có output kèm theo.
- Kết luận từ việc đọc code thay vì chạy code.
- Coi CI xanh là đủ cho thay đổi UI (CI không nhìn màn hình).

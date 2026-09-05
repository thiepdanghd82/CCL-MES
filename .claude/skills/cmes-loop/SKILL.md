---
name: cmes-loop
description: >
  Vòng lặp thực thi bắt buộc của CCL-MES — ANALYZE → SELECT → EXECUTE →
  AUDIT → VERIFY → LEARN. Dùng ở ĐẦU mọi phiên làm việc trên repo này để
  phân loại công việc, chọn agent + skill guardian đúng, và biết bằng
  chứng nào mới tính là "xong". Nạp trước cả khi đọc CLAUDE.md chi tiết.
---

# CMES loop — vòng lặp thực thi có kiểm chứng

**Hợp đồng đầy đủ:** `CCL-MES-Hybrid/docs/AGENT-LOOP.md`. Skill này là bản
thao tác nhanh; khi hai bên mâu thuẫn, AGENT-LOOP.md thắng.

## Bước 0 — phân loại (30 giây, làm trước mọi thứ)

Chốt **đúng một** work-class rồi nạp skill của nó:

| Chạm vào… | Work-class | Skill BẮT BUỘC nạp thêm |
|---|---|---|
| `Entities/` · `Migrations/` · `DbContext` | W1 schema | `cmes-migration-abc` |
| `WorkOrderStateMachine` · `MesPhase` · `LegPhase` · `/advance` | W2 state | `cmes-state-contract` |
| `Controllers/` · DTO · route | W3 api | `cmes-thin-controller` |
| `CheckItemLibrary` · resolver · ngưỡng · chữ ký · freeze | W4 quality | `cmes-audit-emit` |
| `.razor` · `app.css` · layout · grid | W5 ui | `cmes-design-tokens` |
| policy · role · `AuthorizeView` | W6 rbac | `cmes-rbac-matrix` |
| **bất kỳ chuỗi hiển thị nào** | W7 i18n | `cmes-i18n-parity` ← luôn kèm |
| import IFS · outbox · idempotency | W8 integration | `cmes-migration-abc` |
| "không chạy" · 404 · renderer dead | W9 debug | `cmes-verify-evidence` |
| `MES_DB_PATH` · `data/ccl_mes.db` · demo DB · WAL | W10 ops | `cmes-live-db` |
| snapshot · restore · `backup-offsite` | W10 ops | `cmes-backup-wal` |
| `Jwt:SigningKey` · login · refresh · dual-sig OPS_* | W10 ops | `cmes-secrets-jwt` |
| Catalyst · entitlements · notarize · ATS · Keychain | W10 ops | `cmes-macos-ship` |

Không xác định được work-class ⇒ chưa hiểu yêu cầu ⇒ hỏi, đừng code.

## Pha 1 — ANALYZE

- Viết 1 câu: **thay đổi này làm gì cho người đứng máy?** Không trả lời
  được ⇒ STOP.
- Thu bằng chứng hiện trạng bằng lệnh, không bằng trí nhớ:
  `grep` / `sqlite3 .schema` / `curl` / đọc test đang phủ vùng đó.
- Có sự cố ⇒ **RCA proven trước** (S1). "Most likely" không phải nguyên nhân.

## Pha 2 — SELECT

Nêu **≥2 phương án**, chấm 1–5 theo: Blast radius · Reversibility ·
Contract impact · Evidence cost · Debt delta. Chọn tổng cao nhất.
Bất kỳ tiêu chí nào = 1 ⇒ **STOP-gate, hỏi Henry**.

**Luật additive:** mở rộng enum/state/cột đã production = **cộng thêm,
không dịch giá trị cũ, projection một chiều về hình cũ**. Phương án
"sửa lại cho sạch" giá trị cũ tự động 1 điểm Contract impact.

## Pha 3 — EXECUTE

- Diff tối thiểu. Không refactor kèm theo. Không "tiện tay dọn".
- Tuân skill guardian đã nạp ở bước 0.
- Chuỗi hiển thị mới ⇒ vào `TranslationCatalog`, không hardcode.

## Pha 4 — AUDIT (tĩnh)

```bash
bash CCL-MES-Hybrid/scripts/gate-all.sh
```
Ratchet **không được xấu đi**. Muốn bump BASELINE ⇒ giải thích được vì sao
hợp lệ, ngay trong PR body. Không giải thích được ⇒ STOP-gate.

## Pha 5 — VERIFY (động) — pha này KHÔNG thay thế được pha 4

| Loại | Bằng chứng phải dán |
|---|---|
| Schema | `.schema` trước/sau + rowcount + SHA256 |
| State | output test parity + ma trận transition |
| API | `curl` thật + status + body, cả happy path lẫn 403/409 |
| UI | screenshot **2 density** (`shopfloor` + `office`) |
| Gate mới | **PASS → FAIL (inject vi phạm) → PASS** |
| Debug | lệnh chứng minh nguyên nhân + output của nó |

**Không dán output = chưa xong.** Câu "đã test rồi" không phải bằng chứng.

## Pha 6 — LEARN

Bug class mới tốn ≥2h ⇒ lesson card vào `LESSONS-LEARNED.md` **kèm cơ chế
chặn** (test / gate / rule). Cột "Cơ chế chặn tái phát" rỗng ⇒ PR bị reject.
Prose không ship.

## STOP-gate — dừng và hỏi

1. Phương án thắng có tiêu chí = 1 điểm.
2. Đụng state machine mà `P10.7-WO-STATE-CONTRACT.md` chưa mô tả transition đó.
3. Cần chạy migration lên live DB (`data/ccl_mes.db`).
4. RCA chưa proven mà đã muốn mở PR fix.
5. Phải bump BASELINE của gate mà không giải thích được.
6. Đụng `src/CCL.MES.Web` (đóng băng). Domain/Application/Infrastructure được sửa khi schema — vẫn STOP nếu migration **live** (mục 3).

## Do NOT

- Nhảy từ ANALYZE thẳng sang EXECUTE.
- Coi gate xanh (pha 4) là đã verify (pha 5).
- Nạp cả `SKILLS.md` 612 dòng khi chỉ sửa một dòng CSS.
- Vá tại chỗ khi một pha fail — quay lại pha 1.

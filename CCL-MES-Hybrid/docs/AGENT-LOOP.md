# CCL-MES — Agent Loop (vòng lặp thực thi có kiểm chứng)

> **Status: ACTIVE.** Đây là **hợp đồng vận hành** cho mọi phiên làm việc
> của agent trên repo này. Nó không mô tả *cái gì* phải build (đó là
> `CLAUDE.md`), mà mô tả *bằng cách nào* một thay đổi được phép đi từ
> yêu cầu → merge.
>
> Sinh ra từ audit kiến trúc 2026-08-18. Vấn đề gốc: tri thức dự án nằm
> trong ~1.800 dòng prose (`CLAUDE.md` + `SKILLS.md` + `LESSONS-LEARNED.md`),
> agent phải nạp hết mỗi phiên và vẫn trôi; đồng thời bộ skill hiện có
> chỉ canh **vỏ UI** (showcard / row-menu / print) trong khi các vùng
> đắt nhất (schema, state contract, audit, RBAC, i18n) **không có gate**.

---

## 1. Vòng lặp 6 pha

Mọi thay đổi đi qua đúng 6 pha, theo thứ tự, không nhảy cóc:

```
  ┌────────────┐
  │ 1 ANALYZE  │  Phân loại work-class + thu bằng chứng (KHÔNG đoán)
  └─────┬──────┘
        ▼
  ┌────────────┐
  │ 2 SELECT   │  ≥2 phương án + chấm điểm + chọn agent + chọn skill bắt buộc
  └─────┬──────┘
        ▼
  ┌────────────┐
  │ 3 EXECUTE  │  Diff tối thiểu, tuân skill guardian của work-class
  └─────┬──────┘
        ▼
  ┌────────────┐
  │ 4 AUDIT    │  gate-all.sh — kiểm TĨNH, ratchet không được xấu đi
  └─────┬──────┘
        ▼
  ┌────────────┐
  │ 5 VERIFY   │  Chạy thật + DÁN OUTPUT THẬT. Không output = chưa xong
  └─────┬──────┘
        ▼
  ┌────────────┐
  │ 6 LEARN    │  Bug class mới → lesson + cơ chế chặn, cùng PR
  └────────────┘
```

**Luật bất biến của vòng lặp:**

- **Không nhảy pha.** Từ ANALYZE nhảy thẳng sang EXECUTE là nguồn gốc của
  L4 (lesson prose không có canary) và L7 (fix sai vì RCA "most likely").
- **Pha 4 và 5 khác nhau.** AUDIT là tĩnh (grep/gate/ratchet). VERIFY là
  động (chạy API/test/script, dán stdout). Pass pha 4 **không** thay cho pha 5.
- **Một pha thất bại → quay lại pha 1**, không vá tại chỗ. Fix mà không
  hiểu nguyên nhân là cách sinh ra lesson tiếp theo.

---

## 2. Bảng phân loại work-class → agent → skill bắt buộc → gate

Pha 1 kết thúc bằng việc chốt **đúng một** work-class. Pha 2 tra bảng này.

| # | Work-class | Dấu hiệu nhận biết | Agent chủ trì | Skill BẮT BUỘC | Gate phải xanh |
|---|---|---|---|---|---|
| **W1** | **Schema / migration** | Đụng `Entities/`, `Migrations/`, `DbContext` | `mes-process-architect` | `cmes-migration-abc` | `gate-all` + rowcount + SHA |
| **W2** | **State machine / phase** | Đụng `WorkOrderStateMachine`, `MesPhase`, `LegPhase`, `/advance` | `mes-process-architect` | `cmes-state-contract` | parity test + `gate-all` |
| **W3** | **API endpoint** | Thêm/sửa controller, DTO, route | `cmes-implementer` | `cmes-thin-controller` | `gate-thin-controller` |
| **W4** | **QC / chất lượng** | `CheckItemLibrary`, resolver, ngưỡng, chữ ký, freeze | `mes-quality-architect` | `cmes-audit-emit` | `gate-audit-emit` |
| **W5** | **UI / màn hình** | `.razor`, `app.css`, layout, grid | `cmes-shopfloor-ux` | `cmes-design-tokens` + skill chrome sẵn có | `gate-design-tokens` + `gate-hex` + `gate-row-actions` + `gate-floating-showcard` |
| **W6** | **RBAC / quyền** | policy, role, `AuthorizeView`, 403 | `cmes-implementer` | `cmes-rbac-matrix` | `RbacTests` + `gate-all` |
| **W7** | **i18n** | thêm chuỗi hiển thị bất kỳ | (agent đang chủ trì) | `cmes-i18n-parity` | `gate-i18n-parity` |
| **W8** | **Tích hợp / master data** | import IFS, outbox, idempotency | `mes-integration-architect` | `cmes-migration-abc` (nếu đụng schema) | `IdempotencyMiddlewareTests` |
| **W9** | **Debug / sự cố** | "không chạy", "404", "renderer dead" | `cmes-rca-detective` | `cmes-verify-evidence` | RCA proven trước khi mở PR |

**W7 luôn kèm.** Bất kỳ work-class nào thêm chuỗi hiển thị đều phải kéo
theo `cmes-i18n-parity` — i18n không phải một task riêng, nó là thuế của
mọi task chạm UI.

---

## 3. Pha 2 — cách chọn phương án (không chọn bằng cảm tính)

Bắt buộc nêu **≥2 phương án**, chấm theo 5 tiêu chí, thang 1–5, chọn tổng cao nhất.
Nếu phương án thắng có **bất kỳ tiêu chí nào = 1** → STOP-gate, hỏi Henry.

| Tiêu chí | Câu hỏi | 5 điểm | 1 điểm |
|---|---|---|---|
| **Blast radius** | Bao nhiêu surface vỡ nếu sai? | 1 file, có gate canh | Live DB / mọi màn hình |
| **Reversibility** | Rollback mất bao lâu? | `git revert`, không đụng dữ liệu | Cần restore backup |
| **Contract impact** | Có đụng hợp đồng đã ký? | Additive thuần | Đổi nghĩa state/route/enum cũ |
| **Evidence cost** | Chứng minh đúng bằng gì? | Test sẵn có phủ | Phải kiểm tay trên máy thật |
| **Debt delta** | Nợ kỹ thuật tăng hay giảm? | Ratchet đi xuống | Thêm nhánh song song mới |

**Luật additive (bất di bất dịch).** Khi mở rộng một enum / state / cột đã
lên production: **cộng thêm, không dịch giá trị cũ, projection một chiều
về hình cũ**. Đây là lý do `MesPhase` 14-state sống chung được với
`ProcessStepCode` 8-state. Phương án nào đòi "sửa lại cho sạch" giá trị cũ
= tự động 1 điểm Contract impact.

---

## 4. STOP-gate — khi nào agent PHẢI dừng và hỏi

Dừng, viết rõ đang vướng gì, **không tự quyết**:

1. Phương án thắng có tiêu chí = 1 điểm (§3).
2. Đụng `WorkOrderStateMachine` / `MesPhase` mà `P10.7-WO-STATE-CONTRACT.md`
   chưa mô tả transition đó → cần sửa contract trước, contract cần chữ ký.
3. Cần chạy migration lên **live DB** (`data/ccl_mes.db`).
4. RCA chưa proven mà đã muốn mở PR fix (vi phạm S1).
5. Gate phải bump BASELINE mà không giải thích được vì sao hợp lệ.
6. Yêu cầu đụng `../src/CCL.MES.*` (baseline read-only theo README Hybrid).

---

## 5. Bằng chứng — định nghĩa "xong"

Kế thừa S3 ("no output = not done"), nâng thành hợp đồng:

| Loại thay đổi | Bằng chứng BẮT BUỘC dán vào PR |
|---|---|
| Schema | `sqlite3 .schema <bảng>` trước/sau + rowcount + SHA256 |
| State machine | Output test parity + ma trận transition |
| API | `curl` thật kèm HTTP status + body, cả happy path lẫn 403/409 |
| UI | Screenshot **2 density** (`shopfloor` + `office`) |
| Gate mới | Chuỗi **PASS → FAIL (inject vi phạm) → PASS** |
| Debug | Lệnh chứng minh nguyên nhân + output của nó |

**Không có bằng chứng = thay đổi chưa tồn tại.** Câu "đã test rồi" không
phải bằng chứng.

---

## 6. Roster agent

Định nghĩa tại `.claude/agents/`. Tất cả đều đọc file này trước khi làm.

| Agent | Vai | Được sửa code? |
|---|---|---|
| `mes-process-architect` | ISA-95, routing DAG, state contract, schema | Không — ra thiết kế + contract |
| `mes-quality-architect` | QC library, AQL/SPC/CAPA, chữ ký, freeze | Không — ra thiết kế |
| `mes-integration-architect` | ERP/outbox/master data/idempotency | Không — ra thiết kế |
| `cmes-shopfloor-ux` | token, density, touch, contrast, grid | Có — chỉ CSS/Razor |
| `cmes-implementer` | thực thi diff tối thiểu theo thiết kế | Có |
| `cmes-auditor` | pha 4 — chạy gate tĩnh, báo ratchet delta | Không |
| `cmes-verifier` | pha 5 — chạy thật, dán output, ra verdict | Không |
| `cmes-rca-detective` | pha 1 khi có sự cố — RCA proven | Không |

**Vì sao 3 architect không được sửa code:** tách "ai quyết định hình dạng"
khỏi "ai gõ phím" là cách duy nhất giữ được contract-first. Architect ra
contract, implementer thực thi, verifier chứng minh. Một agent làm cả ba
sẽ tự chấm bài của chính mình.

---

## 7. Bản đồ gate

| Gate | Canh điều gì | Lesson | Kiểu |
|---|---|---|---|
| `gate-no-hardcoded-hex.sh` | màu phải qua token | L37 | ratchet 35 |
| `gate-row-actions.sh` | không cột "Actions" | L35 | pattern |
| `gate-floating-showcard.sh` | showcard bọc FloatingWindow | L34 | pattern |
| `gate-spec-print.sh` | native print + bảng auto+nowrap | L39 | block-aware |
| `gate-thin-controller.sh` | **MỚI** — logic không nằm trong controller | L40 | ratchet |
| `gate-design-tokens.sh` | **MỚI** — type/space qua scale, có density | L41 | ratchet |
| `gate-i18n-parity.sh` | **MỚI** — key trùng/rỗng, chuỗi cứng trong Razor | L42 | ratchet |
| `gate-audit-emit.sh` | **MỚI** — mutation phải emit audit, detail sạch secret | L43 | ratchet |
| `gate-all.sh` | **MỚI** — chạy tất cả, `[N/total]` + SUMMARY (S12) | — | runner |

---

## 8. Cách một phiên bắt đầu

```
1. Đọc CLAUDE.md §0 (router) — 40 dòng, không phải 600.
2. Phân loại work-class theo §2 trên đây.
3. Nạp ĐÚNG skill của work-class đó (+ cmes-i18n-parity nếu chạm UI).
4. Chạy vòng lặp 6 pha.
```

Không nạp cả `SKILLS.md` 612 dòng khi chỉ sửa một dòng CSS. Nạp theo nhu
cầu là điều kiện để phiên dài không trôi context.

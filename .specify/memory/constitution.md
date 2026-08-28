<!--
SYNC IMPACT REPORT
==================
Version change: (chưa có) → 1.0.0
Loại thay đổi:  MAJOR — phê chuẩn lần đầu. File trước đó là template chưa điền,
                không có điều khoản nào đang hiệu lực để so sánh.

Nguyên tắc THÊM MỚI (5):
  I.   Bằng chứng, không phải lời khẳng định (NON-NEGOTIABLE)
  II.  Mọi bài học phải có cơ chế chặn tái phát (NON-NEGOTIABLE)
  III. Ratchet chỉ đi xuống
  IV.  Dữ liệu sản xuất là BẰNG CHỨNG, không phải trạng thái (NON-NEGOTIABLE)
  V.   Bàn tay người đứng máy quyết định giao diện

Mục THÊM MỚI:
  - Ràng buộc kỹ thuật
  - Quy trình phát triển
  - Governance

Mục GỠ BỎ: không có.

Nguồn suy ra (bản hiến pháp này KHÔNG phát minh luật mới — nó chỉ nâng luật
đã thi hành lên thành văn bản có thứ bậc):
  - CLAUDE.md §0 Router · §1 Vùng cấm · §4 EF Core safety · UI rule L34/L35/L37/L39/L52
  - CCL-MES-Hybrid/docs/LESSONS-LEARNED.md — 60 lesson card, mỗi card có cột
    "Cơ chế chặn tái phát" bắt buộc không rỗng
  - CCL-MES-Hybrid/docs/SKILLS.md — RCA proven, verify-script per PR, STOP-gate
  - CCL-MES-Hybrid/docs/STACKED-PR-CHECKLIST.md — R1..R7
  - CCL-MES-Hybrid/docs/AGENT-LOOP.md — vòng lặp 6 pha
  - CCL-MES-Hybrid/scripts/gate-*.sh — 19 gate đang chạy trong CI

Ghi chú về ngày phê chuẩn: các luật trong văn bản này đã được thi hành từ
2026-05-30 (commit đầu) qua CLAUDE.md và LESSONS-LEARNED.md. RATIFICATION_DATE
ghi 2026-08-26 vì đó là ngày CHÍNH VĂN BẢN NÀY được thông qua — không suy diễn
ngược một ngày thông qua cho tài liệu chưa từng tồn tại.

TODO còn treo: không có. Mọi placeholder đã được thay bằng nội dung cụ thể.
-->

# CCL-MES Constitution

Hiến pháp của **CCL-MES** — Manufacturing Execution System của CCL Design Vietnam.

Hệ thống này chạy dưới xưởng in nhãn. Người dùng đứng máy, đeo găng, cầm tablet
một tay, và mỗi lần bấm là một chữ ký lên hồ sơ chất lượng. Đó là lý do các điều
khoản dưới đây nghiêm ngặt hơn mức thường thấy ở một ứng dụng nội bộ: một dòng
bấm nhầm ở đây không phải là một bug giao diện, nó là một lô hàng sai được ký duyệt.

## Core Principles

### I. Bằng chứng, không phải lời khẳng định (NON-NEGOTIABLE)

Không thay đổi nào được tuyên bố là "xong" nếu không kèm **output thật đã dán vào**.

- Pha AUDIT (gate tĩnh) **KHÔNG** thay thế được pha VERIFY (chạy thật). Gate xanh
  chỉ chứng minh "không vi phạm luật đã biết", không chứng minh "dùng được".
- "Đã test rồi" **KHÔNG** phải bằng chứng. Số test pass, output lệnh, ảnh chụp màn
  hình, hoặc probe boot mới là bằng chứng.
- RCA phải **proven**: mỗi giả thuyết phải được chứng minh bằng một lệnh có output
  TRƯỚC KHI ai đó được phép viết fix. Không có RCA proven mà mở PR là STOP-gate.
- Test mới phải được xác nhận **ĐỎ khi hoàn nguyên fix**. Test xanh suông không
  chứng minh điều gì — nó chỉ chứng minh test tồn tại.

*Lý do:* dự án này đã nhiều lần mất hàng giờ vì một chẩn đoán nghe hợp lý mà không
ai chạy thử. Chi phí dán một dòng output là vài giây; chi phí sửa nhầm chỗ là cả buổi.

### II. Mọi bài học phải có cơ chế chặn tái phát (NON-NEGOTIABLE)

Một bài học không có cơ chế chặn thì **không được merge**.

- Mọi bug class tốn ≥2 giờ điều tra phải thành một lesson card trong
  `LESSONS-LEARNED.md` theo đúng 4 cột: Triệu chứng · Root cause (proven) · Fix ·
  **Cơ chế chặn tái phát**.
- Cột "Cơ chế chặn tái phát" **PHẢI** trỏ tới một thứ fail được CI: tên file test,
  gate script, rule number, hoặc boot probe. Để trống = PR bị từ chối.
- **Khi phải vá cùng một lớp lỗi lần thứ HAI, dừng lại.** Đó là dấu hiệu thiếu một
  LUẬT, không phải thiếu một lần vá.

*Lý do:* văn xuôi không fail CI. Markdown không chặn được ai. Dự án này đã vấp đúng
kiểu thất bại đó ít nhất bốn lần — token ma, vùng chạm, `clamp/vw`, và `box-sizing`
(vá lẻ **13 lần** trước khi thành luật).

### III. Ratchet chỉ đi xuống

Mọi gate có baseline đều là **ratchet một chiều**.

- Baseline **CHỈ** được giảm. Tăng baseline là STOP-gate: phải giải thích được bằng
  văn bản và phải được Henry duyệt.
- Baseline phải được **đếm lại bằng chính gate đó**, không chép tay. Baseline sai
  còn tệ hơn không có baseline — nó rửa nợ kỹ thuật thành quyết định thiết kế.
- Mỗi gate phải có `--self-test` **hai chiều**: bắt được vi phạm mới, VÀ không báo
  nhầm mẫu hợp lệ.
- 19 gate hiện hành phải **PASS** trước mọi PR: `bash CCL-MES-Hybrid/scripts/gate-all.sh`.

*Lý do:* nợ kỹ thuật không tự dừng lại. Nếu con số được phép tăng, nó sẽ tăng.

### IV. Dữ liệu sản xuất là BẰNG CHỨNG, không phải trạng thái (NON-NEGOTIABLE)

Hồ sơ chất lượng đã ký là bằng chứng pháp lý về việc gì đã xảy ra tại thời điểm đó.

- **Đóng băng bằng chứng:** chuỗi hiển thị mà người vận hành đã thấy phải được đóng
  băng vào bảng dữ liệu — và phải đóng băng **ĐỦ MỌI NGÔN NGỮ** tại thời điểm đó.
  Chọn một ngôn ngữ lúc ghi là vứt bỏ vĩnh viễn khả năng đổi ngôn ngữ.
- **Sửa master data KHÔNG hồi tố** hồ sơ đang chạy hoặc đã ký.
- **Mọi mutation phải emit audit row.** Detail JSON tuyệt đối không chứa password,
  hash, cookie, hay token.
- **Live DB đi theo Phase A→B→C:** backup tường minh + sha256 + rowcount baseline →
  test trên DB cô lập ở `/tmp` → áp thật + verify rowcount trước = sau.
  Migration lên live DB là **STOP-gate**.
- `dotnet ef migrations remove` **BỊ CẤM** — nó tự connect live DB và chạy `Down()`
  thật. Migration chỉ đi tiến, undo bằng `rm` thủ công + `git checkout` snapshot.

*Lý do:* ngày 2026-05-31 một lệnh `ef migrations remove` đã DROP bảng `AuditLogs`
trên DB thật. Phải khôi phục từ backup byte-identical. Điều khoản này là cái giá
đã trả cho sự cố đó.

### V. Bàn tay người đứng máy quyết định giao diện

Giao diện được đo bằng người đeo găng cầm tablet, không bằng lập trình viên ngồi
trước màn 27 inch.

- Hai chế độ mật độ là **hợp đồng**, không phải tuỳ chọn: `office` và `shopfloor`.
  Ở `shopfloor`, vùng chạm **PHẢI** ≥ `var(--d-tap)` và cỡ chữ ≥ 16px.
- Bảng rộng hơn tablet ngang (≥1024px) **PHẢI** có luật responsive cho chính nó.
  Bắt người đeo găng cuộn ngang một bảng ma trận là cách chắc chắn để ký nhầm dòng.
- **Không phát minh khuôn thứ ba.** Bảng dày dữ liệu chọn một trong hai khuôn đã có:
  *sập card* (`[data-label]::before`) hoặc *cột dính* (`position: sticky`).
- Cỡ chữ và khoảng cách **KHÔNG** được lái bằng đơn vị viewport (`vw`/`vh`) — chúng
  mù `data-density` và mù `--ui-scale`, tức là màn hình đó tự rút khỏi hệ mật độ.
- Gate xanh **KHÔNG** có nghĩa là dùng được. Mọi PR chạm `.razor` hoặc `app.css`
  phải kèm **ảnh chụp ở 768px** (`--bp-tablet-p`, bề mặt xưởng chính).

*Lý do:* app này sẽ được cài trên tablet dưới xưởng. Màn hình đẹp trên máy dev mà
vỡ trên tablet là màn hình hỏng, không phải màn hình "cần tinh chỉnh sau".

## Ràng buộc kỹ thuật

**Stack.** .NET 10 · EF Core · Blazor. Hai bề mặt: `CCL.MES.Web` (Blazor Server,
legacy) và `CCL-MES-Hybrid` (MAUI Blazor Hybrid, offline shop-floor). Provider mặc
định SQLite; cổng SQL Server phải giữ **provider-agnostic** — mọi migration mới
PHẢI strip `type: "TEXT|INTEGER|REAL"` và `.HasColumnType(...)`.

**Phân tầng.** Controller **mỏng**: luật nghiệp vụ nằm ở Application service và
Domain policy, không nằm trong controller HTTP. Enforce: `gate-thin-controller.sh`.

**Một nguồn sự thật cho mỗi thứ có thang.**

| Thứ | Nguồn duy nhất | Gate |
|---|---|---|
| Màu | design token semantic ở `:root` | `no-hardcoded-hex` · `token-defined` |
| Cỡ chữ · khoảng cách · bo góc | thang `--fs-*` `--sp-*` `--r-*` | `design-tokens` · `viewport-sizing` |
| Breakpoint | thang 5 bậc `--bp-*` (480·768·1024·1280·1600) | `breakpoint-scale` |
| Vùng chạm | `--d-tap` theo density | `tap-target` |
| Xác nhận OK/NG | `Shared/ConfirmToggle.razor` | `confirm-toggle` |
| Showcard · detail dialog | `Shared/FloatingWindow.razor` | `floating-showcard` |
| Hành động trên dòng grid | `Shared/RowContextMenu.razor` | `row-actions` |
| Chuỗi hiển thị | `TranslationCatalog` / `SharedResource.resx` | `i18n-parity` |

Đổi tone = **swap giá trị token** ở một chỗ, KHÔNG find/replace.

**i18n là thuế của mọi task chạm UI**, không phải một task riêng. Thêm key mới bắt
buộc có đủ EN + VI, đặt theo namespace.

**Vùng cấm.** `src/CCL.MES.*` là baseline **read-only** — đụng vào là STOP-gate.
`_legacy-web-freeze` và `_archive` không được giải nén vào cây làm việc.

## Quy trình phát triển

**Vòng lặp 6 pha, không nhảy cóc:**

`ANALYZE → SELECT → EXECUTE → AUDIT → VERIFY → LEARN`

Mỗi phiên bắt đầu bằng skill `cmes-loop`, sau đó **chỉ nạp skill của work-class
đang làm**. Nạp toàn bộ tài liệu để sửa một dòng CSS là cách chắc chắn để trôi
context giữa phiên dài. Bảng tra work-class → skill → agent nằm ở `CLAUDE.md §0`.

**Trước khi nói "xong":**

1. `bash CCL-MES-Hybrid/scripts/gate-all.sh` → 19 gate PASS.
2. Chạy đủ các test suite liên quan, **dán số thật**.
3. Chạy thật và dán output thật (pha VERIFY).
4. PR chạm UI: kèm ảnh chụp ở 768px.

**STOP-gate — dừng lại và hỏi Henry:**

- Phương án có tiêu chí chấm 1 điểm.
- Transition chưa có trong `P10.7-WO-STATE-CONTRACT.md`.
- Phải chạy migration lên **live DB**.
- RCA chưa proven mà đã muốn mở PR.
- Phải **tăng** BASELINE của một gate mà không giải thích được.
- Phải đụng `src/CCL.MES.*` (baseline read-only).

**Stacked PR** tuân theo R1..R7 trong `STACKED-PR-CHECKLIST.md`: `--base` tường
minh, không `--delete-branch` giữa stack, cascade-close recovery, comment-strip
gate, migration step trong Henry-action, verify-script tự chuẩn bị DB, và header
`[ctx] DB=` bắt buộc trong mọi operator script.

## Governance

**Thứ bậc.** Hiến pháp này đứng trên mọi tài liệu và thói quen khác trong dự án.
Khi hiến pháp mâu thuẫn với `CLAUDE.md`, `SKILLS.md`, một skill, hoặc một thói quen
đang chạy, **hiến pháp thắng** — và mâu thuẫn đó phải được sửa ở tài liệu kia ngay
trong PR phát hiện ra nó.

**Quan hệ với tài liệu chi tiết.** Hiến pháp nêu *luật*; các tài liệu dưới đây nêu
*cách thi hành* và không được mâu thuẫn với nó:

- `CLAUDE.md` — router work-class, bảng tra skill/agent, chi tiết vận hành.
- `CCL-MES-Hybrid/docs/LESSONS-LEARNED.md` — sổ nợ đã trả, canonical index.
- `CCL-MES-Hybrid/docs/SKILLS.md` — playbook quy trình.
- `CCL-MES-Hybrid/docs/STACKED-PR-CHECKLIST.md` — R1..R7.
- `CCL-MES-Hybrid/scripts/gate-all.sh` — hiện thân chạy được của Nguyên tắc III.

**Thủ tục sửa đổi.** Mọi sửa đổi phải: (a) đi qua PR riêng chỉ chạm hiến pháp và
các tài liệu bị ảnh hưởng; (b) nêu rõ điều khoản nào thêm/sửa/gỡ và **vì sao**;
(c) nếu gỡ hoặc nới một điều khoản, phải chỉ ra sự cố nào chứng minh điều khoản đó
không còn cần thiết — **không nới luật vì luật gây bất tiện**; (d) được Henry duyệt.

**Chính sách phiên bản.** Semantic versioning:

- **MAJOR** — gỡ bỏ hoặc định nghĩa lại một nguyên tắc theo hướng không tương thích ngược.
- **MINOR** — thêm nguyên tắc hoặc mục mới, hoặc mở rộng đáng kể hướng dẫn hiện có.
- **PATCH** — làm rõ câu chữ, sửa lỗi chính tả, tinh chỉnh không đổi ngữ nghĩa.

**Rà soát tuân thủ.** Mọi PR review phải kiểm: gate-all PASS · lesson mới (nếu có)
đủ cột "Cơ chế chặn tái phát" · baseline không tăng · PR chạm UI có ảnh 768px ·
mutation mới có audit row · chuỗi hiển thị mới có đủ EN+VI. Độ phức tạp phát sinh
phải được biện minh; không biện minh được thì cắt.

**Vi phạm.** Một PR vi phạm hiến pháp bị từ chối, kể cả khi code chạy đúng. Nếu
buộc phải vi phạm, PR phải nêu điều khoản bị vi phạm, lý do, và thời hạn khắc phục
— và điều đó phải được Henry chấp thuận trước khi merge.

**Version**: 1.0.0 | **Ratified**: 2026-08-26 | **Last Amended**: 2026-08-26

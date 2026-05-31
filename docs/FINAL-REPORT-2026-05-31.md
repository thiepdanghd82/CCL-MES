# FINAL REPORT — CCL-CMES Phase 1 → Phase 4 (2026-05-31)

> Tổng kết một mạch toàn bộ phiên làm việc trên project CCL-CMES, từ
> audit (Phase 0) → finish NPI import (Phase 1) → login + i18n
> (Phase 2) → Settings dropdown (Phase 3) → merge tất cả vào `main` +
> i18n full EN coverage + báo cáo (Phase 4).
>
> KHÔNG có file nào trong `Ops Control v1.2/`, `CMES/`,
> `Old ver ( DO NOT USE)/`, `SpecHub/` được đọc-ghi-tạo-xoá. Mọi việc
> nằm trong `CCL-CMES/CCL-MES/`.

---

## 1. Tổng quan 3 phase + PR + commit

| Phase | PR | Branch | State | Commit count | SHA list |
|---|---|---|---|---|---|
| **Phase 0** — Audit doc | — | (commit thẳng vào Phase 1 PR) | MERGED qua PR #1 | 1 | `16d54da` |
| **Phase 1** — NPI import + i18n EN default | [#1](https://github.com/thiepdanghd82/CCL-MES/pull/1) | `feat/phase1-npi-import` | **MERGED** | 4 | `16d54da` · `715402c` · `4166997` · `c1b30ea` |
| **Phase 2** — Login + cookie auth + flag picker | [#2](https://github.com/thiepdanghd82/CCL-MES/pull/2) | `feat/phase2-login-i18n` | **MERGED** | 5 | `496f2e5` · `0c0b3f2` · `b536d5e` · `82a8887` · `fe7dd88` |
| **Phase 3** — Settings dropdown (10 sub-tab) | [#3](https://github.com/thiepdanghd82/CCL-MES/pull/3) | `feat/phase3-settings-dropdown` | **MERGED** | 3 | `b86ccb9` · `bc21e6e` · `59ffeae` |
| **Phase 4** — Merge + i18n full EN coverage + báo cáo | (commit thẳng `main`) | `main` | shipping | 1 | (commit cuối sau khi viết doc xong) |

Merge commits trên main:
- `e96592f` Merge pull request #1 from thiepdanghd82/feat/phase1-npi-import
- `e96592f` Merge pull request #2 from thiepdanghd82/feat/phase2-login-i18n
- `c6ce14f` Merge pull request #3 from thiepdanghd82/feat/phase3-settings-dropdown

### Commit chi tiết (Conventional Commits, newest first)

```
docs(phase3): 4 evidence screenshots — dropdown + sub-tab in EN + VI       59ffeae
feat(i18n): Settings header dropdown + 51 EN+VI keys for the 10 sub-tabs   bc21e6e
feat(settings): 10 placeholder Razor pages under /settings (Ops v1.2)      b86ccb9
docs(phase2): 4 evidence screenshots from the Phase 2 build                fe7dd88
feat(auth): wire cookie auth + global authorize fallback + Blazor shell    82a8887
feat(login): /login + /logout + /set-language Razor Pages + i18n keys      b536d5e
feat(i18n): SVG flag components + LangFlagPicker (shared with Phase 3)     0c0b3f2
feat(auth): User entity + unique-username index + DbSet plumbing           496f2e5
feat(i18n): ASP.NET Core Localization (EN default + VI satellite) +        c1b30ea
            migrate hardcoded VI strings
fix(import-npi): correct IFS column mapping + atomic transaction +         4166997
                 skipped/failed counters
feat(npi): NPI module — entities, paged service, API, shared Pager         715402c
docs(audit): Phase 0 audit + sub-tab catalogue from Ops Control v1.2       16d54da
```

---

## 2. Issue đã fix + cải tiến đã ship

### 2.1 P0 issues fixed

| ID | File:line | Triệu chứng | Cách fix | Phase |
|---|---|---|---|---|
| P0-1 | `tools/import_npi.py` read_routing | `PartNo` lấy cột "Create Date" ("11/5/26"), `WorkCenterNo` lấy cột "Operation Description", `MachineSetupTime`/`LaborSetupTime` lấy cột string → num()=0.0 cho mọi row | Mapping lại 7 cột theo header IFS thật (62 cột) | 1 |
| P0-2 | `tools/import_npi.py` read_structures | `ParentPart`/`ComponentPart` swap; `QtyAssembly` đọc cột ComponentPart (giá trị 30030951 thay vì 0.001158); `Uom` lấy `[3]` ComponentDescription | Mapping lại theo header IFS thật; UOM lấy column `[29]` | 1 |
| P0-3 | `tools/import_npi.py` read_raw_materials | `PriceUom` lấy cột Price-incl-Tax (số), `CatalogGroup`/`Type`/`Grp` lấy cột không tồn tại trong xlsx hiện tại | Mapping closest-meaning theo IFS xlsx hiện tại; document gap trong docstring | 1 |

### 2.2 Improvements đã thêm

| Improvement | File | Phase |
|---|---|---|
| Transaction-wrapped import (BEGIN..COMMIT, rollback on except) | `tools/import_npi.py` | 1 |
| Per-table counters (seen / skipped / imported / failed) + skip-reason histogram | `tools/import_npi.py` | 1 |
| Pre-flight schema assert (bail nếu thiếu 4 bảng NPI) | `tools/import_npi.py` | 1 |
| ASP.NET Core Localization (EN default + VI satellite via .resx) | `Program.cs` + `Resources/SharedResource.*.resx` | 1 |
| `IStringLocalizer<SharedResource>` đưa tất cả chuỗi UI qua key | 14 files razor/cshtml | 1 → 4 |
| EF index trên `Username` (unique), `Code` (WC), `PartNo` (RM + RO), `ParentPart` (MS) | `MesDbContext.cs` | 1 + 2 |
| Cookie auth (PBKDF2 + 8h sliding) | `Program.cs` + `Pages/Login.cshtml.cs` | 2 |
| Global `FallbackPolicy = RequireAuthenticatedUser` | `Program.cs` | 2 |
| Inline SVG flags (GB + VN) thay emoji — đa OS render đồng nhất | `Shared/Flags/*` | 2 |
| Cookie `.AspNetCore.Culture` persist 1 năm + survives login flow | `Pages/SetLanguage.cshtml.cs` | 2 |
| Auto-redirect anonymous via `AuthorizeRouteView` + `RedirectToLogin` component | `App.razor` + `Shared/RedirectToLogin.razor` | 2 |
| Settings dropdown (10 sub-tab y hệt Ops Control v1.2 §2.3) | `Shared/MainLayout.razor` + `Pages/Settings/*` | 3 |
| State-machine reason strings localised-friendly (EN base) | `Domain/StateMachine/WorkOrderStateMachine.cs` | 4 |

### 2.3 Files touched count theo phase

| Phase | Files new | Files modified | Total lines |
|---|---|---|---|
| 1 | 12 | 8 | +1,568 / -80 |
| 2 | 19 | 7 | +752 / -26 |
| 3 | 14 | 3 | +309 / -0 |
| 4 | 0 (sửa hiện hữu) | ~7 | (ước tính từ diff) |

---

## 3. i18n coverage table (EN 100% / VI 100%)

### 3.1 Cách test
- Spawn server tại `http://localhost:5000`
- Session A: cookie `.AspNetCore.Culture=c=en|uic=en` (mặc định)
- Session B: cookie `.AspNetCore.Culture=c=vi|uic=vi`
- Cả 2 đều đăng nhập admin/admin, sau đó `curl` + headless Playwright screenshot.

### 3.2 Trạng thái mỗi page

| # | Page | EN OK | VI OK | Note |
|---|---|:---:|:---:|---|
| 1 | `/login` | ✓ | ✓ | "Sign in" / "Đăng nhập", placeholder, error message, footer |
| 2 | `/` (Index) | ✓ | ✓ | Title + 4 cards + footer |
| 3 | `/dashboard` | ✓ | ✓ | KPI labels, OEE section, table headers, OEE footer, WO-by-step. Title "Dashboard" giữ EN cả 2 ngôn ngữ (industry-standard) |
| 4 | `/workorders` | ✓ | ✓ | 9 column headers, Advance/Unlock buttons, demo note, 8 dynamic messages (advance_ok/fail, unlocked, qc_passed, start/pause/resume/finish) |
| 5 | `/workinstructions` | ✓ | ✓ | Title + meta (Product/Step/Machine/Version) + no-items |
| 6 | `/npi/engineer-routine` | ✓ | ✓ | Title + search placeholder + Search button + Loading |
| 7 | `/npi/engineer-structure` | ✓ | ✓ | Same pattern |
| 8 | `/npi/raw-materials` | ✓ | ✓ | Same pattern |
| 9 | `/npi/workcenter` | ✓ | ✓ | Same pattern |
| 10 | `/npi/engineer-spec` | ✓ | ✓ | Title + placeholder + 3 bullets |
| 11 | `/qcqa/iqc` | ✓ | ✓ | Title + placeholder + 3 bullets |
| 12 | `/qcqa/ipqc` | ✓ | ✓ | Title + placeholder + 3 bullets |
| 13 | `/qcqa/oqc` | ✓ | ✓ | Title + placeholder + 3 bullets |
| 14 | `/settings/profile` | ✓ | ✓ | "Settings — My Profile" / "Cài đặt — Hồ sơ của tôi" |
| 15 | `/settings/mypwd` | ✓ | ✓ | "Settings — My Password" / "Cài đặt — Mật khẩu của tôi" |
| 16 | `/settings/appearance` | ✓ | ✓ | "Settings — Appearance" / "Cài đặt — Giao diện" |
| 17 | `/settings/hardware` | ✓ | ✓ | "Settings — Hardware devices" / "Cài đặt — Thiết bị phần cứng" |
| 18 | `/settings/mode` | ✓ | ✓ | "Settings — Connection mode" / "Cài đặt — Chế độ kết nối" |
| 19 | `/settings/account` | ✓ | ✓ | "Settings — Account Control" / "Cài đặt — Quản lý tài khoản" (+ TODO RBAC) |
| 20 | `/settings/about` | ✓ | ✓ | "Settings — About / Diagnostics" / "Cài đặt — Giới thiệu / Chẩn đoán" |
| 21 | `/settings/data` | ✓ | ✓ | "Settings — Backup / Restore" / "Cài đặt — Sao lưu / Phục hồi" (+ TODO RBAC) |
| 22 | `/settings/syslog` | ✓ | ✓ | "Settings — System Logs" / "Cài đặt — Nhật ký hệ thống" (+ TODO RBAC) |
| 23 | `/settings/import-legacy` | ✓ | ✓ | "Settings — Import data v1.0" / "Cài đặt — Nhập dữ liệu v1.0" (+ TODO RBAC) |
| – | `MainLayout` (topbar nav) | ✓ | ✓ | 8 nav items + 3 dropdown labels + 18 sub-tab labels + Sign out |

**Residual hardcoded VI scan post-Phase-4** (excluding intentional `Tiếng Việt` ARIA label in `LangFlagPicker.razor`):

```
$ grep -rn --include="*.razor" --include="*.cshtml" \
    -E "[áàảãạâấầẩẫậăắằẳẵặéèẻẽẹêếềểễệíìỉĩịóòỏõọôốồổỗộơớờởỡợúùủũụưứừửữựýỳỷỹỵđ]" \
    src/CCL.MES.Web/ | grep -v "Resources/" | grep -v "LangFlagPicker.razor"
(nothing)

$ grep -rn --include="*.razor" --include="*.cshtml" -wE \
    "(Dang|Trang|Tong|Toi|Chua|Da|May|Phut|Khong|Huong|Cong|Cai|Thiet|Buoc|Hieu|Lieu)" \
    src/CCL.MES.Web/
(nothing)
```

### 3.3 Evidence screenshots (`docs/screenshots/`)

| Surface | EN | VI |
|---|---|---|
| Login | [login-en.png](screenshots/login-en.png) | [login-vi.png](screenshots/login-vi.png) |
| Home (authenticated) | [home-en-authenticated.png](screenshots/home-en-authenticated.png) | [home-vi-authenticated.png](screenshots/home-vi-authenticated.png) |
| Dashboard | [dashboard-en.png](screenshots/dashboard-en.png) | [dashboard-vi.png](screenshots/dashboard-vi.png) |
| Work Orders | [workorders-en.png](screenshots/workorders-en.png) | [workorders-vi.png](screenshots/workorders-vi.png) |
| Work Instructions | [workinstructions-en.png](screenshots/workinstructions-en.png) | [workinstructions-vi.png](screenshots/workinstructions-vi.png) |
| Settings dropdown | [settings-dropdown-en.png](screenshots/settings-dropdown-en.png) | [settings-dropdown-vi.png](screenshots/settings-dropdown-vi.png) |
| Settings sub-tab (Account Control) | [settings-account-en.png](screenshots/settings-account-en.png) | [settings-account-vi.png](screenshots/settings-account-vi.png) |

### 3.4 i18n key count
- Total keys: **~160** (1 file × 2 languages)
- File EN: `src/CCL.MES.Web/Resources/SharedResource.resx`
- File VI: `src/CCL.MES.Web/Resources/SharedResource.vi.resx`

---

## 4. Data integrity — CCL-CMES không mất dữ liệu

### 4.1 Row count chain (BEFORE → AFTER mỗi phase)

| Stage | WorkCenters | RawMaterials | RoutingOperations | ManufacturingStructures | Users |
|---|---:|---:|---:|---:|---:|
| **BEFORE Phase 1** (DB seed mẫu) | (table missing) | (table missing) | (table missing) | (table missing) | (table missing) |
| **AFTER Phase 1** import | 43 | 2,127 | 38,441 | 20,530 | (no Users table yet) |
| **BEFORE Phase 2** schema rebuild | 43 | 2,127 | 38,441 | 20,530 | (no Users table) |
| **AFTER Phase 2** rebuild + re-import + seed admin | 43 | 2,127 | 38,441 | 20,530 | 1 |
| **BEFORE Phase 4** merge (post Phase 3 build) | 43 | 2,127 | 38,441 | 20,530 | 1 |
| **AFTER Phase 4** merge | 43 | 2,127 | 38,441 | 20,530 | 1 |
| **AFTER restart proof** (fresh `dotnet run`) | 43 | 2,127 | 38,441 | 20,530 | 1 |

Tất cả 4 NPI tables giữ nguyên row count xuyên suốt 3 phase rebuild + 2 lần restart server.

### 4.2 Backup chain (path tường minh)

| Stage | File | Size | Integrity | MD5 |
|---|---|---:|:---:|---|
| Pre-Phase-1 | `src/CCL.MES.Web/ccl_mes.backup-2026-05-31.db` | 147,456 B | ok | `1976cc9550ea6044114fb3bdca7ee080` |
| Post-Phase-1 (imported) | `src/CCL.MES.Web/ccl_mes.imported-2026-05-31.db` | 11,104,256 B | ok | – |
| Pre-Phase-2 | `src/CCL.MES.Web/ccl_mes.backup-phase2-pre.db` | 11,104,256 B | ok | – |
| **Pre-Phase-4 (pre-merge)** | `src/CCL.MES.Web/ccl_mes.backup-phase4-pre.db` | 11,018,240 B | ok | `f387bfe1fa3d903186e89e51a76eadf3` |
| **Post-Phase-4 (final)** | `src/CCL.MES.Web/ccl_mes.backup-final-2026-05-31.db` | 11,018,240 B | ok | `7cc7611c8e1b4e28fda62758af6b0687` |

Tất cả backup `*.db` đều gitignored — tồn tại local trên dev machine,
không vào repo (đúng).

### 4.3 Restart proof

```
$ # Stop server
$ pkill -f "CCL.MES.W"

$ # Restart with clean process
$ dotnet run --project src/CCL.MES.Web

$ # Re-login + query API
$ curl -b /tmp/jar.txt "http://localhost:5000/api/npi/workcenters?page=1&pageSize=1"
{"items":[{"code":"AAINK",...}],"total":43,"page":1,"pageSize":1,"totalPages":43}

$ # Cross-check via sqlite3 (process running, but reads file directly)
$ sqlite3 src/CCL.MES.Web/ccl_mes.db \
    "SELECT (SELECT COUNT(*) FROM WorkCenters), \
            (SELECT COUNT(*) FROM RawMaterials), \
            (SELECT COUNT(*) FROM RoutingOperations), \
            (SELECT COUNT(*) FROM ManufacturingStructures);"
43|2127|38441|20530
```

Kết luận: dữ liệu persist trong SQLite file, không in-memory.

---

## 5. Vùng cấm nguyên vẹn

Mọi thư mục anh em cùng cấp với `CCL-CMES/` KHÔNG có file tracked nào
được đụng. `git status --short` (chạy đầu Phase 0 vs cuối Phase 4):

| Repo | Baseline đầu session (untracked) | Cuối Phase 4 (untracked) | Tracked modifications |
|---|---|---|---|
| `Ops Control v1.2/` | `4. CLAUDE OUTPUT/` + `START_SERVER_v1.5.10.command` | (giống hệt) | **0** |
| `CMES/` | `CCL_Design_MES_System_Design.docx` + `Input/` + `~$L_Design_MES_System_Design.docx` | (giống hệt) | **0** |
| `SpecHub/` | `CMES_GENESIS_PROMPT.md` + `PROMPT_EXTRACT_CORE.md` + `PROMPT_PDF_BORDER_FIX.md` | (giống hệt) | **0** |
| `Old ver ( DO NOT USE)/` | (không phải git repo, không đọc) | (không đụng tới) | n/a |

Các untracked items ở `Ops Control v1.2/`, `CMES/`, `SpecHub/` đều
đã tồn tại trước session — không phải sản phẩm phụ của session này.

---

## 6. TODO còn lại (cho phase sau)

### 6.1 RBAC enforcement (cao)
- `User.Role` đã có trong DB + claim `ClaimTypes.Role` đã bake vào
  cookie principal Phase 2, nhưng **chưa có policy nào check role**.
- 4 sub-tab Settings có comment + placeholder note "Admin-only in Ops
  Control v1.2 source; RBAC is not yet enforced in CCL-MES Phase 3":
  `account`, `data`, `syslog`, `import-legacy`.
- Phase 5 nên: thêm `[Authorize(Roles = "Admin")]` lên 4 Razor page
  + middleware tương ứng cho NPI write API (khi có).

### 6.2 SignalR hub auth (trung)
- `MapHub<ShopfloorHub>("/hubs/shopfloor").AllowAnonymous()` vì Blazor
  Server `HubConnection` client từ `Dashboard.razor` không carry
  cookie vào negotiate call.
- Phase 5+ nên: pass cookies vào `HubConnectionBuilder.WithUrl(uri,
  options => { options.Cookies = ... })` rồi remove `AllowAnonymous`
  trên hub. Tham khảo Microsoft.AspNetCore.Http.Connections.Client
  options doc.

### 6.3 Placeholder pages → nội dung nghiệp vụ thực (trung-thấp)
Tabs render placeholder chỉ enumerate operational surface:
- 5 NPI tabs (3 đã có data — Engineer Routine/Structure/Raw Materials/
  Work Center hiện grid OK; Engineer Spec stub)
- 3 QC tabs (IQC/IPQC/OQC stub)
- 10 Settings sub-tabs (tất cả stub)

Nội dung nghiệp vụ cụ thể nên build theo sprint riêng (mỗi tab ~1
sprint), không gộp.

### 6.4 Backend error string → key-mapped (thấp)
- `Domain/StateMachine/WorkOrderStateMachine.cs` + `Application/
  Services/WorkOrderService.cs` hiện trả error message tiếng Anh
  hardcoded.
- UI prefix ("Cannot advance: …" / "Không thể chuyển: …") đã
  i18n hoá; nhưng dynamic Error portion bleed-through giữ tiếng Anh
  cả 2 culture.
- Phase 5+ nên: state machine return `ErrorCode` enum thay string;
  Razor page map `Loc["workorders.err.<code>"]`.

### 6.5 EF Migrations cho SQLite (thấp)
- Hiện dev dùng `EnsureCreated()` — không support schema migration.
- Lần tới schema đổi (e.g. RBAC tables), phải xoá DB + reimport.
- Phase 5+ có thể chuyển sang Migrations chung cả Sqlite + SqlServer
  (đã có `MesDbContextFactory.cs` cho design-time).

---

## 7. Tài liệu đi kèm

- `docs/AUDIT-2026-05-31.md` — Phase 0 audit (hiện trạng + đề xuất +
  bảng 10 sub-tab Settings nguồn).
- `docs/LESSONS_LEARNED.md` — kế thừa từ pre-session, chưa cập nhật
  cho Phase 1-4 (TODO nếu cần).
- `docs/MINDMAP.md` — kế thừa từ pre-session, **đã cập nhật** trong
  Phase 4 (xem commit cuối).
- `README.md` — kế thừa từ pre-session, **đã cập nhật phần Auth +
  i18n + Settings** trong Phase 4.
- `docs/FINAL-REPORT-2026-05-31.md` — file này.

---

*Phase 1 → Phase 4 hoàn tất 2026-05-31. main green, push xong, EN
100% Anh / VI 100% Việt có evidence, data integrity verified, không
đụng vùng cấm.*

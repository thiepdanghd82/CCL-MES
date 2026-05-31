# Phase 5 — Báo cáo đóng (2026-05-31)

> **Mục tiêu Phase 5**: hoàn tất 4 TODO còn lại từ FINAL-REPORT Phase 4 §6.
> **Trạng thái**: ✅ **HOÀN TẤT — 4/4 bước landed lên `main`**.

---

## 1. Tổng quan 4 bước

| Bước | Tên | Branch | Commit | PR vào main |
|---|---|---|---|---|
| 1 | RBAC enforcement | `feat/phase5-rbac` | `15313cc` | [#4](https://github.com/thiepdanghd82/CCL-MES/pull/4) ✅ |
| 2 | SignalR hub auth | `feat/phase5-hub-auth` | `1cc5b4b` | [#5](https://github.com/thiepdanghd82/CCL-MES/pull/5) ✅ |
| 3 | Error-string → WoErrorCode | `feat/phase5-error-codes` | `db42c8d` | [#8](https://github.com/thiepdanghd82/CCL-MES/pull/8) ✅ (replaced #6) |
| 4 | EF Migrations cho SQLite | `feat/phase5-ef-migrations` | `29cca38` | [#9](https://github.com/thiepdanghd82/CCL-MES/pull/9) ✅ (replaced #7) |

Tổng cộng: **38 file đụng (24 mới + 14 sửa), +4 245 LOC / −90 LOC** (chủ yếu auto-gen migration trong Bước 4).

> Ghi chú PR-numbering: PR #6 + #7 ban đầu là stacked PRs (base = branch của bước trước). Khi PR #5 merged + `--delete-branch`, PR #6 tự đóng do mất base; tương tự PR #7 sau khi PR #8 merge. Mỗi PR thay thế (#8, #9) được mở lại trỏ thẳng `main` với cùng head branch — toàn bộ commits + audit trail nguyên vẹn.

---

## 2. Chi tiết từng bước

### Bước 1 — RBAC enforcement (PR #4)

- **Đóng TODO**: 4 sub-tab admin-only (`/settings/account`, `/settings/data`, `/settings/syslog`, `/settings/import-legacy`) ở Phase 3 còn ghi "RBAC enforcement deferred to Phase 4+".
- **Phương án**: AuthorizationPolicy `AdminOnly = RequireRole("Admin")` + defence-in-depth 2 layer (UI hide `<AuthorizeView Roles="Admin">` + route gate `[Authorize(Policy = "AdminOnly")]`).
- **Mới**: `Services/AccessDenied.razor` component i18n + 4 key `access_denied.*` EN+VI.
- **Seed thêm**: `operator/operator` (Role=User) idempotent để test ngay.
- **Smoke**: admin thấy 10 dropdown items + vào được 4 admin tab; operator thấy 6 items + URL trực tiếp → `AccessDenied` panel.

### Bước 2 — SignalR hub auth (PR #5)

- **Đóng TODO**: `Program.cs:118-124` từ Phase 2: "Phase 4+ should pass cookies via HubConnectionBuilder options and remove this AllowAnonymous."
- **Phương án A** (sau khảo sát 3 lựa chọn trong [docs/PHASE5-STEP2-PLAN.md](PHASE5-STEP2-PLAN.md)): scoped `HubCookieAccessor` capture cookie từ `_Host.cshtml.cs` → forward qua `HubConnectionBuilder.WithUrl(opts.Cookies.Add(...))`.
- **Mới**: `Services/HubCookieAccessor.cs` (scoped, 1 instance/circuit).
- **Smoke**: anonymous negotiate → 401 (trước: 200 với `AllowAnonymous`); admin/operator authenticated → 200 + connectionId; logout-relogin cùng tab → cookie stale **không** xảy ra (forceLoad teardown sạch).

### Bước 3 — Backend error-string → WoErrorCode enum (PR #8, replaced #6)

- **Đóng 2 TODO comment**: `WorkOrderStateMachine.cs:11-14` + `WorkOrderService.cs:56-60` "Phase 4+ should swap to an error-code → resource-key map".
- **Đóng gap i18n cuối Phase 4**: dynamic error portion vẫn EN dù culture=VI ("Không thể chuyển: Requires machine setup confirmation").
- **Phương án A**: enum 9 value trong Domain language-free + dictionary map `code → resource key` ở Web; `AdvanceResult.Error` (string?) → `ErrorCode` (WoErrorCode?).
- **Mới**: `Domain/StateMachine/WoErrorCode.cs` + `Web/Services/WoErrorKeys.cs` + 10 key `workorders.error.*` EN+VI.
- **Cross-check**: 9 enum values + 9 dict entries + 10 resx keys/locale (9 mapped + 1 unknown fallback).
- **Smoke API**: `POST /api/workorders/99999/advance` → `{"errorCode":"WorkOrderNotFound"}` (chuyển từ free-form string sang enum name qua `JsonStringEnumConverter`).

### Bước 4 — EF Migrations cho SQLite (PR #9, replaced #7)

- **Đóng TODO cuối FINAL-REPORT §6**: "EF Migrations cho SQLite — hiện dùng `EnsureCreated()`, lần tới schema đổi phải xoá DB + reimport".
- **Phương án A**: Init migration (19 CreateTable + 22 CreateIndex khớp 100% live DB) + `DbInitializer.InitializeAsync` baseline-aware (cross-provider qua `IHistoryRepository`) thay branching `EnsureCreated()/Migrate()` trong `Program.cs`.
- **Mới**: `Infrastructure/Migrations/20260531050444_Init.cs` (~710 LOC auto-gen + manual review) + `Infrastructure/DbInitializer.cs` (~50 LOC) + `ef-migrate.sh` 2-mode (`--sqlite | --sqlserver` + `add <Name>` subcommand).
- **Test methodology** (rủi ro mất data CAO NHẤT Phase 5):
  - Phase A: backup `ccl_mes.db.bak.phase5migr-20260531-120743` + SHA256
  - Phase B: test trên `ccl_mes.db.testcopy` trước, verify baseline ran + row counts unchanged + restart no-op
  - Phase C: áp DB thật, cùng sequence, verify lần 2
- **DbInitializer hành vi**:
  - New install (no tables) → `Migrate()` tạo schema + record Init
  - Existing install (tables + no history) → baseline insert qua `IHistoryRepository.GetInsertScript`, sau đó Migrate là no-op
  - Subsequent restart → Migrate no-op (history hợp lệ)

---

## 3. Issue đã fix + cải tiến

| # | Khu vực | Issue trước | Cải tiến Phase 5 |
|---|---|---|---|
| 1 | Settings dropdown | 4 admin-only tab visible cho mọi user, URL trực tiếp vẫn vào được | Layer 1 (UI hide) + Layer 2 (route gate) — defence-in-depth |
| 2 | `/hubs/shopfloor` | `AllowAnonymous()` workaround từ Phase 2 — bất kỳ ai cũng connect được hub | Cookie forward qua scoped accessor — FallbackPolicy enforce |
| 3 | `WorkOrders.razor` advance fail message | "Không thể chuyển: **Requires machine setup confirmation (SetupConfirmed)**" — dynamic portion EN giữa VI text | Toàn bộ message localize: "Không thể chuyển: **Cần xác nhận setup máy**" |
| 4 | `Domain/StateMachine` | 8 string EN hardcoded trong guard | Enum `WoErrorCode` 9 value — Domain language-free |
| 5 | API wire format `AdvanceResult` | `{"error": "<EN string>"}` | `{"errorCode": "RequiresSetupConfirmed"}` — enum NAME qua JsonStringEnumConverter |
| 6 | `Program.cs` DB init | `if (provider == SqlServer) Migrate() else EnsureCreated()` — SQLite không bao giờ qua migration | `DbInitializer.InitializeAsync` chung cho cả 2 provider, baseline-aware |
| 7 | Schema change quy trình | Operator phải xóa DB + reimport mỗi khi entity đổi | `dotnet ef migrations add <Name>` rồi restart, baseline tự xử lý |
| 8 | Auth state machine | Không phân biệt anonymous vs authenticated-sai-role | `App.razor <NotAuthorized>` split → RedirectToLogin vs AccessDenied component |

**Demo accounts seeded idempotent**:
- `admin / admin` (Role=Admin) — đã có từ Phase 2
- `operator / operator` (Role=User) — mới ở Bước 1

---

## 4. Data integrity (verify sau khi merge xong)

### 4.1 Backup chain (4 backup tích lũy Phase 5)

| Backup | Khi | SHA256 |
|---|---|---|
| `ccl_mes.db.bak.phase5rbac-20260531-111842` | Pre-Bước 1 | (chain start) |
| `ccl_mes.db.bak.phase5hubauth-20260531-113445` | Pre-Bước 2 | (gitignored: `*.db.bak*`) |
| `ccl_mes.db.bak.phase5errcodes-20260531-114918` | Pre-Bước 3 | |
| `ccl_mes.db.bak.phase5migr-20260531-120743` | Pre-Bước 4 | `9a810bc91ea882cc33446bd2b73bbc76969ab06bf3e15be99fcedbc16fc4ed63` |
| `ccl_mes.db.bak.phase5-close-20260531-121523` | Pre-merge cuối | `46d186688f92c6f5de9cab256f50efcddb038e2dac24e2f3d3f761114055e5b4` |

### 4.2 Row count audit (verify trên main sau merge cuối)

| Bảng | Phase 1 baseline | Sau Phase 5 đóng | Δ |
|---|---|---|---|
| WorkCenters | 43 | **43** | 0 ✓ |
| RawMaterials | 2 127 | **2 127** | 0 ✓ |
| RoutingOperations | 38 441 | **38 441** | 0 ✓ |
| ManufacturingStructures | 20 530 | **20 530** | 0 ✓ |
| Users | 1 (admin) | **2** (admin + operator) | +1 ✓ (idempotent seed Bước 1) |
| WorkOrders | 1 (seed WO-26-3683) | **1** | 0 ✓ |
| `__EFMigrationsHistory` | absent | **1 row** (`20260531050444_Init / 10.0.8`) | +1 ✓ (Bước 4) |

### 4.3 Restart proof

Boot 1 sau merge cuối:
```
SELECT COUNT(*) FROM sqlite_master WHERE name = '__EFMigrationsHistory'
SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId
No migrations were applied. The database is already up to date.
Now listening on: http://localhost:5080
```

Boot 2 (sau kill server rồi restart):
```
No migrations were applied. The database is already up to date.
GET / → 302 (FallbackPolicy redirect to login)
```

Final main DB SHA256: `36485d2a2ad9ec19ce787146dbeac1f81953c781d047914dae2b151383f590b2`. Khác pre-merge SHA chỉ vì admin `LastLoginAt` cập nhật sau smoke test (entity-level, không ảnh hưởng NPI row counts).

---

## 5. Vùng cấm nguyên vẹn

| Directory | Trạng thái |
|---|---|
| `Ops Control v1.2/` | [PRESENT] không đụng (read-only reference từ Phase 0) |
| `CMES/` | [PRESENT] không đụng |
| `Old ver ( DO NOT USE)/` | [PRESENT] không đụng |
| `SpecHub/` | [PRESENT] không đụng |

Toàn bộ thay đổi Phase 5 nằm trong `CCL-CMES/CCL-MES/` (cd PROJECTS/CCL-CMES/CCL-MES). Repo Ops Control v1.2 git log không có commit nào từ git user `v1.3 autonomous upgrade` trong sprint Phase 5.

---

## 6. Smoke matrix sau merge cuối (verify trên `main` HEAD `88f01b8`)

| # | Test | Kết quả |
|---|---|---|
| 1 | `dotnet build` (Domain + Application + Infrastructure + Web) | **0 warning, 0 error** |
| 2 | Boot 1 — `No migrations were applied` | ✓ |
| 3 | Bước 1 (RBAC) — admin `/settings/account` → npi-placeholder | ✓ |
| 4 | Bước 1 (RBAC) — operator `/settings/account` → access-denied | ✓ |
| 5 | Bước 2 (Hub auth) — anonymous negotiate → **401** | ✓ |
| 6 | Bước 2 (Hub auth) — admin negotiate → 200 + connectionId | ✓ |
| 7 | Bước 3 (Error code) — `POST /api/workorders/99999/advance` → `{"errorCode":"WorkOrderNotFound"}` | ✓ |
| 8 | Bước 4 (EF Migrations) — `__EFMigrationsHistory` chứa `20260531050444_Init` | ✓ |
| 9 | Row counts 43/2 127/38 441/20 530/2 unchanged | ✓ |
| 10 | Boot 2 — Migrate no-op + HTTP 302 GET / | ✓ |

---

## 7. TODO còn lại sau Phase 5 (Phase 6+)

| # | Khu vực | Mô tả |
|---|---|---|
| 1 | Nội dung nghiệp vụ 3 QC tab | IQC / IPQC / OQC hiện chỉ là placeholder Razor page với 3 bullet. Cần checklist động + lookup vào lib coverage |
| 2 | Nội dung nghiệp vụ 1 NPI tab | Engineer Spec — gắn vào Spec Control hiện có, versioning + approval |
| 3 | Nội dung nghiệp vụ 10 Settings tab | My Profile / My Password / Appearance / Hardware / Mode + 4 admin tab (Account Control / Backup / Logs / Import-legacy) — UI thực, không placeholder |
| 4 | Deploy SQL Server thật | Bước 4 đã chuẩn bị migration provider-agnostic + `ef-migrate.sh --sqlserver`. Cần ops chạy `appsettings.SqlServer.json` + verify trên SQL Server instance |
| 5 | RBAC roles ngoài Admin/User | Phase 5 chỉ có 2 role. Future: Supervisor (xem dashboard + duyệt QC), Operator (chỉ Start/Pause/Resume/Finish), QA Lead, etc. |
| 6 | Hub auth — `HubConnection` reconnect sau 8h cookie expire | Khi circuit sống idle >8h, cookie sliding refresh cần re-fetch. Hiện chưa giải quyết. Pattern: bổ sung endpoint re-fetch cookie qua circuit + AccessTokenProvider |
| 7 | Audit log cho RBAC events | RBAC violations + role changes nên log vào audit history. Hiện chưa có audit log entity |
| 8 | Test suite | Project chưa có unit test framework. Phase 6 nên thêm xUnit cho Domain + Application; Playwright cho Blazor flow |

---

## 8. Cross-reference Phase 4 → Phase 5

| Phase 4 FINAL-REPORT §6 TODO | Phase 5 đóng ở | Trạng thái |
|---|---|---|
| RBAC enforcement on 4 admin tab | Bước 1 (PR #4) | ✅ |
| SignalR hub auth (gỡ `AllowAnonymous`) | Bước 2 (PR #5) | ✅ |
| Backend error-string → error-code | Bước 3 (PR #8) | ✅ |
| EF Migrations cho SQLite | Bước 4 (PR #9) | ✅ |
| Real business content cho 3 QC + 1 NPI + 10 Settings tab | Phase 6+ | Pending |

Phase 5 đóng **4/5** TODO từ Phase 4 FINAL-REPORT §6 (mục cuối là nội dung nghiệp vụ thực, scope khác).

---

## 9. Kết luận

✅ Phase 5 **hoàn tất**. 4 bước landed lên `main`, build clean, 4-step smoke pass, data NPI nguyên vẹn (43/2 127/38 441/20 530/2), restart proof, vùng cấm không bị đụng.

Pre-Phase-5 `main` HEAD: `eb75be8` (Phase 4 docs).
Post-Phase-5 `main` HEAD: `88f01b8` (PR #9 merge).
Tổng commits sprint Phase 5: 4 commit chính + 4 merge commit.

Dev box đã sẵn sàng cho Phase 6.

*Cập nhật: 2026-05-31, sau Bước 4 đóng Phase 5.*

---
name: cmes-secrets-jwt
description: >
  Bí mật JWT, mật khẩu seed, refresh token, dual-sig QC, Swagger của
  CCL-MES Hybrid API. Dùng khi đụng Jwt:SigningKey, AuthController,
  InMemoryRefreshTokenStore, MauiSecureTokenStore, login lockout, hoặc
  cờ OPS_IPQC_* / OPS_OQC_*. Cấm commit key prod và tắt 4-mắt "cho nhanh".
---

# CMES secrets + phiên JWT

**Rule:** key ký token và mật khẩu xưởng không nằm git. Dual-sig default-ON
là cửa chất lượng, không phải feature flag demo.

## JWT

File: `CCL-MES-Hybrid/src/CCL.MES.Api/appsettings.json`  
Placeholder: `REPLACE-IN-PROD-Jwt__SigningKey-...` (≥32 byte UTF-8).

Prod: env `Jwt__SigningKey` hoặc `appsettings.Production.json` (**gitignore**).
Rotate key = mọi máy phải login lại.

Issuer `ccl-mes-api` / Audience `ccl-mes-hybrid` — validate cả hai (đã bật).
Access ~15 phút, refresh ~7 ngày. SignalR đưa token qua query `access_token`
trên `/hubs` — **chỉ chấp nhận trên LAN tin cậy**; hướng HTTPS + không log query.

Swagger: `UseSwagger` chỉ `IsDevelopment()`. `ASPNETCORE_ENVIRONMENT=Production`
trên xưởng. Development + LAN = lộ schema.

## Refresh store (nợ P10.1)

`InMemoryRefreshTokenStore` — restart API = hết phiên. Revoke không bền.
Pilot OK; **không** tuyên bố "đã xong session quản lý thiết bị".
Đổi sang bảng hashed = skill này + `cmes-migration-abc` (W1).

Reuse-detection (revoke family) **giữ**. Đừng nới.

## Client Mac — Keychain

`MauiSecureTokenStore`: Keychain / DPAPI. Ad-hoc thiếu entitlement → fallback
RAM, log một lần. **Release / bản xưởng:** thiếu Keychain = **fail closed**,
không lén RAM. Xem `cmes-macos-ship`.

Không `Console.Write` access/refresh token.

## Mật khẩu + lockout

Seed `admin/admin` … (pwd = username) **chỉ** DB trống. Trên live: đổi hết
trước khi mở LAN. `scripts/RecoverAdmin/` + `CONFIRM-RECOVER` khi mất Admin.

**Chưa có** lockout / rate-limit login (Phase 7 treo). Đừng thêm endpoint
login mới mà không có delay/lockout. Brute-force HTTP LAN là kịch bản thật.

Hasher: `PasswordHasher<User>` PBKDF2 — giữ. Audit `LoginFail` không phân biệt
user-tồn-tại vs sai-pwd (đã làm). Detail JSON **cấm** password/hash/cookie.

## Dual-sig — không tắt

| Env | Mặc định |
|---|---|
| `OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER` | ON (typo → vẫn ON, L20) |
| `OPS_OQC_REQUIRE_DISTINCT_REVIEWER` / `_APPROVER` / `_APPROVER_DISTINCT_FROM_INSPECTOR` | ON |

Checkpoint 7d/7e **từ chối chạy** khi flag OFF. Tắt = cùng người duyệt QC
của mình. STOP-gate, hỏi Henry.

## Checklist PR đụng auth

- [ ] Không commit SigningKey thật  
- [ ] Không log token  
- [ ] `[Authorize]` / FallbackPolicy còn; login/refresh/health mới phải giải thích AllowAnonymous  
- [ ] Cờ 4-mắt không đổi default  
- [ ] `curl` 401 sai pwd + 200 login (lab DB), dán output

## Do NOT

- Hardcode key "tạm" trong `appsettings.json` rồi quên.  
- `OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER=off` để demo một người.  
- Persist refresh plaintext không hash.

---
name: cmes-macos-ship
description: >
  Đóng gói CCL MES Mac Catalyst đúng Apple Developer / Gatekeeper — ký
  Developer ID, notarize, Keychain entitlement, ATS, camera, Privacy
  Manifest. Dùng khi entitlements, Info.plist, SecureStorage
  MissingEntitlement, notarization, phân phối máy xưởng, hoặc "up App Store".
  Không nhắm Mac App Store. Skill nền tảng: desktop-app (user).
---

# CMES macOS ship — Developer ID, không App Store

**Rule:** app xưởng = **Developer ID Application + notarize + staple**.
Không nộp Mac App Store (sandbox + HTTP LAN + API :5100 cạnh máy = không khớp).

Bundle: `com.ccl.mes.hybrid` · project `CCL-MES-Hybrid/src/CCL.MES.Hybrid`.
Q6: Win + Mac desktop; iOS/Android hoãn.

## Entitlements + Keychain

Repo từng **không** có `Entitlements.plist`. Ad-hoc →
`SecureStorage` `MissingEntitlement` → `MauiSecureTokenStore` fallback RAM.

Bản xưởng bắt buộc:

- `keychain-access-groups` (Team ID + app id)  
- network client (HTTPS/LAN)  
- Release: **fail closed** nếu Keychain throw — không `_fallback` RAM  

Camera: `NSCameraUsageDescription` đã có (VI). Thêm `InfoPlist.strings` EN+VI.
Thêm `NSLocalNetworkUsageDescription` nếu gọi API máy khác trên LAN.

`PrivacyInfo.xcprivacy` — camera, file timestamp, UserDefaults (MAUI SDK).
`ITSAppUsesNonExemptEncryption` nếu có ngày nộp Store; nội bộ thì ghi nhận
chỉ HTTPS/JWT HMAC (thường exempt).

## ATS

Hiện: `NSAllowsLocalNetworking` + exception **localhost / 127.0.0.1** cleartext.
**Cấm** `NSAllowsArbitraryLoads`.

Xưởng: API **HTTPS** (cert nội bộ hoặc terminator). Exception IP
`10.x` chỉ bundle station-specific, rebuild — không nới toàn cục.

`NSExceptionMinimumTLSVersion` TLSv1.0 trên localhost là nợ dev; prod TLS 1.2+.

## Debug vs Release

P10.2 từng bật `WKWebView.Inspectable = true`. **Release = false.**

Print: native `IPrintService` / `UIPrintInteractionController` — không
`window.print()` (skill `cmes-spec-print`).

## Pipeline ký (mục tiêu)

```text
dotnet publish -f net10.0-maccatalyst -c Release
codesign --options runtime --timestamp ...  # Hardened Runtime
xcrun notarytool submit ... --wait
xcrun stapler staple CCL\ MES.app
```

Chứng chỉ: Apple Developer **Organization** CCL, Developer ID Application.
MDM / pkg nội bộ sau staple. USB un-notarized = Gatekeeper chặn — đúng.

Xcode/SDK: Catalyst build đỏ vì lệch Xcode ≠ lỗi nghiệp vụ. Ghi rõ trong PR.

## Checklist trước đưa máy xưởng

- [ ] Signing Team ≠ ad-hoc  
- [ ] Keychain ghi/đọc token qua restart app (không fallback log)  
- [ ] Camera dialog hiện đúng câu usage; deny → banner Settings  
- [ ] ATS: không ArbitraryLoads; BaseUrl prod là `https://`  
- [ ] Inspectable false trên binary Release  
- [ ] `spctl --assess --verbose` / Gatekeeper pass sau staple  

## Do NOT

- Bảo "tắt Gatekeeper giúp em".  
- Nộp App Store "cho có compliance".  
- Ship ad-hoc rồi coi Keychain đã xong.  
- Nới ATS vì login fail — sửa URL/cert, đừng mở internet.

---
name: cmes-state-contract
description: >
  Luật thay đổi state machine Work Order / Leg trong CCL-MES — MesPhase 14
  state, ProcessStepCode 8 state legacy, LegPhase, và projection một chiều.
  Dùng khi chạm WorkOrderStateMachine, MesPhase, LegPhase, endpoint /advance,
  hoặc bất kỳ transition nào. Contract có chữ ký; code phải theo contract.
---

# CMES state contract

**Rule (enforced):** `CCL-MES-Hybrid/docs/P10.7-WO-STATE-CONTRACT.md` là
**nguồn sự thật**. Code theo contract, không phải contract theo code. Muốn
thêm một transition chưa có trong contract ⇒ **sửa contract trước, có chữ ký,
rồi mới code**. Đây là STOP-gate.

## Hình dạng hiện tại

```
MesPhase (canonical, 14):
  NEW → PREPRESS → [SPLIT nếu ≥2 leg] → SETTING → IPQC_WAIT
      → QA_PENDING → IPQC_APPROVED → RUNNING ⇄ PAUSED
      → DONE(transient) → FQC_PENDING → OQC_PENDING → SHIPPED
  CANCELLED = terminal, admin/sys-only

ProcessStepCode (legacy, 8): PrePressCheck → OpSetting → IpqcApproval
      → ReadyToRun → Running → Fqc → Oqc → Closed

Projection: MesPhase → ProcessStepCode là MỘT CHIỀU + deterministic
            (WorkOrderStateMachine.ProjectToLegacy). Cặp gộp
            (NEW/PREPRESS, IPQC_WAIT/QA_PENDING, RUNNING/PAUSED,
             DONE/CANCELLED, SPLIT→PrePressCheck) là CỐ Ý.
```

## Luật additive — vì sao hai mô hình sống chung được

Khi mở rộng: **append cuối enum**, giữ nguyên giá trị số của mọi thành viên
cũ, thêm projection về hình legacy. `SHIPPED = 12` và `SPLIT = 13` được thêm
đúng theo luật này nên không phải migrate dữ liệu cũ.

**Cấm:** đổi giá trị số, đổi nghĩa một state đã production, xoá state.
`DONE` là ví dụ mẫu — nghĩa bị **thu hẹp** (terminal → transient) nhưng
dòng dữ liệu cũ vẫn hợp lệ vì state machine giữ nhánh terminal cho legacy row.

## Trước khi sửa — trả lời 5 câu

1. Transition này đã có trong contract §3.1 chưa? Chưa ⇒ STOP.
2. Nó nằm ở tầng **WO** hay tầng **leg**? (Leg dùng `LegPhase`, không phải `MesPhase`.)
3. Guard là gì, và guard đó **fail** thì trả `WoErrorCode` nào?
4. Emit audit row hình dạng nào? (xem skill `cmes-audit-emit`)
5. Concurrency: RowVersion nào bảo vệ? Idempotency-Key có bắt buộc không?

## Bất biến phải giữ

- **Server-authoritative.** Client không bao giờ tự quyết phase; nó gọi
  endpoint và nhận state mới. Không có state machine bản sao trên client.
- **Projection một chiều.** Không viết hàm ngược `ProcessStepCode → MesPhase`.
- **Guard trả `WoErrorCode`, không trả chuỗi.** Web layer map code → i18n key.
- **Join leg:** WO chỉ rời `SPLIT` khi **mọi leg terminal** đạt `LEG_DONE`.
  WO 1-leg **không bao giờ** vào `SPLIT`.
- **Không có transition ra khỏi `SHIPPED`.** Rework = mở WO mới tham chiếu WO cũ.

## Bằng chứng bắt buộc

- `WorkOrderStateMachineLegacyParityTests` xanh (khoá hợp đồng legacy).
- Ma trận transition mới: dán bảng `from → to → guard → error code`.
- Test concurrency nếu transition ghi dữ liệu: N thread → 1 winner, N-1 × 409.

## Do NOT

- Thêm `if (phase == ...)` rải rác trong controller — luật transition sống
  trong `WorkOrderStateMachine`, không nằm ở tầng HTTP.
- Dùng `MesPhase` cho leg hoặc `LegPhase` cho WO.
- Sửa contract và code trong cùng một commit mà không tách phần chữ ký.

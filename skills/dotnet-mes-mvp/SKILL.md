---
name: dotnet-mes-mvp
description: "Dựng khung MVP hệ thống MES (Manufacturing Execution System) cho nhà máy in/sản xuất bằng .NET + EF Core + Blazor theo Clean Architecture. Dùng khi cần tạo nhanh phần mềm kiểm soát Work Order theo process flow nhiều bước (state machine), Spec Control online, QC (IPQC/FQC/OQC), OEE/Production Log, và Work Instruction số hóa. Trigger khi người dùng nói về MES, Work Order control, process flow, OEE, spec control, work instruction, hoặc nhà máy CCL Design / Brady."
license: Proprietary - CCL Design internal
---

# Dựng MVP MES bằng .NET (Clean Architecture)

Skill này đóng gói cách dựng một hệ thống MES tối thiểu-nhưng-chạy-được cho nhà máy in nhãn/label, đã áp dụng thực tế cho dự án **CCL-MES**.

## Khi nào dùng

- Cần phần mềm điều phối **Work Order** theo process flow nhiều bước có kiểm soát (guard + phân quyền).
- Cần **Spec Control online** (version + approval) gắn cứng vào WO.
- Cần **QC** số hóa (IPQC/FQC/OQC, checklist, approval, chặn WO khi fail).
- Cần đo **OEE / hiệu suất máy** (Run/Stop/Setup, sản lượng).
- Cần **Work Instruction** điện tử theo sản phẩm/bước/máy.

## Tech stack chuẩn

- **.NET** (khớp SDK máy đích — kiểm tra `dotnet --version` trước; CCL dùng net10.0)
- **EF Core** + **SQLite** (dev) → **SQL Server** (prod)
- **Blazor Server** + **ASP.NET Core Web API** + **Swagger**
- **Clean Architecture**: `Domain` → `Application` → `Infrastructure` → `Web`

## Quy trình dựng (theo thứ tự)

1. **Hỏi rõ phạm vi**: CSDL dev (SQLite/SQL Server), phạm vi UI (API-only / + Blazor), module nào (WO/Spec/QC/OEE/WI).
2. **Kiểm tra SDK**: `dotnet --version` để chọn đúng `<TargetFramework>` và version package EF (`Major.0.0` khớp .NET major).
3. **Tạo 4 project** theo cấu trúc dưới, solution `.sln` tham chiếu cả 4.
4. **Domain trước**: Enums, Entities (BaseEntity có CreatedAt/By...), và **State Machine** cho Work Order.
5. **Application**: interface `IMesDbContext`, các `Service` (CQRS-lite), DTOs.
6. **Infrastructure**: `DbContext` (cấu hình `.HasConversion<string>()` cho MỌI enum, `Ignore` computed members), `DbSeeder`, DI.
7. **Web**: Controllers (REST) + Swagger + trang Blazor; bật `ReferenceHandler.IgnoreCycles` + `JsonStringEnumConverter`.
8. **Seed dữ liệu mẫu sát thực tế** để demo + bắt lỗi sớm.
9. **Chạy & kiểm chứng**: `dotnet run --project src/<Web>`, kiểm tra `/swagger`, `/workorders`, `/dashboard`.

## Cấu trúc thư mục mẫu

```
<Project>.sln
src/
  <P>.Domain          # Entities, Enums, StateMachine (guard chuyển bước)
  <P>.Application     # IMesDbContext, Services, DTOs
  <P>.Infrastructure  # EF Core DbContext (SQLite), DbSeeder, DI
  <P>.Web             # API + Swagger + Blazor (Dashboard, WO, WI)
```

## Mẫu State Machine (cốt lõi)

Đóng gói toàn bộ luật chuyển bước vào một hàm `CanAdvance(wo)` trả về `(bool Allowed, string? Reason)`. Mỗi cặp `(from, to)` có guard riêng (vd: chuyển khỏi Pre-press cần Spec Approved + MaterialsReady; qua Ready cần IPQC Pass; qua FQC cần ProducedQty > 0; đóng WO cần OQC Pass + RoHS). Xem `src/<P>.Domain/StateMachine/WorkOrderStateMachine.cs` của dự án CCL-MES làm chuẩn.

## Công thức OEE (đã kiểm chứng)

```
Availability = Run / (Run + Stop + Setup)
Performance  = min(1.0, IdealCycleTime × TotalCount / Run)
Quality      = Good / (Good + Reject)
OEE          = Availability × Performance × Quality
```

Luôn viết một bài test đối chiếu số (ví dụ chuẩn Vorne: A 88.8% × P 86.1% × Q 97.8% = OEE 74.8%) trước khi tin công thức.

## Bẫy thường gặp (xem thêm docs/LESSONS_LEARNED.md)

- `<TargetFramework>` phải khớp SDK máy đích, nếu không lỗi "runtime not found".
- Quên `.HasConversion<string>()` → enum lưu thành số.
- Computed property/method (vd `DurationMinutes`, `LastQc`) phải `Ignore()` khỏi EF.
- `EnsureCreated()` chỉ cho demo; production dùng EF Migrations.
- `cd` đúng thư mục chứa `.sln` trước khi `dotnet run`.
- Bật `ReferenceHandler.IgnoreCycles` để tránh vòng lặp JSON khi serialize entity quan hệ 2 chiều.

## Mở rộng sau MVP

RBAC/Entra ID · SignalR realtime · tích hợp SAP & Warehouse · thu OEE tự động từ PLC (OPC-UA/Modbus) · unit test cho state machine & OEE.

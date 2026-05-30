# Lessons Learned — Dự án CCL-MES (MVP)

Tài liệu này ghi lại những bài học rút ra trong quá trình thiết kế & dựng khung MVP hệ thống MES cho nhà máy CCL Design. Cập nhật dần theo từng giai đoạn.

---

## 1. Bối cảnh & quyết định kiến trúc

| Quyết định | Lý do | Bài học |
|---|---|---|
| Clean Architecture (Domain / Application / Infrastructure / Web) | Tách nghiệp vụ khỏi framework, dễ test, dễ thay DB | Đáng giá ngay cả với MVP — khi đổi SQLite → SQL Server chỉ sửa 1 dòng ở tầng Infrastructure |
| SQLite cho dev, SQL Server cho prod | Chạy được ngay bằng `dotnet run`, không cần cài DB server | EF Core viết chuẩn provider-agnostic thì chuyển đổi gần như miễn phí |
| State Machine cho Work Order | Luật chuyển 7 bước tập trung 1 chỗ, có guard | Không rải logic "if status ==" khắp nơi; mọi thay đổi quy trình chỉ sửa `WorkOrderStateMachine` |
| Lưu enum dạng string trong DB | Đọc DB dễ hiểu (PrePressCheck thay vì 1) | Phải khai báo `.HasConversion<string>()` cho TẤT CẢ enum, nếu quên sẽ lưu số |

## 2. Bài học kỹ thuật (.NET / EF Core)

- **Target framework phải khớp SDK của máy chạy.** Máy CCL dùng .NET 10 (SDK 10.0.300) nên phải đổi 4 file `.csproj` từ `net8.0` → `net10.0` và version package EF/Extensions tương ứng (`10.0.0`). Nếu để net8.0 mà máy không có runtime 8 sẽ lỗi *"framework 'Microsoft.NETCore.App' version '8.0.0' was not found"*.
- **Computed property không được map vào DB.** `WorkOrder.LastQc(...)` (method) và `ProductionLog.DurationMinutes` (get-only) phải `Ignore(...)` trong `OnModelCreating`, nếu không EF cố tạo cột và build/migrate lỗi.
- **`EnsureCreated()` chỉ hợp cho demo.** Nó tạo schema 1 lần, KHÔNG hỗ trợ thay đổi schema sau này. Sang production phải chuyển sang **EF Core Migrations** (`dotnet ef migrations add`, `database update`).
- **Tránh vòng lặp JSON khi serialize entity có quan hệ 2 chiều** (WorkOrder ↔ QcInspection). Đã bật `ReferenceHandler.IgnoreCycles` + `JsonStringEnumConverter` trong `Program.cs`.
- **`cd` đúng thư mục trước khi `dotnet run`.** Lỗi `MSB1003: Specify a project or solution file` chỉ vì đang đứng ở `~`. Luôn `cd` vào thư mục chứa `.sln`.
- **Comment `#` dán cùng dòng lệnh trong zsh** gây lỗi `unknown file attribute: h`. Khi hướng dẫn lệnh, để comment ở dòng riêng.

## 3. Bài học về OEE

- **Định nghĩa "Planned time" phải rõ ràng.** Trong model này `Planned = Run + Stop + Setup`. Nếu muốn khớp ví dụ chuẩn ngành (Vorne) thì loại break ra khỏi planned.
- **Performance phải chặn trần 100%.** Do sai số đo cycle-time, `idealMin/runMin` có thể > 1; dùng `Math.Min(1.0, ...)`.
- **Đã kiểm chứng công thức** khớp ví dụ chuẩn ngành: Availability 88.8% × Performance 86.1% × Quality 97.8% = **OEE 74.8%**. Luôn viết một test đối chiếu số trước khi tin vào công thức.

## 4. Bài học quy trình làm việc

- **Hỏi rõ phạm vi trước khi code** (CSDL dev, phạm vi UI, module nào) giúp không làm thừa.
- **Dựng MVP chạy được rồi mới mở rộng.** Bản đầu chỉ WO+Spec+QC; sau khi user chạy OK mới thêm OEE/WI/Dashboard.
- **Seed dữ liệu mẫu sát thực tế** (Brady Asia, BRD-7656-D, WO-26-3683, máy ACNC3) giúp demo trực quan và phát hiện lỗi sớm.

## 5. Việc cần làm tiếp (carry-over)

- [ ] Chuyển `EnsureCreated()` → EF Migrations + cấu hình SQL Server.
- [ ] Thêm xác thực & phân quyền (RBAC / Entra ID).
- [ ] SignalR realtime cho Dashboard (hiện đang load khi mở trang).
- [ ] Tích hợp SAP (đơn hàng, vật tư, costing) và Warehouse.
- [ ] Thu thập OEE tự động từ PLC (OPC-UA/Modbus) thay vì bấm tay.
- [ ] Unit test cho `WorkOrderStateMachine` và `OeeService`.

---

*Cập nhật lần cuối: 30/05/2026 — sau khi hoàn thiện MVP (6 module) và chạy thành công trên máy .NET 10.*

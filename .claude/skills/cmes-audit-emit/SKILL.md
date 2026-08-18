---
name: cmes-audit-emit
description: >
  Hình dạng bắt buộc của audit row trong CCL-MES và luật "mọi mutation phải
  emit". Bao gồm danh sách trường cấm xuất hiện trong detail JSON, quy tắc
  đặt AuditAction code, và quan hệ với bằng chứng bất biến (WoTraceSnapshot).
  Dùng khi thêm/sửa bất kỳ đường ghi dữ liệu nào.
---

# CMES audit emit

**Rule (enforced):** mỗi đường ghi dữ liệu nghiệp vụ phát đúng một audit row
qua `IAuditWriter.EmitAsync(action, actor, role, targetType, targetId, detail)`.
Bảng `AuditLogs` là **append-only** — không UPDATE, không DELETE, không sửa
dòng cũ khi "phát hiện sai". Sai thì emit dòng đính chính mới.

## Hình dạng

```csharp
await _audit.EmitAsync(
    action:     AuditAction.WoAdvance,   // const trong Domain/Audit/AuditAction.cs
    actor:      user.UserName,
    role:       user.Role.ToString(),
    targetType: "WorkOrder",
    targetId:   wo.Id.ToString(),
    detail:     JsonSerializer.Serialize(new { from, to, legId, reason }));
```

## Detail JSON — danh sách CẤM

Tuyệt đối không xuất hiện, kể cả đã hash hay cắt bớt:

```
password  passwordHash  pwd  hash  salt  token  refreshToken
jwt  cookie  authorization  bearer  secret  apiKey  connectionString
```

Lý do: `AuditLogs` xuất được ra CSV qua `AuditLogExportController` và đọc
được ở `Settings → System Log` bởi Admin. Bất cứ thứ gì vào detail đều coi
như đã rời khỏi vùng bảo mật.

## AuditAction code

- Hằng số trong `src/CCL.MES.Domain/Audit/AuditAction.cs`, **sắp xếp alphabet**.
- Thêm code mới = append + giữ nguyên chuỗi của code cũ (dữ liệu cũ tham chiếu chuỗi).
- Một hành động nghiệp vụ = một code. Không dùng lại code của hành động khác
  vì "gần giống".

## Audit ≠ bằng chứng chất lượng

Hai thứ khác nhau, đừng gộp:

| | `AuditLogs` | `WoTraceSnapshot` |
|---|---|---|
| Trả lời | **ai** làm **gì** lúc **nào** | sản phẩm này được làm ra **như thế nào** |
| Tính chất | append-only, ghi liên tục | **đóng băng** tại mốc phase |
| Người đọc | Admin điều tra sự cố | khách hàng audit chất lượng |
| Sửa được? | Không | Không — và không được upsert đè |

Cập nhật `WoTraceIndex` (mutable) **không bao giờ** chạm snapshot đã đóng băng.

## Checklist

- [ ] Mọi nhánh ghi thành công đều emit — kể cả nhánh sớm (early return)
- [ ] Emit **sau** khi `SaveChangesAsync` thành công, cùng transaction ngữ nghĩa
- [ ] Detail không chứa từ khoá trong danh sách cấm
- [ ] `targetType` là tên entity, `targetId` là khoá chính dạng chuỗi
- [ ] Action code đã có trong `AuditAction`, alphabet đúng chỗ
- [ ] Test dùng `InMemoryAuditWriter` khẳng định đúng số dòng + đúng action
- [ ] `bash CCL-MES-Hybrid/scripts/gate-audit-emit.sh` xanh

## Do NOT

- Emit trong `catch` rồi nuốt exception — audit nói "thành công" khi đã fail.
- Ghi cả object entity vào detail (`JsonSerializer.Serialize(user)` kéo theo hash).
- Dùng `_db.AuditLogs.Add(...)` trực tiếp thay vì `IAuditWriter`.

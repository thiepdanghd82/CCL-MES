---
name: cmes-rbac-matrix
description: >
  Ma trận phân quyền 5 role của CCL-MES (Admin/Supervisor/Engineer/QC/Operator)
  và luật enforce 3 tầng. Dùng khi thêm màn hình, endpoint, nút bấm, hoặc bất
  kỳ thứ gì có điều kiện "ai được làm". Ẩn nút KHÔNG phải là phân quyền.
---

# CMES RBAC matrix

**Rule (enforced):** phân quyền enforce ở **3 tầng**, và tầng server là tầng
duy nhất tính. Ẩn nút trên UI là trải nghiệm, không phải bảo mật.

| Tầng | Cơ chế | Vai trò thật |
|---|---|---|
| Role | `User.Role` (5 giá trị) | cổng thô |
| Policy | `[Authorize(Policy="...")]` trên endpoint/page | **cổng thật** |
| Inline | `<AuthorizeView Roles="...">` | chỉ để không hiện nút vô nghĩa |

**5 role:** `Admin` · `Supervisor` · `Engineer` · `QC` · `Operator`. Không
thêm role thứ 6 mà không sửa ma trận + test — role mới rơi vào mặc định
"không được gì" chứ không phải "được mọi thứ".

## Luật vàng

1. **Mọi endpoint mutation phải có `[Authorize(Policy=...)]` tường minh.**
   Dựa vào `FallbackPolicy = RequireAuthenticatedUser` nghĩa là "ai đăng nhập
   cũng ghi được" — sai gần như luôn.
2. **RBAC-by-omission trên UI:** chỉ *dựng* item mà user được phép
   (`RowContextMenu` không có item ⇒ không mở menu). Không dựng rồi disable.
3. **Server vẫn phải 403** kể cả khi UI đã ẩn. Test phải chứng minh cả hai.
4. **Tách chữ ký khỏi quyền.** Quy tắc 3 chữ ký (Inspector ≠ Reviewer ≠
   Approver) là **luật nghiệp vụ** trong Domain policy, không phải RBAC.
   Một người có role QC vẫn không được ký 2 vai trên cùng một WO.

## Khi thêm surface mới — trả lời trước khi code

| Câu hỏi | Nếu không trả lời được |
|---|---|
| Role nào ĐỌC được? | mặc định: không ai ⇒ chọn policy hẹp nhất |
| Role nào GHI được? | mặc định: Admin ⇒ mở rộng có chủ đích |
| Operator đứng máy có thấy không? | nếu có ⇒ phải chạy được ở density `shopfloor` |
| Sai quyền thì thấy gì? | `AccessDenied`, không phải trang trắng / 500 |

## Bằng chứng bắt buộc

Thêm case vào `RbacTests` cho **mỗi** endpoint mới: 1 role được phép (2xx) +
**ít nhất 1** role bị chặn (403). Test chỉ có happy path = chưa test RBAC.

```bash
dotnet test CCL-MES-Hybrid/tests/CCL.MES.Api.Tests/CCL.MES.Api.Tests.csproj \
  --filter RbacTests
```

## Do NOT

- Dùng `[AllowAnonymous]` cho endpoint có dữ liệu — kể cả "chỉ để test".
- Kiểm quyền bằng `if (user.Role == "Admin")` rải trong service — dùng policy.
- Cho `Operator` quyền ghi master data (Spec/Routing/RawMaterial) — đó là Engineer.
- Coi việc ẩn nút là đã xong phân quyền.

---
name: cmes-thin-controller
description: >
  Luật phân tầng cho API CCL-MES — controller mỏng, luật nghiệp vụ nằm trong
  Application service + Domain policy, không nằm trong controller HTTP. Dùng
  khi thêm/sửa bất kỳ controller, endpoint, hoặc DTO nào trong CCL.MES.Api.
---

# CMES thin controller

**Rule (enforced):** controller chỉ làm 4 việc — bind, authorize, gọi service,
map lỗi → HTTP status. **Không** truy vấn `DbContext`, **không**
`SaveChangesAsync`, **không** chứa điều kiện nghiệp vụ.

Nợ hiện tại (baseline đo 2026-08-18): **22** `SaveChangesAsync` nằm trong
controller, **20/33** controller chạm `DbContext` trực tiếp,
`WoQcReviewController.cs` = **1.460 dòng**. Gate
`scripts/gate-thin-controller.sh` là **ratchet đi xuống** — code mới không
được làm con số này tệ hơn, và mỗi PR chạm vùng cũ nên kéo nó xuống.

## Vì sao đây không phải chuyện thẩm mỹ

Luật nghiệp vụ nằm trong controller thì:
- không test được nếu không dựng `WebApplicationFactory` (chậm, giòn),
- không tái dùng được cho background job, ERP adapter, hay API thứ hai cho máy,
- hai endpoint làm "gần giống nhau" sẽ **phân kỳ** — mỗi cái một luật.

Đây chính là thứ chặn CCL-MES mở cổng tích hợp ERP.

## Hình dạng đúng

```
Controller (HTTP)            →  bind DTO · [Authorize] · gọi service · map ApiError
  └─ Application/Service     →  orchestration · transaction · emit audit
       └─ Domain/Policy      →  luật thuần, không I/O, test được bằng unit test
```

```csharp
[HttpPost("{id:long}/advance")]
[Authorize(Policy = "OperatorWrite")]
public async Task<IActionResult> Advance(long id, [FromBody] AdvanceRequest req, CancellationToken ct)
{
    var result = await _workOrders.AdvanceAsync(id, req.ToCommand(User), ct);
    return result.Error switch
    {
        null                          => Ok(result.Value),
        WoErrorCode.Conflict          => Conflict(ApiError.From(result.Error)),
        WoErrorCode.Forbidden         => Forbid(),
        _                             => BadRequest(ApiError.From(result.Error))
    };
}
```

Luật "IPQC phải Pass mới được advance" **thuộc về** `WorkOrderStateMachine` /
policy object — không phải một `if` trong controller.

## Policy object — khi nào tách

Tách một `*Policy` trong Domain khi luật thoả **≥2** điều:
- có ≥3 nhánh điều kiện,
- được dùng ở ≥2 nơi (hoặc sẽ được),
- sai thì hỏng dữ liệu (không chỉ hỏng hiển thị).

Ứng viên đã xác định: `SignaturePolicy` (3 chữ ký, Inspector ≠ Reviewer ≠
Approver), `QcGate` (điều kiện pass theo ngưỡng đã resolve), `LegAdvancePolicy`
(HARD/SOFT dependency trong DAG), `SemiStockPolicy`.

## Checklist cho endpoint mới

- [ ] Controller **0** `SaveChangesAsync`, **0** `_db.` query
- [ ] Có `[Authorize(Policy=...)]` — không dựa vào FallbackPolicy
- [ ] Mutation ⇒ nhận `Idempotency-Key` (xem `IdempotencyMiddleware`)
- [ ] Mutation ⇒ emit audit (skill `cmes-audit-emit`)
- [ ] Lỗi trả `ApiError` + `WoErrorCode`, **không** trả chuỗi tiếng Việt/Anh
- [ ] DTO nằm ở `CCL.MES.Shared`, POCO thuần, **không** tham chiếu EF
- [ ] Test: happy path + 403 (sai role) + 409 (concurrency) + 400 (guard fail)
- [ ] `bash CCL-MES-Hybrid/scripts/gate-thin-controller.sh` không tăng ratchet

## Do NOT

- Thêm `using Microsoft.EntityFrameworkCore;` vào controller mới.
- Trả `Ok(entity)` — luôn map sang DTO (entity kéo theo navigation + secret).
- Viết lại luật đã có trong Domain vì "gọi service phiền hơn".

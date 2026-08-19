using CCL.MES.Api.Diagnostics;
using CCL.MES.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// Liveness + readiness probes. Anonymous so a load balancer / kubelet can
/// hit them without holding a token. <c>GET /health</c> stays intentionally
/// cheap — no DB query, just process-up confirmation.
///
/// <c>GET /health/ready</c> carries the gate-enum-integrity TẦNG 3 signal
/// (see <see cref="EnumIntegrityMonitor"/>). The scan itself is cached by the
/// monitor, so this endpoint costs one field read on the hot path.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route(ApiVersion.Prefix + "/[controller]")]
public sealed class HealthController : ControllerBase
{
    private readonly EnumIntegrityMonitor _enumIntegrity;

    public HealthController(EnumIntegrityMonitor enumIntegrity) => _enumIntegrity = enumIntegrity;

    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "ok",
        version = "P10.1",
        timeUtc = DateTime.UtcNow,
    });

    /// <summary>
    /// Readiness + tính toàn vẹn dữ liệu.
    ///
    /// LUÔN 200 và LUÔN <c>ready=true</c> khi process phục vụ được — kể cả khi
    /// dữ liệu nhiễm. Cố ý: đây là endpoint mà load balancer dùng để quyết định
    /// đưa instance ra khỏi vòng quay. Sự cố 2026-08-19 làm hỏng 10/24 route;
    /// trả 503 ở đó sẽ biến sự cố 41% thành mất toàn bộ dịch vụ của nhà máy —
    /// cơ chế canh tự gây ra thiệt hại lớn hơn cái nó canh.
    ///
    /// Tín hiệu nằm ở TRƯỜNG <c>dataIntegrity.status</c> (ok · degraded ·
    /// unknown), không ở HTTP status. Cảnh báo giám sát bắt theo trường đó.
    /// </summary>
    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken ct)
    {
        var snapshot = await _enumIntegrity.GetAsync(ct);
        return Ok(new
        {
            ready = true,
            dataIntegrity = new
            {
                status = snapshot.Status,
                messageKey = snapshot.MessageKey,
                checkedAtUtc = snapshot.CheckedAtUtc,
                columnsScanned = snapshot.ColumnsScanned,
                columnsDiscovered = snapshot.ColumnsDiscovered,
                badColumns = snapshot.BadColumns,
                badRows = snapshot.BadRows,
                violations = snapshot.Violations,
                error = snapshot.Error,
            },
        });
    }
}

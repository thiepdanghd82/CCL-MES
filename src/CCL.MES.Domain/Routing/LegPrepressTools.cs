namespace CCL.MES.Domain.Routing;

/// <summary>
/// P11 per-leg Pre-press (Q-B, Henry 2026-07-24) — quy tắc <c>LegKind → tool
/// check</c>: leg nào cần <c>WoPlateCheck</c> (bản in) / <c>WoCutterCheck</c>
/// (khuôn cắt). "Chỉ leg liên quan": PRINT cần bản; CUT/TAPE cần khuôn (cắt
/// tape dùng khuôn dập); PRINT_CUT (in+bế inline) cần cả hai; ASSEMBLY không.
///
/// <para><b>⚠ Ops-confirm</b> — mapping này suy từ LegKind, chưa có bảng master
/// tool→công đoạn thật (mirror precedence chờ-xác-nhận của
/// <c>RoutingLegMapSeed</c>). Khi Ops cấp mapping, thay thân hàm — chữ ký giữ.</para>
///
/// Thuần (không I/O) — <c>PrepressBomSnapshotService.MaterializeForLegAsync</c>
/// gọi để quyết định tạo tool-row nào cho leg.
/// </summary>
public static class LegPrepressTools
{
    public static bool NeedsPlate(string? legKind) => Norm(legKind) switch
    {
        nameof(LegKind.PRINT) or nameof(LegKind.PRINT_CUT) => true,
        _ => false,
    };

    public static bool NeedsCutter(string? legKind) => Norm(legKind) switch
    {
        nameof(LegKind.CUT) or nameof(LegKind.TAPE) or nameof(LegKind.PRINT_CUT) => true,
        _ => false,
    };

    private static string Norm(string? s) => (s ?? "").Trim().ToUpperInvariant();
}

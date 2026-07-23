namespace CCL.MES.Domain.Routing;

/// <summary>
/// P11-1 — loại công đoạn của một <c>WoLeg</c> trong routing DAG đa
/// phương pháp. Phân loại từ routing op qua <c>RoutingLegMap</c> (dữ
/// liệu, không hardcode). Quyết định surface profile + luật dependency:
/// <list type="bullet">
///   <item><see cref="PRINT_CUT"/> — in+bế inline 1 lượt (topology T1) →
///     1 leg, chạy tuyến tính như WO cũ.</item>
///   <item><see cref="PRINT"/> / <see cref="CUT"/> — tách rời (T2/T3);
///     CUT phụ thuộc PRINT (SOFT gate).</item>
///   <item><see cref="TAPE"/> — cắt tape (semi, song song PRINT — SOFT).</item>
///   <item><see cref="ASSEMBLY"/> — dán tape + semi-in; phụ thuộc HARD
///     vào mọi PRINT + TAPE (không dán được nếu thiếu semi).</item>
/// </list>
/// Lưu DB dạng string (WoLeg.LegKind).
/// </summary>
public enum LegKind
{
    PRINT,
    CUT,
    TAPE,
    ASSEMBLY,
    PRINT_CUT,
}

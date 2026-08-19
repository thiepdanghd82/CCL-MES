using System.Globalization;

namespace CCL.MES.EnumIntegrity;

/// <summary>
/// Một bộ từ vựng in ra dùng chung cho CẢ BA tầng. Nếu mỗi tầng tự chế câu chữ
/// thì log boot, output gate và body /health/ready sẽ nói ba thứ tiếng khác
/// nhau về cùng một sự thật — và người trực ca sẽ không nối được chúng lại.
/// </summary>
public static class EnumIntegrityReport
{
    public const string Tag = "[enum-integrity]";

    /// <summary>Dòng số liệu — luôn in, kể cả khi sạch (S12: im lặng = không ai biết đã chạy).</summary>
    public static string Counters(EnumIntegrityResult r) =>
        $"columns scanned = {r.ColumnsScanned}/{r.ColumnsDiscovered} · " +
        $"distinct values = {r.DistinctValuesChecked} · " +
        $"skipped = {r.Skipped.Count} · " +
        $"{r.Duration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)} ms";

    /// <summary>Dòng phán quyết một hàng.</summary>
    public static string Verdict(EnumIntegrityResult r) =>
        r.IsClean
            ? "PASS — no out-of-enum values"
            : $"FAIL — {r.BadColumns} column(s), {r.BadRows} row(s) out of enum";

    /// <summary>Toàn bộ báo cáo, mỗi dòng đã gắn <see cref="Tag"/>.</summary>
    public static IEnumerable<string> Lines(EnumIntegrityResult r)
    {
        yield return $"{Tag} {Counters(r)}";

        foreach (var v in r.Violations)
            yield return $"{Tag} VIOLATION {v.Format()}";

        // Cột bỏ qua chỉ in gọn: DB test chưa migrate sẽ bỏ qua toàn bộ 37 cột và
        // ta KHÔNG muốn 37 dòng nhiễu. Nhưng phải in số, vì scanned=0 nghĩa là
        // KHÔNG KẾT LUẬN ĐƯỢC chứ không phải sạch.
        if (r.Skipped.Count > 0)
        {
            var sample = string.Join(", ", r.Skipped.Take(3).Select(s => $"{s.Table}.{s.Column} ({s.Reason})"));
            var more = r.Skipped.Count > 3 ? $" … +{r.Skipped.Count - 3}" : "";
            yield return $"{Tag} skipped: {sample}{more}";
        }

        yield return $"{Tag} {Verdict(r)}";
    }

    /// <summary>
    /// Đúng — quét được 0/N cột KHÔNG phải là PASS. DB lạc hậu migration hoặc bị
    /// khoá sẽ cho kết quả "sạch" giả; phía gọi phải phân biệt được.
    /// </summary>
    public static bool IsInconclusive(EnumIntegrityResult r) =>
        r.ColumnsDiscovered > 0 && r.ColumnsScanned == 0;
}

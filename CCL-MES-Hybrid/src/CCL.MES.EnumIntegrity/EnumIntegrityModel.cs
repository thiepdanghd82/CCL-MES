namespace CCL.MES.EnumIntegrity;

/// <summary>
/// Một cột enum-lưu-dạng-chuỗi được phát hiện bằng REFLECTION trên EF model.
/// Không hard-code ở đâu cả: enum thêm về sau tự động được canh.
/// </summary>
/// <param name="Table">Tên bảng vật lý.</param>
/// <param name="Column">Tên cột vật lý.</param>
/// <param name="EnumType">Kiểu enum CLR phía model.</param>
/// <param name="EntityType">Tên entity khai báo (để lần ngược về DbContext).</param>
public sealed record EnumStringColumn(
    string Table,
    string Column,
    Type EnumType,
    string EntityType)
{
    public string Key => $"{Table}.{Column}";
    public override string ToString() => $"{Key} ({EnumType.Name})";
}

/// <summary>
/// Hai HẠNG vi phạm — cố ý tách vì chúng nguy hiểm theo cách khác nhau.
/// </summary>
public enum EnumViolationKind
{
    /// <summary>Converter NÉM khi đọc. Hỏng ồn ào: truy vấn chết, 500 rõ ràng.
    /// Đây là hạng đã giết 10 route ngày 2026-08-19 (<c>'Done'</c>).</summary>
    Throws = 1,

    /// <summary>Converter KHÔNG ném nhưng cho ra giá trị không định nghĩa
    /// (<c>Enum.IsDefined = false</c>). Hỏng IM LẶNG — nguy hiểm hơn vì không
    /// ai biết: badge trống, switch rơi vào default, báo cáo lệch. Đo thực tế:
    /// <c>''</c> và <c>'0'</c> trên <c>ProcessStepCode</c> rơi vào hạng này.</summary>
    Undefined = 2,
}

/// <summary>Một giá trị rác cụ thể + số dòng mang nó.</summary>
public sealed record EnumViolation(
    string Table,
    string Column,
    string EnumType,
    string Value,
    long RowCount,
    EnumViolationKind Kind,
    string Detail)
{
    /// <summary>Dòng một-hàng dùng cho log boot, CLI và /health/ready.</summary>
    public string Format() =>
        $"{Table}.{Column} = '{Value}' x{RowCount} ({EnumType}: " +
        (Kind == EnumViolationKind.Throws
            ? Detail
            : $"không ném nhưng IsDefined=False — {Detail}") + ")";
}

/// <summary>Cột bị bỏ qua + lý do (bảng/cột chưa tồn tại vì DB lạc hậu migration…).</summary>
public sealed record EnumColumnSkip(string Table, string Column, string Reason);

/// <summary>Kết quả một lần quét.</summary>
public sealed record EnumIntegrityResult(
    int ColumnsDiscovered,
    int ColumnsScanned,
    IReadOnlyList<EnumViolation> Violations,
    IReadOnlyList<EnumColumnSkip> Skipped,
    long DistinctValuesChecked,
    TimeSpan Duration)
{
    public bool IsClean => Violations.Count == 0;

    /// <summary>Tổng số DÒNG mang giá trị rác — con số ops cần, không phải số cột.</summary>
    public long BadRows => Violations.Sum(v => v.RowCount);

    public int BadColumns => Violations.Select(v => v.Table + "." + v.Column).Distinct().Count();

    public static EnumIntegrityResult Empty { get; } =
        new(0, 0, Array.Empty<EnumViolation>(), Array.Empty<EnumColumnSkip>(), 0, TimeSpan.Zero);
}

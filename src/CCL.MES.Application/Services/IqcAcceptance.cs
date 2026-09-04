namespace CCL.MES.Application.Services;

/// <summary>Kết luận máy chấm cho MỘT hạng mục.</summary>
public enum IqcAutoVerdict
{
    /// <summary>Không đủ căn cứ để máy chấm — người phải chấm tay.
    /// KHÔNG được hiểu là đạt.</summary>
    Undecidable = 0,
    Pass = 1,
    Fail = 2,
}

/// <summary>Một lần chấm: kết luận + lý do đọc được, để hiện cho người kiểm và
/// đóng băng vào hồ sơ.</summary>
/// <param name="Verdict">Kết luận.</param>
/// <param name="ReasonCode">Mã lý do máy đọc được (không dịch, để test khoá).</param>
/// <param name="OffendingIndex">Vị trí phép đo gây trượt (1-based), nếu có.</param>
/// <param name="OffendingValue">Giá trị gây trượt, nếu có.</param>
public readonly record struct IqcJudgement(
    IqcAutoVerdict Verdict, string ReasonCode, int? OffendingIndex, double? OffendingValue)
{
    public static IqcJudgement Undecidable(string why) => new(IqcAutoVerdict.Undecidable, why, null, null);
    public static IqcJudgement Pass(string why) => new(IqcAutoVerdict.Pass, why, null, null);
}

/// <summary>
/// P13 — luật chấp nhận IQC, mã hoá từ luật ĐO ĐƯỢC trên file master 2026 chứ
/// không phải từ suy đoán.
///
/// <para><b>Ngoại quan: Ac = 0, Re = 1 (zero-defect).</b> Đo trên 3.715 lô Roll
/// có kết luận rõ ràng:</para>
/// <list type="bullet">
///   <item>OK &amp; không đếm được lỗi nào : 3.648</item>
///   <item>OK &amp; CÓ lỗi                  : <b>0</b></item>
///   <item>NG &amp; CÓ lỗi                  : 67</item>
///   <item>NG &amp; không lỗi               : <b>0</b></item>
/// </list>
/// <para>Không một lô nào có lỗi mà vẫn đạt ⇒ luật là tuyệt đối, không có số
/// chấp nhận. Đây là điều bảng AQL trong file KHÔNG nói (nó chỉ cho cỡ mẫu,
/// không có cột Ac/Re) — luật thật nằm trong hành vi, phải đo mới thấy.</para>
///
/// <para>Thuần, không I/O. Mọi nhánh trả về <see cref="IqcAutoVerdict"/> ba
/// trạng thái: thiếu căn cứ KHÔNG được rơi về "đạt" (bài học L67 — bản ghi
/// bằng chứng thiếu một chiều thông tin thì nó nói dối im lặng).</para>
/// </summary>
public static class IqcAcceptance
{
    /// <summary>Chấm hạng mục ĐẾM LỖI. <paramref name="defectCounts"/> là số lỗi
    /// đếm được của từng loại; <c>null</c> = chưa ai đếm ô đó.</summary>
    public static IqcJudgement JudgeDefectCounts(IReadOnlyList<int?> defectCounts)
    {
        if (defectCounts.Count == 0)
            return IqcJudgement.Undecidable("iqc.judge.no_defect_columns");

        // Còn ô trống = chưa kiểm xong. Cho "đạt" lúc này là kết luận thay cho
        // người chưa làm việc — đúng cái L67 đã cảnh báo.
        for (var i = 0; i < defectCounts.Count; i++)
            if (defectCounts[i] is null)
                return new IqcJudgement(IqcAutoVerdict.Undecidable,
                    "iqc.judge.defect_incomplete", i + 1, null);

        for (var i = 0; i < defectCounts.Count; i++)
        {
            var n = defectCounts[i]!.Value;
            if (n < 0)
                return new IqcJudgement(IqcAutoVerdict.Undecidable,
                    "iqc.judge.defect_negative", i + 1, n);
            if (n > 0)
                return new IqcJudgement(IqcAutoVerdict.Fail,
                    "iqc.judge.defect_found", i + 1, n);
        }
        return IqcJudgement.Pass("iqc.judge.zero_defect");
    }

    /// <summary>
    /// Chấm hạng mục ĐO LƯỜNG (độ rộng ×5, độ dày ×5…).
    ///
    /// <para><paramref name="limit"/> null ⇒ không có ngưỡng số ⇒ người chấm.
    /// Đây là nhánh của "Tham khảo báo cáo" và của mọi chuỗi bộ đọc không hiểu:
    /// máy im lặng nhường quyền, KHÔNG đoán.</para>
    ///
    /// <para><paramref name="tearObserved"/> chỉ có nghĩa khi tiêu chuẩn ghi
    /// "or tear": vật liệu rách trước khi bong keo là ĐẠT, vì lực bám đã lớn
    /// hơn độ bền của chính vật liệu.</para>
    /// </summary>
    public static IqcJudgement JudgeMeasurements(
        IReadOnlyList<double?> values, IqcSpecLimit? limit, bool tearObserved = false)
    {
        if (limit is not { } lim)
            return IqcJudgement.Undecidable("iqc.judge.no_numeric_limit");

        if (tearObserved && lim.TearIsPass)
            return IqcJudgement.Pass("iqc.judge.tear_accepted");

        if (lim.Low is null && lim.Up is null)
            return IqcJudgement.Undecidable("iqc.judge.limit_has_no_bound");

        if (values.Count == 0)
            return IqcJudgement.Undecidable("iqc.judge.no_measurements");

        for (var i = 0; i < values.Count; i++)
            if (values[i] is null)
                return new IqcJudgement(IqcAutoVerdict.Undecidable,
                    "iqc.judge.measurement_missing", i + 1, null);

        for (var i = 0; i < values.Count; i++)
        {
            var v = values[i]!.Value;
            if (lim.Low is { } lo && v < lo)
                return new IqcJudgement(IqcAutoVerdict.Fail, "iqc.judge.below_low", i + 1, v);
            if (lim.Up is { } up && v > up)
                return new IqcJudgement(IqcAutoVerdict.Fail, "iqc.judge.above_up", i + 1, v);
        }
        return IqcJudgement.Pass("iqc.judge.all_in_range");
    }

    /// <summary>
    /// Gộp kết luận của nhiều hạng mục thành kết luận CUỐI của phiếu.
    ///
    /// <para>Thứ tự ưu tiên: có một hạng mục trượt ⇒ phiếu trượt. Không trượt
    /// nhưng còn hạng mục chưa quyết được ⇒ phiếu CHƯA quyết được (không phải
    /// đạt). Chỉ khi mọi hạng mục đều đạt thì phiếu mới đạt.</para>
    /// </summary>
    public static IqcAutoVerdict Combine(IEnumerable<IqcAutoVerdict> items)
    {
        var any = false; var undecided = false;
        foreach (var v in items)
        {
            any = true;
            if (v == IqcAutoVerdict.Fail) return IqcAutoVerdict.Fail;
            if (v == IqcAutoVerdict.Undecidable) undecided = true;
        }
        if (!any) return IqcAutoVerdict.Undecidable;
        return undecided ? IqcAutoVerdict.Undecidable : IqcAutoVerdict.Pass;
    }
}

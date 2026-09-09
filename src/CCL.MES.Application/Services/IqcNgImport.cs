using CCL.MES.Domain.Entities;

namespace CCL.MES.Application.Services;

/// <summary>Một dòng thô của sheet <c>NG Material </c> — chuỗi/số nguyên văn,
/// chưa diễn giải. Reader (Infrastructure) dựng cái này, mapper dưới đây mới
/// quyết nghĩa, nên toàn bộ luật quy đổi test được mà không cần file Excel.</summary>
public sealed class IqcNgSheetRow
{
    /// <summary>Số dòng 1-based trong sheet — thành phần của khoá idempotent.</summary>
    public int RowNumber { get; set; }

    public DateTime? WarehouseInDate { get; set; }   // C
    public DateTime? DetectedDate { get; set; }      // D
    public DateTime? ClaimDate { get; set; }         // E
    public string? ClaimRef { get; set; }            // F
    public string? PoNo { get; set; }                // G
    public string? SupplierName { get; set; }        // H
    public string? MaterialName { get; set; }        // I
    public string? PartNo { get; set; }              // J
    public string? SupplierLotNo { get; set; }       // L
    public string? Uom { get; set; }                 // M
    public string? DefectName { get; set; }          // N
    public double? NgQty { get; set; }               // Q
    public double? NgAreaM2 { get; set; }            // R
    public int? NgRolls { get; set; }                // S
    public string? SupplierAnswer { get; set; }      // T
    public string? Title { get; set; }               // U
    public string? Stage { get; set; }               // V
    public string? Remark { get; set; }              // W
    public string? Remark2 { get; set; }             // X
}

/// <summary>Kết quả quy đổi một dòng: bản ghi đã map + lý do bỏ qua nếu có.</summary>
public sealed class IqcNgMapped
{
    public IqcNgRecord? Record { get; set; }
    public string? SkipReason { get; set; }
}

/// <summary>
/// P13 bước 5 — quy đổi sheet <c>NG Material </c> của file master sang
/// <see cref="IqcNgRecord"/>.
///
/// <para><b>Ba quyết định đã đo, không phải chọn cho tiện:</b></para>
/// <list type="number">
///   <item><b>Khoá idempotent theo NỘI DUNG, không theo vị trí dòng.</b>
///     Bản đầu dùng <c>r{số dòng}</c>: chạy lại thì sạch, nhưng vỡ ngay khi ai
///     đó CHÈN hoặc XOÁ một dòng giữa sheet — mọi dòng phía dưới tụt số và lần
///     nạp sau coi chúng là vụ mới, nhân đôi cả sổ.
///     Nay khoá = mã băm của (ngày phát hiện · NCC · mã NVL · số lô · tên lỗi ·
///     P/O), cộng một số thứ tự cho các dòng TRÙNG HỆT nhau — vì cùng NCC +
///     cùng mã + cùng ngày là chuyện có thật (nhiều lô, hoặc một lô nhiều loại
///     lỗi), gộp chúng lại là mất một vụ.</item>
///   <item><b>KHÔNG tự nối <c>MaterialLotId</c>.</b> Số lô trên sheet là số lô
///     của NHÀ CUNG CẤP; khớp <c>MaterialLots.LotNo</c> 0/140. Nối phải do
///     người dùng chọn trong app.</item>
///   <item><b>Trạng thái suy từ cột "NCC Xác nhận" + ngày claim</b>, giữ nguyên
///     văn câu trả lời vào <c>SupplierNote</c> — 16 biến thể chữ trên 152 dòng,
///     ép hết về enum là mất thông tin đàm phán.</item>
/// </list>
/// </summary>
public static class IqcNgImport
{
    /// <summary>Tiền tố khoá idempotent. Đổi chuỗi này = nạp trùng toàn bộ.</summary>
    public const string SourcePrefix = "xlsx:NG Material:h";

    /// <summary>Tiền tố bản nạp CŨ (khoá theo vị trí dòng). Giữ để importer
    /// nhận ra và nâng cấp tại chỗ thay vì nhân đôi dữ liệu đã có.</summary>
    public const string LegacyRowPrefix = "xlsx:NG Material:r";

    /// <summary>Khoá nội dung của một dòng, CHƯA gắn số thứ tự trùng lặp.
    /// Ngày lấy tới NGÀY (Excel lưu ngày thuần, không giờ).</summary>
    public static string ContentKey(IqcNgSheetRow r)
    {
        static string N(string? s) => (Clean(s) ?? "").ToUpperInvariant();
        var raw = string.Join("|",
            r.DetectedDate?.ToString("yyyy-MM-dd") ?? "",
            N(r.SupplierName), N(r.PartNo), N(r.SupplierLotNo), N(r.DefectName), N(r.PoNo));
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    /// <summary>Excel trả <c>#REF!</c> / <c>#N/A</c> khi công thức gãy. Đó là
    /// LỖI Ô, không phải dữ liệu — nạp nguyên văn vào DB thì mọi báo cáo sau
    /// này phải đi lọc chuỗi rác.</summary>
    private static readonly string[] ErrorCells =
        { "#REF!", "#N/A", "#VALUE!", "#DIV/0!", "#NAME?", "#NULL!", "#NUM!" };

    public static string? Clean(string? s)
    {
        var t = s?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        foreach (var e in ErrorCells)
            if (string.Equals(t, e, StringComparison.OrdinalIgnoreCase)) return null;
        return t;
    }

    private static string? Cut(string? s, int max)
        => s is null ? null : s.Length <= max ? s : s[..max];

    /// <summary>Công đoạn phát hiện. Để trống ⇒ <c>Unknown</c>, KHÔNG gán bừa
    /// về IQC: 38% vụ phát hiện ở sản xuất, đoán sai làm hỏng chính con số đó.</summary>
    public static IqcNgStage MapStage(string? raw) => Clean(raw)?.ToUpperInvariant() switch
    {
        "IQC" => IqcNgStage.Iqc,
        "SX" or "SẢN XUẤT" or "SAN XUAT" or "PRODUCTION" => IqcNgStage.Production,
        _ => IqcNgStage.Unknown,
    };

    /// <summary>Hình thức đền bù, suy từ câu trả lời nguyên văn của NCC.</summary>
    public static IqcClaimSettlement MapSettlement(string? raw)
    {
        var t = Clean(raw);
        if (t is null) return IqcClaimSettlement.None;
        var s = t.ToLowerInvariant();
        if (s.Contains("bù hàng") || s.Contains("bu hang")) return IqcClaimSettlement.Replacement;
        if (s.Contains("trừ công nợ") || s.Contains("cấn trừ") || s.Contains("giảm trừ"))
            return IqcClaimSettlement.CreditNote;
        if (s.Contains("trả hàng") || s.Contains("tra hang")) return IqcClaimSettlement.Return;
        if (s.Contains("hủy") || s.Contains("huỷ")) return IqcClaimSettlement.Scrap;
        return IqcClaimSettlement.None;
    }

    /// <summary>Trạng thái vòng đời. Thứ tự kiểm là có chủ đích: "Close - k
    /// claim đc NCC" phải thắng trước khi rơi vào nhánh "đã có ngày claim".</summary>
    public static IqcNgStatus MapStatus(string? supplierAnswer, DateTime? claimDate)
    {
        var t = Clean(supplierAnswer);
        var s = t?.ToLowerInvariant();

        if (s is not null && s.StartsWith("close")) return IqcNgStatus.ClosedNoClaim;
        if (s is not null && (s.Contains("chưa trả lời") || s.Contains("chua tra loi")))
            return IqcNgStatus.Claimed;
        if (s is not null && (s.Contains("chờ bù") || s.Contains("cho bu")))
            return IqcNgStatus.SupplierConfirmed;
        if (MapSettlement(t) != IqcClaimSettlement.None) return IqcNgStatus.Settled;
        return claimDate.HasValue ? IqcNgStatus.Claimed : IqcNgStatus.Open;
    }

    /// <summary>Đơn vị. Sheet có một ô lọt ngày tháng vào cột đơn vị — giữ
    /// nguyên thì cột UOM có giá trị "2026-02-08", nên chỉ nhận chữ.</summary>
    public static string? MapUom(string? raw)
    {
        var t = Clean(raw);
        if (t is null) return null;
        if (t.Any(char.IsDigit)) return null;
        return Cut(t, 16);
    }

    /// <summary>Quy đổi một dòng. Trả <c>SkipReason</c> thay vì ném: một dòng
    /// rác không được làm hỏng cả lần nạp, nhưng phải ĐẾM ĐƯỢC.</summary>
    public static IqcNgMapped Map(IqcNgSheetRow r)
    {
        // Ngày phát hiện là thứ DUY NHẤT bắt buộc: không có nó thì bản ghi
        // không xếp được vào dòng thời gian nào, và mọi báo cáo theo kỳ đều lệch.
        if (r.DetectedDate is null)
            return new IqcNgMapped { SkipReason = "thiếu NGÀY PHÁT HIỆN" };

        var supplier = Clean(r.SupplierName);
        var part = Clean(r.PartNo);
        var defect = Clean(r.DefectName);
        if (supplier is null && part is null && defect is null)
            return new IqcNgMapped { SkipReason = "dòng trống (không NCC / mã / lỗi)" };

        var answer = Clean(r.SupplierAnswer);
        var remark = string.Join(" · ",
            new[] { Clean(r.Title), Clean(r.Remark), Clean(r.Remark2) }.Where(x => x is not null));

        return new IqcNgMapped
        {
            Record = new IqcNgRecord
            {
                PartNo = Cut(part, 32),
                SupplierLotNo = Cut(Clean(r.SupplierLotNo), 64),
                SupplierName = Cut(supplier, 200),
                MaterialName = Cut(Clean(r.MaterialName), 300),
                PoNo = Cut(Clean(r.PoNo), 64),

                DetectedAt = r.DetectedDate.Value,
                DetectedStage = MapStage(r.Stage),
                DefectName = Cut(defect, 256),

                NgQty = r.NgQty,
                NgUom = MapUom(r.Uom),
                NgAreaM2 = r.NgAreaM2,
                NgRolls = r.NgRolls,

                Status = MapStatus(answer, r.ClaimDate),
                ClaimedAt = r.ClaimDate,
                ClaimRef = Cut(Clean(r.ClaimRef), 128),
                Settlement = MapSettlement(answer),
                SupplierNote = Cut(answer, 512),
                Remark = Cut(remark.Length == 0 ? null : remark, 512),

                // Số thứ tự do MapAll gán: Map() một dòng đơn lẻ không biết có
                // dòng nào trùng hệt nó, nên mặc định 0.
                ImportSource = SourcePrefix + ContentKey(r) + "-0",
            },
        };
    }

    /// <summary>
    /// Quy đổi CẢ SHEET. Phải đi qua đây thay vì gọi <see cref="Map"/> từng
    /// dòng: số thứ tự phân biệt các dòng TRÙNG HỆT nhau chỉ tính được khi
    /// nhìn toàn bộ, và nó phải ỔN ĐỊNH — nên đánh theo thứ tự dòng tăng dần
    /// trong nhóm trùng, không theo thứ tự duyệt của người gọi.
    /// </summary>
    public static List<(IqcNgSheetRow Row, IqcNgMapped Mapped)> MapAll(IEnumerable<IqcNgSheetRow> rows)
    {
        var all = rows.OrderBy(r => r.RowNumber).ToList();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var outp = new List<(IqcNgSheetRow, IqcNgMapped)>(all.Count);
        foreach (var r in all)
        {
            var m = Map(r);
            if (m.Record is not null)
            {
                var key = ContentKey(r);
                var ord = seen.TryGetValue(key, out var n) ? n : 0;
                seen[key] = ord + 1;
                m.Record.ImportSource = SourcePrefix + key + "-" + ord;
            }
            outp.Add((r, m));
        }
        return outp;
    }
}

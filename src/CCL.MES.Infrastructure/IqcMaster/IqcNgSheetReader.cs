using ClosedXML.Excel;
using CCL.MES.Application.Services;

namespace CCL.MES.Infrastructure.IqcMaster;

/// <summary>
/// P13 bước 5 — đọc sheet NG của file master "IQC report 2026".
///
/// <para>Nằm ở Infrastructure vì ClosedXML nằm ở đây; Application chỉ nhận
/// <see cref="IqcNgSheetRow"/> thuần nên luật quy đổi test được mà không cần
/// file Excel.</para>
///
/// <para><b>Tên sheet có DẤU CÁCH Ở CUỐI</b> — "NG Material " chứ không phải
/// "NG Material". Đó là tên thật trong workbook; so bằng dấu bằng sẽ trượt.
/// Nên ở đây dò theo tên đã Trim, và chấp nhận cả số ít lẫn số nhiều.</para>
///
/// <para><b>Map theo VỊ TRÍ cột, đã đối chiếu tay với dòng tiêu đề</b> (skill
/// cmes-defect-library-import: luôn đối soát index trước khi code). Dòng 1 là
/// tiêu đề, dữ liệu từ dòng 2.</para>
/// </summary>
public static class IqcNgSheetReader
{
    /// <summary>Tên sheet mong đợi, so sánh sau khi Trim.</summary>
    public static readonly string[] SheetNames = { "NG Material", "NG Materials" };

    private const int FirstDataRow = 2;

    // 1-based, đối chiếu với tiêu đề dòng 1:
    //   C=NGÀY NHẬP KHO · D=NGÀY PHÁT HIỆN · E=NGÀY CLAIM · F=Số tham chiếu
    //   G=P/O · H=NHÀ CUNG CẤP · I=TÊN NGUYÊN LIỆU · J=MÃ NGUYÊN LIỆU
    //   L=SỐ LÔ · M=QUY CÁCH · N=NỘI DUNG NG · Q=TOTAL NG THỰC TẾ (PCS/M)
    //   R=M² · S=Số cuộn · T=NCC Xác nhận · U=Tiêu đề · V=Phát hiện tại công đoạn
    //   W=Remark · X=(không tiêu đề, ghi chú tiếp)
    private const int ColWarehouseIn = 3, ColDetected = 4, ColClaim = 5, ColClaimRef = 6;
    private const int ColPo = 7, ColSupplier = 8, ColMaterial = 9, ColPartNo = 10;
    private const int ColLot = 12, ColUom = 13, ColDefect = 14;
    private const int ColQty = 17, ColAreaM2 = 18, ColRolls = 19;
    private const int ColAnswer = 20, ColTitle = 21, ColStage = 22, ColRemark = 23, ColRemark2 = 24;

    /// <summary>Đọc mọi dòng có dữ liệu. KHÔNG lọc, KHÔNG quy đổi — việc đó là
    /// của <see cref="IqcNgImport"/>, để đếm được bao nhiêu dòng bị bỏ và vì sao.</summary>
    public static List<IqcNgSheetRow> Read(Stream xlsx)
    {
        using var wb = new XLWorkbook(xlsx);
        var ws = wb.Worksheets.FirstOrDefault(
                     w => SheetNames.Any(n => string.Equals(w.Name.Trim(), n, StringComparison.OrdinalIgnoreCase)))
                 ?? throw new InvalidOperationException(
                     $"Không thấy sheet {string.Join(" / ", SheetNames)}. Sheet có: " +
                     string.Join(", ", wb.Worksheets.Select(w => $"'{w.Name}'")));

        var last = ws.LastRowUsed()?.RowNumber() ?? 0;
        var rows = new List<IqcNgSheetRow>();
        for (var r = FirstDataRow; r <= last; r++)
        {
            var row = ws.Row(r);
            if (row.IsEmpty()) continue;
            rows.Add(new IqcNgSheetRow
            {
                RowNumber = r,
                WarehouseInDate = Date(ws, r, ColWarehouseIn),
                DetectedDate = Date(ws, r, ColDetected),
                ClaimDate = Date(ws, r, ColClaim),
                ClaimRef = Text(ws, r, ColClaimRef),
                PoNo = Text(ws, r, ColPo),
                SupplierName = Text(ws, r, ColSupplier),
                MaterialName = Text(ws, r, ColMaterial),
                PartNo = Text(ws, r, ColPartNo),
                SupplierLotNo = Text(ws, r, ColLot),
                Uom = Text(ws, r, ColUom),
                DefectName = Text(ws, r, ColDefect),
                NgQty = Num(ws, r, ColQty),
                NgAreaM2 = Num(ws, r, ColAreaM2),
                NgRolls = Num(ws, r, ColRolls) is { } n ? (int)Math.Round(n) : null,
                SupplierAnswer = Text(ws, r, ColAnswer),
                Title = Text(ws, r, ColTitle),
                Stage = Text(ws, r, ColStage),
                Remark = Text(ws, r, ColRemark),
                Remark2 = Text(ws, r, ColRemark2),
            });
        }
        return rows;
    }

    private static string? Text(IXLWorksheet ws, int r, int c)
    {
        var cell = ws.Cell(r, c);
        // Ô lỗi công thức: lấy GetString() sẽ ném hoặc trả "#REF!" — trả nguyên
        // văn rồi để IqcNgImport.Clean() loại, như vậy chỉ một chỗ biết luật đó.
        try { var s = cell.GetFormattedString()?.Trim(); return string.IsNullOrEmpty(s) ? null : s; }
        catch { return null; }
    }

    private static double? Num(IXLWorksheet ws, int r, int c)
    {
        var cell = ws.Cell(r, c);
        try
        {
            if (cell.IsEmpty()) return null;
            if (cell.DataType == XLDataType.Number) return cell.GetDouble();
            var s = cell.GetFormattedString()?.Trim();
            return double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
        }
        catch { return null; }
    }

    private static DateTime? Date(IXLWorksheet ws, int r, int c)
    {
        var cell = ws.Cell(r, c);
        try
        {
            if (cell.IsEmpty()) return null;
            if (cell.DataType == XLDataType.DateTime) return cell.GetDateTime();
            // Sheet có ô ngày lưu dạng SỐ SERIAL Excel (45743) — không đổi thì
            // toàn bộ cột ngày rỗng và mọi dòng bị bỏ vì "thiếu ngày phát hiện".
            if (cell.DataType == XLDataType.Number)
            {
                var d = cell.GetDouble();
                if (d is > 20000 and < 80000) return DateTime.FromOADate(d);
                return null;
            }
            var s = cell.GetFormattedString()?.Trim();
            return DateTime.TryParse(s, out var v) ? v : null;
        }
        catch { return null; }
    }
}

using ClosedXML.Excel;
using CCL.MES.Application.Services;

namespace CCL.MES.Infrastructure.IqcMaster;

/// <summary>
/// P13 bước 3 — đọc sheet <c>Raw</c> của file master "IQC report 2026".
///
/// <para>Nằm ở Infrastructure vì ClosedXML nằm ở đây; tầng Application chỉ nhận
/// <see cref="IqcMasterRow"/> thuần nên test không phải kéo theo cả bộ đọc
/// Excel.</para>
///
/// <para><b>Map theo VỊ TRÍ cột, và vị trí đã đối chiếu tay</b> (skill
/// cmes-defect-library-import: "luôn đối soát index trước khi code"). Sheet
/// <c>Raw</c> có 2 dòng tiêu đề: dòng 1 là số thứ tự cột, dòng 2 mới là tên.
/// Dữ liệu bắt đầu từ dòng 3.</para>
/// </summary>
public static class IqcMasterRawReader
{
    /// <summary>Tên sheet trong file master.</summary>
    public const string SheetName = "Raw";

    /// <summary>Dòng đầu tiên CÓ DỮ LIỆU (1-based, ClosedXML đánh số từ 1).</summary>
    private const int FirstDataRow = 3;

    // Vị trí cột 1-based, đã đối chiếu với tiêu đề dòng 2:
    //   B=1c · C=IFS · D=Mother code · E=Tên Nguyên liệu · F=Nhà cung cấp
    //   G=Phương pháp test · H=Tiêu chuẩn keo · I=Tiêu chuẩn dày · J=Tiêu chuẩn rộng
    private const int ColIfs = 3;
    private const int ColMother = 4;
    private const int ColName = 5;
    private const int ColSupplier = 6;
    private const int ColMethod = 7;
    private const int ColAdhesion = 8;
    private const int ColThickness = 9;
    private const int ColWidth = 10;

    /// <summary>Đọc toàn bộ dòng có mã mẹ. Dòng trống hoàn toàn bị bỏ qua im
    /// lặng; dòng có dữ liệu nhưng THIẾU mã mẹ vẫn được trả về (mã rỗng) để
    /// service đếm vào <c>RowsSkippedNoCode</c> — nuốt ở đây thì con số nghiệm
    /// thu không khớp với số dòng thật của file.</summary>
    public static List<IqcMasterRow> Read(Stream xlsx)
    {
        using var wb = new XLWorkbook(xlsx);
        var ws = wb.Worksheet(SheetName);
        var rows = new List<IqcMasterRow>();

        var last = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = FirstDataRow; r <= last; r++)
        {
            var mother = Cell(ws, r, ColMother);
            var ifs = Cell(ws, r, ColIfs);
            var name = Cell(ws, r, ColName);
            var sup = Cell(ws, r, ColSupplier);
            var method = Cell(ws, r, ColMethod);
            var keo = Cell(ws, r, ColAdhesion);
            var day = Cell(ws, r, ColThickness);
            var rong = Cell(ws, r, ColWidth);

            // Dòng trắng hoàn toàn: bỏ. Bất kỳ ô nào có chữ: giữ.
            if (mother is null && ifs is null && name is null
                && keo is null && day is null && rong is null) continue;

            rows.Add(new IqcMasterRow(mother ?? "", ifs, name, sup, method, keo, day, rong));
        }
        return rows;
    }

    /// <summary>Ô đã trim; trả <c>null</c> cho ô rỗng và cho mấy cách viết
    /// "không có" của file master. <c>GetFormattedString</c> chứ không phải
    /// <c>GetString</c>: ô tiêu chuẩn hay là SỐ đã định dạng ("0.16"), lấy giá
    /// trị thô sẽ ra "0.16000000000000003".</summary>
    private static string? Cell(IXLWorksheet ws, int row, int col)
    {
        var v = ws.Cell(row, col).GetFormattedString()?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return v is "-" or "--" ? null : v;
    }
}

using CCL.MES.Domain.Entities;

namespace CCL.MES.Application.Services;

/// <summary>
/// P13 bước 4 — suy NHÓM VẬT LIỆU (Roll · Pcs · Chem · Tool) của một nguyên
/// liệu, để biết dựng bộ hạng mục nào cho phiếu.
///
/// <para><b>Vì sao phải suy chứ không đọc thẳng.</b> Phiếu có
/// <see cref="IqcInspection.Group"/> (Materials · Chemical · Tools · Other)
/// nhưng nó KHÔNG đủ: cuộn và tấm đều là <c>Materials</c>, trong khi file
/// master ghi chép chúng ở hai sheet với hai bộ hạng mục khác nhau (Roll 13 ô
/// đếm lỗi, PCS 9). Không có bảng ánh xạ nào trong repo.</para>
///
/// <para><b>Luật dưới đây ĐO ĐƯỢC, không suy đoán.</b> Đối chiếu mã mẹ của
/// sheet Roll (245 mã) và sheet PCS (27 mã) — hai tập KHÔNG giao nhau một dòng
/// nào — với <c>RawMaterials.InventoryUom</c>:</para>
/// <list type="bullet">
///   <item>mã sheet Roll → <c>m2</c> 1173 · <c>m</c> 10 · <c>pcs</c> 3
///         ⇒ <b>1183/1186 = 99,7%</b></item>
///   <item>mã sheet PCS  → <c>pcs</c> 42 · <c>m2</c> 2
///         ⇒ <b>42/44 = 95,5%</b></item>
/// </list>
///
/// <para><b>Luật này là ĐỀ XUẤT, không phải phán quyết.</b> 5 dòng đi ngược
/// luật là 5 lần app sẽ đoán sai nhóm, và lúc đó người kiểm phải sửa được —
/// service ghi nhận nhóm vào phiếu, không tính lại mỗi lần đọc.</para>
///
/// <para>Thuần, không I/O ⇒ khoá được bằng test mà không cần DB.</para>
/// </summary>
public static class IqcCategoryRule
{
    /// <summary>Đơn vị tồn kho ⇒ nhóm. So KHÔNG phân biệt hoa thường và bỏ
    /// khoảng trắng: dữ liệu IFS có cả <c>m2</c>, <c>M2</c>, <c>m²</c>.</summary>
    public static IqcMaterialCategory FromInventoryUom(string? uom)
    {
        var u = (uom ?? "").Trim().ToLowerInvariant().Replace(" ", "");
        return u switch
        {
            "m2" or "m²" or "sqm" or "m" or "mt" or "roll" or "rolls" => IqcMaterialCategory.Roll,
            "pcs" or "pc" or "piece" or "pieces" or "sheet" or "sheets" => IqcMaterialCategory.Pcs,
            "kg" or "g" or "l" or "lit" or "liter" or "litre" or "tin" or "can"
                => IqcMaterialCategory.Chem,
            // Không biết thì nói KHÔNG BIẾT. Đoán bừa về Roll sẽ dựng 13 ô đếm
            // lỗi cho một can mực, và người kiểm phải bấm qua từng ô vô nghĩa.
            _ => IqcMaterialCategory.Any,
        };
    }

    /// <summary>
    /// Nhóm cho một phiếu. Ưu tiên nhóm phiếu khi nó đã nói rõ (Chemical /
    /// Tools), rồi mới suy từ đơn vị tồn kho — vì <c>Materials</c> là cái
    /// thùng chứa cả cuộn lẫn tấm nên tự nó không quyết được gì.
    /// </summary>
    /// <param name="ticketGroup">Giá trị <see cref="IqcInspection.Group"/>.</param>
    /// <param name="inventoryUom">Đơn vị tồn kho của nguyên liệu.</param>
    public static IqcMaterialCategory Resolve(string? ticketGroup, string? inventoryUom)
    {
        var g = (ticketGroup ?? "").Trim();
        if (string.Equals(g, IqcGroup.Chemical, StringComparison.OrdinalIgnoreCase))
            return IqcMaterialCategory.Chem;
        if (string.Equals(g, IqcGroup.Tools, StringComparison.OrdinalIgnoreCase))
            return IqcMaterialCategory.Tool;

        // Materials và Other: đơn vị tồn kho là thứ duy nhất phân biệt được
        // cuộn với tấm.
        return FromInventoryUom(inventoryUom);
    }

    /// <summary>
    /// Hạng mục thư viện này có áp cho nhóm đang xét không.
    /// <see cref="IqcMaterialCategory.Any"/> áp cho mọi nhóm (tem nhãn, hồ sơ
    /// HSF); hạng mục theo nhóm chỉ áp đúng nhóm của nó.
    /// </summary>
    public static bool AppliesTo(IqcMaterialCategory itemCategory, IqcMaterialCategory ticket)
        => itemCategory == IqcMaterialCategory.Any || itemCategory == ticket;

    /// <summary>
    /// Hạng mục này có thuộc <b>BỘ CHUẨN CỦA MỘT NHÓM</b> không — tức nó áp cho
    /// MỌI lô của nhóm đó, không phụ thuộc mã nguyên liệu.
    ///
    /// <para><b>Vì sao cần luật này.</b> Đo trên live 2026-09-05: trong 7.212
    /// dòng tiêu chuẩn của 1.131 mã, số dòng kê ô đếm lỗi (<c>RD-</c> ·
    /// <c>PD-</c> · <c>TD-</c> · <c>CD-</c>) là <b>0</b>, và cũng không cái nào
    /// nằm trong ma trận mặc định. Nghĩa là 30 hạng mục đếm lỗi nhập ở bước 1
    /// hiện KHÔNG có đường nào tới được phiếu.</para>
    ///
    /// <para>Đó không phải thiếu sót của dữ liệu: file master ghi 13 cột lỗi
    /// cho MỌI lô cuộn ở sheet Roll, 9 cột cho MỌI lô tấm ở sheet PCS — chúng
    /// là bộ chuẩn của SHEET, không phải tiêu chuẩn riêng của từng mã. Spec
    /// per-mã sẽ không bao giờ kê chúng, nên phải có nguồn thứ hai.</para>
    ///
    /// <para><b>Luật:</b> <c>Category != Any</c> ⇔ thuộc bộ chuẩn của nhóm đó.
    /// Đúng với toàn bộ 30 hạng mục hiện có (Roll 13 · Pcs 9 · Tool 5 ·
    /// Chem 3) và 0 ngoại lệ — mọi hạng mục theo-mã đều mang
    /// <see cref="IqcMaterialCategory.Any"/>. Không thêm cột cờ mới vì cột đó
    /// sẽ lặp lại đúng thông tin <c>Category</c> đã nói.</para>
    /// </summary>
    public static bool IsCategoryStandard(IqcMaterialCategory itemCategory)
        => itemCategory != IqcMaterialCategory.Any;
}

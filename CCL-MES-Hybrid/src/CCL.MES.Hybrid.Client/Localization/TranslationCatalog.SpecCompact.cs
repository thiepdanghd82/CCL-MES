namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2E — Shared/SpecShowcardCompact.razor (speccompact.*).
public sealed partial class TranslationCatalog
{
    private void RegisterSpecCompact()
    {
        //     key                                  vi                                              en
        Add("speccompact.empty",              "Chưa có dữ liệu spec.",                              "No showcard data yet.");
        Add("speccompact.rev",                "Bản sửa đổi",                                        "Rev");

        Add("speccompact.field.refNo",        "SỐ THAM CHIẾU",                                      "REF NO");
        Add("speccompact.field.customer",     "Khách hàng",                                         "Customer");
        Add("speccompact.field.partNo",       "Mã hàng",                                            "Part No");
        Add("speccompact.field.partName",     "Tên hàng",                                           "Part Name");
        Add("speccompact.field.process",      "Công đoạn",                                          "Process");
        Add("speccompact.field.spec",         "Mức kiểm",                                           "Spec");
        Add("speccompact.field.productSize",  "Kích thước sản phẩm",                               "Product size");
        Add("speccompact.field.processType",  "Loại công nghệ in",                                  "Process type");

        Add("speccompact.processType.other",  "Khác / thông dụng",                                  "Other / generic");

        Add("speccompact.section.compliance", "Tuân thủ",                                           "Compliance");
        Add("speccompact.section.remarks",    "Ghi chú",                                            "Remarks");
        Add("speccompact.remarks.print",      "In:",                                                "Print:");
        Add("speccompact.remarks.diecut",     "Bế:",                                                "Diecut:");

        Add("speccompact.stamp.created",      "Tạo:",                                               "Created:");
        Add("speccompact.stamp.updated",      "Cập nhật:",                                          "Updated:");
        Add("speccompact.stamp.approved",     "Phê duyệt:",                                         "Approved:");
        Add("speccompact.stamp.released",     "Ban hành:",                                          "Released:");

        Add("speccompact.deferred",           "Toàn bộ nội dung spec chi tiết (Vật tư / Dòng in / Flexo / Lụa) sẽ có ở chế độ Đầy đủ (5c).",
                                              "All sub-spec content (Material / Print rows / Flexo / Silk) is coming in 5c (Full mode).");
    }
}

namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2C — WoQtyCorrectModal.razor (woqty.*).
public sealed partial class TranslationCatalog
{
    private void RegisterWoQty()
    {
        //     key                        vi                                                                             en
        Add("woqty.header",            "Sửa sản lượng — Q5 (chỉ thêm mới)",                                            "Correct quantity — Q5 (append-only)");

        Add("woqty.empty.title",       "Chưa có bản ghi sản lượng nào để sửa.",                                        "No output records yet to correct.");
        Add("woqty.empty.body",        "Thêm sản lượng bằng các nút +100 / +500 / +1000 trước khi mở Sửa số.",         "Add quantity with the +100 / +500 / +1000 buttons before opening Correct qty.");

        Add("woqty.source.label",      "Bản ghi gốc *",                                                                "Source record *");
        Add("woqty.source.placeholder","— chọn một bản ghi —",                                                         "— select a record —");

        Add("woqty.done.label",        "Sửa Đạt (Δ, có thể âm)",                                                       "Correct Done (Δ, may be negative)");
        Add("woqty.ng.label",          "Sửa NG (Δ, có thể âm)",                                                        "Correct NG (Δ, may be negative)");
        Add("woqty.reason.label",      "Lý do chỉnh sửa * (1-500 ký tự)",                                              "Correction reason * (1-500 characters)");

        Add("woqty.cancel",            "Huỷ",                                                                          "Cancel");
        Add("woqty.save",              "Lưu chỉnh sửa",                                                                "Save correction");
    }
}

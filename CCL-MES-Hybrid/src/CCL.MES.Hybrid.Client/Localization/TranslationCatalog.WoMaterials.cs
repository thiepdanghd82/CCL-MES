namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2C — WoMaterialsList.razor (womat.*).
public sealed partial class TranslationCatalog
{
    private void RegisterWoMaterials()
    {
        //     key                              vi                                           en
        Add("womat.section.title",           "Vật tư (ảnh chụp BOM)",                       "Materials (BOM snapshot)");
        Add("womat.progress.ok",             "Đạt",                                         "OK");
        Add("womat.empty",                   "Không có dòng vật tư — BOM của sản phẩm này rỗng. Kiểm tra Công đoạn/Cấu trúc trước khi tiếp tục.", "No material lines — the BOM is empty for this product. Check the Routing/Structure before continuing.");

        Add("womat.col.no",                  "STT",                                         "No.");
        Add("womat.col.partno",              "Mã vật tư",                                   "Part No");
        Add("womat.col.description",         "Mô tả",                                       "Description");
        Add("womat.col.qpa",                 "QPA (m2)",                                    "QPA (m2)");
        Add("womat.col.qtyrequired",         "SL yêu cầu",                                  "Qty. Required");
        Add("womat.col.uom",                 "ĐVT",                                         "UoM");
        Add("womat.col.scrapfactor",         "Hệ số hao hụt",                               "Scrap Factor");
        Add("womat.col.scrappct",            "Hao hụt %",                                   "Scrap %");
        Add("womat.col.partscan",            "Quét vật tư",                                 "Part Scan");
        Add("womat.col.partdesc",            "Mô tả vật tư",                                "Part Description");
        Add("womat.col.lot",                 "Lô",                                          "Lot");
        Add("womat.col.status",              "Trạng thái",                                  "Status");
        Add("womat.col.ng",                  "NG — mã lỗi · ghi chú",                       "NG — reason · note");
        Add("womat.col.action",              "Thao tác",                                    "Action");

        Add("womat.aria.partscan",           "Quét vật tư (cho phép nhập tay)",             "Part scan (manual entry allowed)");
        Add("womat.aria.lotno",              "Số lô",                                       "Lot number");
        Add("womat.aria.ngreason",           "Mã lỗi NG",                                   "NG reason code");
        Add("womat.aria.ngnote",             "Ghi chú NG",                                  "NG note");

        Add("womat.ng.selectdefect",         "— chọn mã lỗi —",                             "— select defect code —");
        Add("womat.ng.noteplaceholder",      "Ghi chú NG (1-500 ký tự)",                    "NG note (1-500 characters)");
        Add("womat.ng.catalogempty",         "Danh mục mã lỗi NG rỗng — báo IT",            "NG code catalog is empty — report to IT");

        Add("womat.btn.recordok",            "Ghi OK",                                      "Record OK");
        Add("womat.btn.markng",              "Đánh NG",                                     "Mark NG");
        Add("womat.btn.specialaccept",       "Chấp nhận đặc biệt",                          "Special Accept");
        Add("womat.btn.confirmspecialaccept","Xác nhận chấp nhận đặc biệt",                 "Confirm Special Accept");
        Add("womat.btn.saveng",              "Lưu NG",                                      "Save NG");
        Add("womat.btn.cancel",              "Hủy",                                         "Cancel");

        Add("womat.sa.armhint",              "Trưởng PD / Giám sát nhân nhượng — chọn mã lỗi rồi xác nhận", "PD leader / Supervisor concession — pick the defect, then confirm");
        Add("womat.sa.confirmhint",          "Trưởng PD / Giám sát nhân nhượng — ghi OK nhưng vẫn lưu lỗi", "PD leader / Supervisor concession — records OK with the defect kept on record");

        Add("womat.status.okspecialaccept",  "OK · Chấp nhận đặc biệt",                     "OK · Special Accept");
        Add("womat.status.notchecked",       "Chưa kiểm",                                   "Not checked");
    }
}

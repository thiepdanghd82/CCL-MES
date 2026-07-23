namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2C — TraceabilityDetailDialog.razor (trace.*).
public sealed partial class TranslationCatalog
{
    private void RegisterTraceDetail()
    {
        //     key                              vi                                                      en
        Add("trace.aria.window",             "Truy xuất {0}",                                         "Traceability {0}");
        Add("trace.tab.product",             "Dữ liệu sản phẩm",                                     "Product data");
        Add("trace.frozen.tooltip",          "Đã chốt",                                              "Frozen");
        Add("trace.loading",                 "Đang tải…",                                            "Loading…");
        Add("trace.empty.notfrozen",         "Chưa chốt dữ liệu {0} — dữ liệu chưa được đóng băng.", "{0} data not frozen yet.");
        Add("trace.meta.frozen",             "Phiên bản {0} · chốt {1} bởi {2}",                     "Version {0} · frozen {1} by {2}");
        Add("trace.meta.variant",            "· biến thể {0}",                                       "· variant {0}");
        Add("trace.items.none",              "Không có hạng mục kiểm nào được ghi nhận cho công đoạn này.", "No inspected items recorded for this phase.");
        Add("trace.prod.materials.section",  "1. Vật tư đã xác nhận",                                "1. Materials confirmed");
        Add("trace.prod.tools.section",      "2. Công cụ đã xác nhận",                               "2. Tools confirmed");
        Add("trace.tools.none",              "Không có công cụ nào được ghi nhận cho lệnh sản xuất này.", "No tools recorded for this Work Order.");

        // Table column headers
        Add("trace.col.no",                  "STT",                                                  "No.");
        Add("trace.col.partno",              "Mã linh kiện",                                         "Part No");
        Add("trace.col.description",         "Mô tả",                                                "Description");
        Add("trace.col.qpa",                 "QPA (m²)",                                             "QPA (m²)");
        Add("trace.col.qtyrequired",         "SL yêu cầu",                                           "Qty. Required");
        Add("trace.col.uom",                 "ĐVT",                                                  "UoM");
        Add("trace.col.partscan",            "Quét linh kiện",                                       "Part Scan");
        Add("trace.col.partdescription",     "Mô tả linh kiện",                                      "Part Description");
        Add("trace.col.lot",                 "Lô",                                                   "Lot");
        Add("trace.col.status",              "Trạng thái",                                           "Status");
        Add("trace.col.ng.reasonnote",       "NG — lý do · ghi chú",                                 "NG — reason · note");
        Add("trace.col.tool",                "Công cụ",                                              "Tool");
        Add("trace.col.numbercode",          "Số / mã",                                              "Number / code");
        Add("trace.col.checkedby",           "Người kiểm",                                           "Checked by");
        Add("trace.col.checkedat",           "Thời điểm kiểm",                                       "Checked at");
        Add("trace.col.ngreason",            "Lý do NG",                                             "NG reason");
        Add("trace.col.item",                "Hạng mục",                                             "Item");
        Add("trace.col.ngnote",              "Ghi chú NG",                                           "NG note");
    }
}

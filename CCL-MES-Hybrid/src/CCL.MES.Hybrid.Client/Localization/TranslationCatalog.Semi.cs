namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2C — SemiStockDashboard.razor (semi.*).
public sealed partial class TranslationCatalog
{
    private void RegisterSemi()
    {
        //     key                          vi                                                     en
        Add("semi.title",                 "Kho bán thành phẩm",                                  "Semi-Finished Store");
        Add("semi.subtitle",              "Tồn kho semi in/tape · xuất FEFO cho công đoạn dán",  "Printed/tape semi stock · FEFO issue to the taping stage");

        // KPI stat-cards
        Add("semi.kpi.available",         "Khả dụng",                                            "Available");
        Add("semi.kpi.available.sub",     "đơn vị sẵn sàng xuất",                                "units ready to issue");
        Add("semi.kpi.reserved",          "Đang giữ",                                            "Reserved");
        Add("semi.kpi.reserved.sub",      "đã reserve cho WO",                                   "reserved for WOs");
        Add("semi.kpi.expiring",          "Sắp/đã hết hạn",                                      "Expiring/expired");
        Add("semi.kpi.expiring.sub",      "lô cần ưu tiên xuất",                                 "lots to issue first");
        Add("semi.kpi.count",             "Tổng số lô",                                          "Total lots");
        Add("semi.kpi.count.sub",         "lô trong bộ lọc",                                     "lots in filter");

        // Toolbar filters
        Add("semi.filter.kind",           "Loại",                                                "Kind");
        Add("semi.filter.kind.aria",      "Lọc theo loại",                                       "Filter by kind");
        Add("semi.filter.status",         "Trạng thái",                                          "Status");
        Add("semi.filter.status.aria",    "Lọc theo trạng thái",                                 "Filter by status");
        Add("semi.filter.search",         "Tìm mã lô",                                           "Search lot");
        Add("semi.filter.search.ph",      "Quét/nhập mã lô…",                                    "Scan/enter lot no…");
        Add("semi.reload",                "Tải lại",                                             "Reload");
        Add("semi.add",                   "Nhập lô vào kho",                                     "Add lot to store");

        // Segmented filter option labels
        Add("semi.opt.all",               "Tất cả",                                              "All");
        Add("semi.opt.printed",           "In",                                                  "Printed");
        Add("semi.opt.tape",              "Tape",                                                "Tape");

        // Grid states
        Add("semi.load.failed",           "Không tải được kho:",                                 "Failed to load store:");
        Add("semi.expiry.banner",         "{0} lô sắp/đã hết hạn — ưu tiên xuất trước (FEFO).",  "{0} lots expiring/expired — issue these first (FEFO).");
        Add("semi.empty.title",           "Kho trống theo bộ lọc",                               "No lots match this filter");
        Add("semi.empty.sub",             "Chưa có lô bán thành phẩm nào khớp. Nhập lô mới để bắt đầu.", "No semi-finished lots match. Add a new lot to get started.");

        // Table columns
        Add("semi.col.lotno",             "Mã lô",                                               "Lot no.");
        Add("semi.col.kind",              "Loại",                                                "Kind");
        Add("semi.col.spec",              "Spec",                                                "Spec");
        Add("semi.col.sourcewo",          "WO nguồn",                                            "Source WO");
        Add("semi.col.produced",          "SX",                                                  "Produced");
        Add("semi.col.available",         "Khả dụng",                                            "Available");
        Add("semi.col.reserved",          "Đang giữ",                                            "Reserved");
        Add("semi.col.stock",             "Tồn",                                                 "Stock");
        Add("semi.col.status",            "Trạng thái",                                          "Status");
        Add("semi.col.expiry",            "Hạn dùng",                                            "Expiry");
        Add("semi.col.actions",           "Hành động",                                           "Actions");

        // Row bar + actions
        Add("semi.bar.title",             "Khả dụng {0} · giữ {1} / SX {2}",                     "Available {0} · reserved {1} / produced {2}");
        Add("semi.actions",               "Hành động",                                           "Actions");
        Add("semi.menu.aria",             "Hành động lô {0}",                                    "Actions for lot {0}");
        Add("semi.menu.viewwo",           "Xem WO nguồn",                                        "View source WO");

        // Status labels
        Add("semi.status.available",      "Khả dụng",                                            "Available");
        Add("semi.status.depleted",       "Đã hết",                                              "Depleted");
        Add("semi.status.expired",        "Quá hạn",                                             "Expired");

        // Expiry countdown
        Add("semi.expiry.overdue",        "quá hạn {0} ngày",                                    "{0} days overdue");
        Add("semi.expiry.today",          "hôm nay",                                             "today");
        Add("semi.expiry.remaining",      "còn {0} ngày",                                        "{0} days left");

        // Add-lot modal
        Add("semi.add.title",             "Nhập lô vào kho bán thành phẩm",                      "Add a lot to the semi-finished store");
        Add("semi.add.lotno",             "Mã lô (quét)",                                        "Lot no. (scan)");
        Add("semi.add.lotno.ph",          "Quét barcode lô hoặc nhập tay…",                      "Scan the lot barcode or type it…");
        Add("semi.add.kind",              "Loại",                                                "Kind");
        Add("semi.add.kind.printed",      "In (PRINTED_SEMI)",                                   "Printed (PRINTED_SEMI)");
        Add("semi.add.kind.tape",         "Tape (TAPE_SEMI)",                                    "Tape (TAPE_SEMI)");
        Add("semi.add.qty",               "Số lượng",                                            "Quantity");
        Add("semi.add.source",            "WO nguồn (id)",                                       "Source WO (id)");
        Add("semi.add.spec",              "Spec revision (tuỳ chọn)",                            "Spec revision (optional)");
        Add("semi.add.expiry",            "Hạn dùng (tuỳ chọn)",                                 "Expiry (optional)");
        Add("semi.add.submit",            "Nhập kho",                                            "Add to store");
        Add("semi.cancel",               "Huỷ",                                                  "Cancel");

        // Genealogy modal
        Add("semi.genea.title",           "Nguồn gốc lô",                                        "Lot genealogy");
        Add("semi.genea.lotno",           "Mã lô",                                               "Lot no.");
        Add("semi.genea.kind",            "Loại",                                                "Kind");
        Add("semi.genea.sourcewo",        "WO nguồn",                                            "Source WO");
        Add("semi.genea.spec",            "Spec revision",                                       "Spec revision");
        Add("semi.genea.qtys",            "Sản xuất / khả dụng / giữ",                           "Produced / available / reserved");
        Add("semi.genea.expiry",          "Hạn dùng",                                            "Expiry");
        Add("semi.close",                 "Đóng",                                                "Close");
    }
}

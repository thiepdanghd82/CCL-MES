namespace CCL.MES.Hybrid.Client.Localization;

// feat/iqc-module-tabs — nhãn cho 3 sub-tab IQC (Dashboard · IQC Data · New
// Ticket), picker 4 nhóm, cột grid IQC Data, và 3 form placeholder (Chemical /
// Tools / Other). Đủ VI + EN parity (skill cmes-i18n-parity) — KHÔNG hardcode.
public sealed partial class TranslationCatalog
{
    private void RegisterIqcModule()
    {
        //     key                              vi                                     en
        // ── Sub-tabs ──────────────────────────────────────────────────────
        Add("iqc.tab.dashboard",           "Bảng điều khiển",                     "Dashboard");
        Add("iqc.tab.data",                "Dữ liệu IQC",                         "IQC Data");
        Add("iqc.tab.newticket",           "Phiếu mới",                           "New Ticket");

        // ── Group names ───────────────────────────────────────────────────
        Add("iqc.group.materials",         "Nguyên liệu",                         "Materials");
        Add("iqc.group.chemical",          "Hoá chất",                            "Chemical");
        Add("iqc.group.tools",             "Dụng cụ",                             "Tools");
        Add("iqc.group.other",             "Khác",                                "Other");
        Add("iqc.group.all",               "Tất cả",                              "All");

        // ── Dashboard (KPI placeholder có số liệu thật) ──────────────────
        Add("iqc.dash.title",              "Tổng quan IQC",                       "IQC overview");
        Add("iqc.dash.desc",               "Số liệu đếm thật từ phiếu đã lưu. Sẽ bổ sung biểu đồ & xu hướng.",
                                                                                  "Live counts from saved tickets. Charts & trends to follow.");
        Add("iqc.dash.total",              "Tổng số phiếu",                       "Total tickets");
        Add("iqc.dash.bygroup",            "Theo nhóm",                           "By group");
        Add("iqc.dash.bystatus",           "Theo trạng thái",                     "By status");
        Add("iqc.dash.pending",            "Chờ kiểm",                            "Pending");
        Add("iqc.dash.pass",               "Đạt",                                 "Pass");
        Add("iqc.dash.fail",               "Không đạt",                           "Fail");
        Add("iqc.dash.placeholder.note",   "Khu vực placeholder — sẽ bổ sung KPI sắp/đã hết hạn, xu hướng theo tuần.",
                                                                                  "Placeholder area — expiring-soon KPI and weekly trends to be added.");

        // ── IQC Data (list) ───────────────────────────────────────────────
        Add("iqc.data.title",              "Phiếu đã lưu",                        "Saved tickets");
        Add("iqc.data.search.ph",          "Tìm phiếu / Code IFS / mô tả…",       "Search receipt / Code IFS / description…");
        Add("iqc.data.filter.group",       "Nhóm",                                "Group");
        Add("iqc.data.empty",              "Chưa có phiếu nào khớp bộ lọc.",       "No tickets match the filter.");
        Add("iqc.data.loading",            "Đang tải…",                           "Loading…");
        Add("iqc.data.col.receipt",        "Phiếu nhập",                          "Receipt");
        Add("iqc.data.col.group",          "Nhóm",                                "Group");
        Add("iqc.data.col.codeifs",        "Code IFS",                            "Code IFS");
        Add("iqc.data.col.matdesc",        "Mô tả vật liệu",                      "Material description");
        Add("iqc.data.col.lot",            "Số lô / batch",                       "Lot/Batch");
        Add("iqc.data.col.manfdate",       "Ngày SX",                             "Manf date");
        Add("iqc.data.col.maker",          "Nhà sản xuất",                        "Maker");
        Add("iqc.data.col.supplier",       "Nhà cung cấp",                        "Supplier");
        Add("iqc.data.col.inspector",      "Người kiểm",                          "Inspector");
        Add("iqc.data.col.received",       "Ngày nhận",                           "Received");
        Add("iqc.data.col.qty",            "SL",                                  "Qty");
        Add("iqc.data.col.status",         "Trạng thái",                          "Status");
        Add("iqc.data.status.pending",     "Chờ kiểm",                            "Pending");
        Add("iqc.data.status.pass",        "Đạt",                                 "Pass");
        Add("iqc.data.status.fail",        "Không đạt",                           "Fail");
        Add("iqc.data.pager.status",       "Trang {0}/{1} · {2} phiếu",           "Page {0}/{1} · {2} tickets");
        Add("iqc.data.pager.prev",         "‹ Trước",                             "‹ Previous");
        Add("iqc.data.pager.next",         "Sau ›",                               "Next ›");
        // Row-action menu (RowContextMenu — L35).
        Add("iqc.data.menu.aria",          "Hành động phiếu {0}",                 "Actions for ticket {0}");
        Add("iqc.data.menu.view",          "Xem",                                 "View");
        Add("iqc.data.menu.open",          "Mở",                                  "Open");
        Add("iqc.data.menu.copy",          "Sao chép",                            "Copy");
        Add("iqc.data.menu.edit",          "Sửa",                                 "Edit");
        Add("iqc.data.menu.delete",        "Xoá",                                 "Delete");
        Add("iqc.data.menu.actions",       "Hành động",                           "Actions");
        // View detail dialog.
        Add("iqc.data.view.title",         "Chi tiết phiếu",                      "Ticket detail");
        Add("iqc.data.view.close",         "Đóng",                                "Close");
        // Not-yet-wired actions (Copy/Edit/Delete = placeholder — Henry mở rộng).
        Add("iqc.data.action.notyet",      "Chức năng này sẽ được bổ sung.",       "This action will be added later.");

        // ── New Ticket — group picker (transactional Modal, L34) ─────────
        Add("iqc.newticket.title",         "Tạo phiếu mới",                       "Create a new ticket");
        Add("iqc.newticket.pick",          "Chọn nhóm nguồn nhập",                "Pick the intake group");
        Add("iqc.newticket.pick.desc",     "Chọn 1 trong 4 nhóm để mở đúng biểu mẫu.",
                                                                                  "Pick one of four groups to open the matching form.");
        Add("iqc.newticket.cta",           "＋ Phiếu mới",                        "＋ New ticket");
        Add("iqc.group.materials.hint",    "Tra mô tả → chọn Code IFS (nhiều)",   "Search description → pick Code IFS (multi)");
        Add("iqc.group.chemical.hint",     "Phiếu hoá chất nhập kho",             "Incoming chemical ticket");
        Add("iqc.group.tools.hint",        "Phiếu dụng cụ / khuôn / dao",         "Tooling / die / cutter ticket");
        Add("iqc.group.other.hint",        "Nguồn nhập khác",                     "Other intake");

        // ── Materials inspection showcard (FloatingWindow — L34) ─────────
        Add("iqc.insp.title.new",          "Phiếu thanh tra nguyên liệu",         "Materials inspection ticket");
        Add("iqc.insp.aria",               "Phiếu thanh tra {0}",                 "Inspection ticket {0}");
        Add("iqc.insp.savedraft",          "Lưu nháp",                            "Save draft");
        Add("iqc.insp.complete",           "Hoàn tất phiếu",                      "Complete ticket");
        Add("iqc.insp.maxwins",            "Đang mở tối đa {0} phiếu. Đóng bớt để mở thêm.",
                                                                                  "Up to {0} tickets open. Close one to open another.");

        // ── Placeholder forms (Chemical / Tools / Other) ─────────────────
        Add("iqc.form.placeholder.badge",  "Biểu mẫu tạm — sẽ bổ sung trường",     "Placeholder form — fields to be added");
        Add("iqc.form.placeholder.note",   "Đây là biểu mẫu tối thiểu cho nhóm này. Các trường chuyên biệt sẽ được bổ sung sau.",
                                                                                  "This is a minimal form for this group. Group-specific fields will be added later.");
        Add("iqc.ph.f.desc",               "Mô tả",                               "Description");
        Add("iqc.ph.f.desc.ph",            "Mô tả vật tư nhập",                    "Describe the intake item");
        Add("iqc.ph.f.lot",                "Số lô / batch",                       "Lot/Batch No");
        Add("iqc.ph.f.lot.ph",             "Số lô / batch",                       "Lot / batch number");
        Add("iqc.ph.f.qty",                "Số lượng",                            "Quantity");
        Add("iqc.ph.f.qty.ph",             "Số lượng nhận",                       "Received quantity");
        Add("iqc.ph.f.uom",                "Đơn vị",                              "Unit");
        Add("iqc.ph.f.uom.ph",             "vd. kg, hộp, cái",                    "e.g. kg, box, pcs");
        Add("iqc.ph.f.inspector",          "Người kiểm",                          "Inspector");
        Add("iqc.ph.f.inspector.auto",     "(tự động từ tài khoản đăng nhập)",     "(auto from the signed-in account)");
        Add("iqc.ph.save",                 "Lưu phiếu",                           "Save ticket");
        Add("iqc.ph.saving",               "Đang lưu…",                           "Saving…");
        Add("iqc.ph.cancel",               "Huỷ",                                 "Cancel");
        Add("iqc.ph.saved",                "Đã tạo phiếu {0}.",                    "Ticket {0} created.");
    }
}

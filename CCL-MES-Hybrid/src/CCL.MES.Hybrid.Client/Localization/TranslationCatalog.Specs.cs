namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2E — Pages/Specs.razor (specs.*).
public sealed partial class TranslationCatalog
{
    private void RegisterSpecs()
    {
        //     key                          vi                                          en
        Add("specs.page.title",          "Spec kỹ thuật — NPI",                       "Engineer Spec — NPI");
        Add("specs.heading",             "Spec kỹ thuật",                             "Engineer Spec");
        Add("specs.search.placeholder",  "Tìm theo Khách hàng / Mã hàng / Tên hàng…", "Search by Customer / Part No / Part Name…");
        Add("specs.search",              "Tìm",                                       "Search");
        Add("specs.reload",              "Tải lại",                                   "Reload");

        Add("specs.view.active",         "Đang dùng",                                 "Active");
        Add("specs.view.trash",          "Thùng rác",                                 "Trash");
        Add("specs.view.all",            "Tất cả",                                    "All");

        Add("specs.planner.filter.aria", "Lọc theo Bộ phận in",                       "Filter by Planner");
        Add("specs.planner.all",         "Tất cả loại",                               "All types");
        Add("specs.export.aria",         "Xuất danh sách",                            "Export list");
        Add("specs.export.openfile",     "Mở file",                                   "Open file");

        Add("specs.import.xlsx",         "Nhập xlsx",                                 "Import xlsx");
        Add("specs.create",              "Tạo Spec",                                  "Create Spec");

        Add("specs.loading",             "Đang tải…",                                 "Loading…");
        Add("specs.load.failed",         "Không tải được danh sách Spec.",            "Failed to load the Spec list.");
        Add("specs.retry",               "Thử lại",                                   "Retry");
        Add("specs.empty",               "Không tìm thấy Spec phù hợp.",              "No matching Spec found.");

        Add("specs.pager.prev",          "Trước",                                     "Prev");
        Add("specs.pager.next",          "Sau",                                       "Next");
        Add("specs.pager.status",        "Trang {0} / {1} — {2} dòng",                "Page {0} / {1} — {2} rows");

        // Trash / Restore confirm modals ({0} = spec code).
        Add("specs.trash.title",         "Chuyển Spec {0} vào Thùng rác?",            "Move Spec {0} to Trash?");
        Add("specs.trash.message",       "Spec sẽ được xoá mềm. Có thể khôi phục từ tab Thùng rác. Lưu ý: máy chủ sẽ chặn nếu vẫn còn Lệnh SX đang dùng Spec này.",
                                         "The Spec will be soft-deleted. It can be restored from the Trash tab. Note: the server will block this if a Work Order is still using this Spec.");
        Add("specs.trash.confirm",       "Chuyển vào Thùng rác",                      "Move to Trash");
        Add("specs.trash.pending",       "Đang xoá…",                                 "Deleting…");

        Add("specs.restore.title",       "Khôi phục Spec {0}?",                       "Restore Spec {0}?");
        Add("specs.restore.message",     "Spec sẽ rời Thùng rác và trở lại danh sách Đang dùng.",
                                         "The Spec will leave the Trash and return to the Active list.");
        Add("specs.restore.confirm",     "Khôi phục",                                 "Restore");
        Add("specs.restore.pending",     "Đang khôi phục…",                           "Restoring…");

        // Inline quick-edit cell tooltip.
        Add("specs.inline.quickedit",    "Bấm để sửa nhanh",                          "Click to quick-edit");

        // Grid column headers.
        Add("specs.col.planner",         "Bộ phận in",                                "Planner");
        Add("specs.col.ref_no",          "SỐ THAM CHIẾU",                             "REF NO");
        Add("specs.col.customer",        "Khách hàng",                                "Customer");
        Add("specs.col.part_no",         "Mã hàng",                                   "Part No");
        Add("specs.col.part_name",       "Tên hàng",                                  "Part Name");
        Add("specs.col.colors",          "Màu",                                       "Color");
        Add("specs.col.cavity",          "Số khuôn",                                  "Cavity");
        Add("specs.col.pitch",           "Bước (mm)",                                 "Pitch (mm)");
        Add("specs.col.inspection",      "Spec",                                      "Spec");
        Add("specs.col.status",          "Trạng thái",                                "Status");
        Add("specs.col.rev",             "Phiên bản",                                 "Rev");
        Add("specs.col.rev_date",        "Ngày sửa",                                  "Rev Date");
        Add("specs.col.rev_by",          "Người sửa",                                 "By");
        Add("specs.col.spec_code",       "Mã Spec",                                   "Spec Code");
        Add("specs.col.title",           "Tiêu đề",                                   "Title");
        Add("specs.col.process_code",    "Mã công đoạn",                              "Process code");
        Add("specs.col.effective_from",  "Hiệu lực từ",                               "Effective From");
        Add("specs.col.approved_by",     "Người duyệt",                               "Approved By");
    }
}

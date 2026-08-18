namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Pages/NpiWorkCenters.razor (npiwc.*).
public sealed partial class TranslationCatalog
{
    private void RegisterNpiWc()
    {
        //     key                              vi                                                  en
        Add("npiwc.pagetitle",              "Trung tâm sản xuất — NPI",                          "Work Centers — NPI");
        Add("npiwc.heading",                "Trung tâm sản xuất",                                "Work Centers");

        Add("npiwc.search.placeholder",     "Tìm theo mã hoặc mô tả…",                           "Search by code or description…");

        Add("npiwc.action.search",          "Tìm",                                               "Search");
        Add("npiwc.action.reload",          "Tải lại",                                           "Reload");
        Add("npiwc.action.exportcsv",       "Xuất CSV",                                          "Export CSV");
        Add("npiwc.action.import",          "Nhập",                                              "Import");
        Add("npiwc.action.add",             "Thêm",                                              "Add");
        Add("npiwc.action.retry",           "Thử lại",                                           "Retry");
        Add("npiwc.action.cancel",          "Huỷ",                                               "Cancel");
        Add("npiwc.action.save",            "Lưu",                                               "Save");
        Add("npiwc.action.saving",          "Đang lưu…",                                         "Saving…");
        Add("npiwc.action.delete",          "Xoá",                                               "Delete");
        Add("npiwc.action.deleting",        "Đang xoá…",                                         "Deleting…");
        Add("npiwc.action.close",           "Đóng",                                              "Close");

        Add("npiwc.loading",                "Đang tải…",                                         "Loading…");
        Add("npiwc.error.loadfailed",       "Không tải được dữ liệu.",                           "Failed to load data.");
        Add("npiwc.error.connect",          "Không kết nối được máy chủ: {0}",                   "Could not connect to the server: {0}");
        Add("npiwc.empty",                  "Không có dữ liệu phù hợp.",                         "No matching data.");

        Add("npiwc.col.actions",            "Hành động",                                         "Actions");
        Add("npiwc.col.id",                 "ID",                                                "ID");
        Add("npiwc.col.code",               "Mã trung tâm SX",                                   "Work Center Code");
        Add("npiwc.col.description",        "Mô tả",                                             "Description");
        Add("npiwc.col.area",               "Khu vực",                                           "Area");
        Add("npiwc.col.idealspeed",         "Tốc độ chuẩn (pcs/h)",                              "Ideal Speed (pcs/h)");
        Add("npiwc.col.shift",              "Ca làm việc",                                       "Shift Pattern");
        Add("npiwc.col.status",             "Trạng thái",                                        "Status");

        Add("npiwc.pager.previous",         "Trước",                                             "Previous");
        Add("npiwc.pager.status",           "Trang {0} / {1} — {2} dòng",                        "Page {0} / {1} — {2} rows");
        Add("npiwc.pager.next",             "Sau",                                               "Next");

        Add("npiwc.menu.aria",              "Hành động cho {0}",                                 "Actions for {0}");
        Add("npiwc.menu.copy",              "Sao chép",                                          "Copy");
        Add("npiwc.menu.edit",              "Sửa",                                               "Edit");
        Add("npiwc.menu.delete",            "Xoá",                                               "Delete");

        Add("npiwc.form.title.add",         "Thêm trung tâm sản xuất",                           "Add Work Center");
        Add("npiwc.form.title.edit",        "Sửa trung tâm sản xuất — {0}",                      "Edit Work Center — {0}");

        Add("npiwc.field.code",             "Mã",                                                "Code");
        Add("npiwc.field.code.placeholder", "vd. PR-01",                                         "e.g. PR-01");
        Add("npiwc.field.code.hint",        "3–12 ký tự: A–Z, 0–9, '-', '_'",                    "3–12 chars: A–Z, 0–9, '-', '_'");
        Add("npiwc.field.description",      "Mô tả",                                             "Description");
        Add("npiwc.field.area",             "Khu vực",                                           "Area");
        Add("npiwc.field.idealspeed",       "Tốc độ chuẩn (pcs/h)",                              "Ideal Speed (pcs/h)");
        Add("npiwc.field.shiftpattern",     "Ca làm việc",                                       "Shift Pattern");
        Add("npiwc.field.active",           "Đang hoạt động",                                    "Active");

        Add("npiwc.delete.title",           "Xoá trung tâm sản xuất",                            "Delete Work Center");
        Add("npiwc.delete.prefix",          "Xoá",                                               "Delete");
        Add("npiwc.delete.suffix",          "? Không thể hoàn tác.",                             "? This cannot be undone.");

        Add("npiwc.import.title",           "Nhập trung tâm sản xuất (CSV)",                     "Import Work Centers (CSV)");
        Add("npiwc.import.hint",            "Cập nhật theo Mã (dòng đã có được cập nhật, dòng mới được thêm; không xoá dòng nào).",
                                                                                                 "Upsert by Code (existing rows updated, new rows inserted; nothing is deleted).");
        Add("npiwc.import.columns",         "Cột: Code, Description, Area, Ideal Speed (pcs/h), Shift Pattern, Active.",
                                                                                                 "Columns: Code, Description, Area, Ideal Speed (pcs/h), Shift Pattern, Active.");
        Add("npiwc.import.inserted",        "đã thêm",                                           "inserted");
        Add("npiwc.import.updated",         "đã cập nhật",                                       "updated");
        Add("npiwc.import.skipped",         "đã bỏ qua",                                         "skipped");
        Add("npiwc.import.rowprefix",       "Dòng {0}:",                                        "Row {0}:");
        Add("npiwc.import.nofile",          "Chưa chọn tệp.",                                    "No file selected.");
        Add("npiwc.import.importing",       "Đang nhập…",                                        "Importing…");
        Add("npiwc.import.choosecsv",       "Chọn CSV…",                                         "Choose CSV…");

        Add("npiwc.toast.added",            "Đã thêm {0}.",                                      "Added {0}.");
        Add("npiwc.toast.saved",            "Đã lưu {0}.",                                       "Saved {0}.");
        Add("npiwc.toast.deleted",          "Đã xoá {0}.",                                       "Deleted {0}.");
        Add("npiwc.toast.exportready",      "Đã sẵn sàng xuất — workcenters.csv",                "Export ready — workcenters.csv");

        Add("npiwc.status.active",          "Hoạt động",                                         "Active");
        Add("npiwc.status.inactive",        "Ngừng",                                             "Inactive");
    }
}

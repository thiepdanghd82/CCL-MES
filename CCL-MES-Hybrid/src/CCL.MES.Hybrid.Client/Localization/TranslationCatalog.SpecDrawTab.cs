namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2E — Shared/SpecDrawingsTab.razor (specdrawtab.*).
public sealed partial class TranslationCatalog
{
    private void RegisterSpecDrawTab()
    {
        //     key                                       vi                                                              en
        Add("specdrawtab.empty.norevision",           "Chưa có bản vẽ nào cho phiên bản này.",                          "No drawings yet for this revision.");

        // Upload button + banners
        Add("specdrawtab.upload.new",                 "⤒ Tải lên bản mới",                                            "⤒ Upload new version");
        Add("specdrawtab.upload.busy",                "Đang tải lên…",                                                "Uploading…");
        Add("specdrawtab.upload.failed",              "Tải lên thất bại.",                                             "Upload failed.");

        // Version list
        Add("specdrawtab.version.none",               "Chưa có bản tải lên.",                                          "No uploads yet.");
        Add("specdrawtab.version.fname.title",        "{0}  ·  gốc: {1}",                                              "{0}  ·  original: {1}");

        // Version row actions
        Add("specdrawtab.action.open",                "↗ Mở",                                                          "↗ Open");
        Add("specdrawtab.action.open.busy",           "Đang mở…",                                                     "Opening…");
        Add("specdrawtab.action.save",                "↓ Lưu",                                                         "↓ Save");
        Add("specdrawtab.action.save.title",          "Lưu một bản sao vào thiết bị này",                             "Save a copy to this device");
        Add("specdrawtab.action.delete",              "🗑 Xoá",                                                        "🗑 Delete");
        Add("specdrawtab.action.delete.title",        "Xoá bản này (cần mật khẩu NPI)",                               "Delete this version (NPI password required)");

        // In-app preview
        Add("specdrawtab.preview.loading",            "Đang tải…",                                                    "Loading…");

        // Delete-version modal
        Add("specdrawtab.delete.title",               "Xoá bản vẽ",                                                    "Delete drawing version");
        Add("specdrawtab.delete.warn",                "Hành động này xoá vĩnh viễn bản vẽ. Cần xác thực bằng tài khoản thuộc",
                                                      "This permanently deletes the drawing. Authentication with an account belonging to the");
        Add("specdrawtab.delete.warn.team",           "NPI team",                                                      "NPI team");
        Add("specdrawtab.delete.warn.tail",           ".",                                                             " is required.");
        Add("specdrawtab.delete.field.user",          "Tài khoản NPI",                                                 "NPI account");
        Add("specdrawtab.delete.field.user.ph",       "tên đăng nhập",                                                 "username");
        Add("specdrawtab.delete.field.pwd",           "Mật khẩu",                                                      "Password");
        Add("specdrawtab.delete.field.reason",        "Lý do (tuỳ chọn)",                                              "Reason (optional)");
        Add("specdrawtab.delete.field.reason.ph",     "Ví dụ: tải nhầm file",                                          "e.g. uploaded the wrong file");
        Add("specdrawtab.delete.cancel",              "Huỷ",                                                           "Cancel");
        Add("specdrawtab.delete.confirm",             "Xoá bản vẽ",                                                    "Delete drawing");
        Add("specdrawtab.delete.confirm.busy",        "Đang xoá…",                                                    "Deleting…");

        // Notices (success toasts) — {0} version no · {1} name · {2} size
        Add("specdrawtab.notice.uploaded",            "Đã tải lên v{0} · {1} ({2}).",                                 "Uploaded v{0} · {1} ({2}).");
        Add("specdrawtab.notice.opened",              "Đã mở v{0} · {1} ({2}).",                                       "Opened v{0} · {1} ({2}).");
        Add("specdrawtab.notice.saved",              "Đã lưu file vào {0} (không thể mở tự động).",                    "Saved the file to {0} (could not open automatically).");
        Add("specdrawtab.notice.deleted",             "Đã xoá v{0} · {1}.",                                            "Deleted v{0} · {1}.");
        Add("specdrawtab.notice.decided",             "Đã ghi nhận quyết định cho v{0}",                              "Recorded the decision for v{0}");
        Add("specdrawtab.notice.decided.superseded",  " · {0} bản cũ được đánh dấu Thay thế.",                         " · {0} older version(s) marked Superseded.");

        // Error messages
        Add("specdrawtab.err.connect",                "Không kết nối được máy chủ: {0}",                               "Could not connect to the server: {0}");
        Add("specdrawtab.err.upload.timeout",         "Tải lên quá lâu (hơn 90 giây) — kiểm tra mạng và thử lại.",      "Upload took too long (over 90 seconds) — check your network and try again.");
        Add("specdrawtab.err.preview",                "Không tạo được bản xem trước cho file này — hãy dùng ↓ Lưu.",   "Could not build a preview for this file — use ↓ Save instead.");

        // Approval chip tooltips — {0} role · {1} status · {2} actor · {3} time
        Add("specdrawtab.chip.title.acted",           "{0} — {1} bởi {2} · {3}",                                       "{0} — {1} by {2} · {3}");
        Add("specdrawtab.chip.title.reason",          "Lý do: {0}",                                                    "Reason: {0}");
        Add("specdrawtab.chip.title.pending",         "{0} — {1}",                                                     "{0} — {1}");

        // Drawing-kind labels
        Add("specdrawtab.kind.customerdrawing",       "Bản vẽ khách hàng",                                             "Customer drawing");
        Add("specdrawtab.kind.npiprintlayout",        "Layout in (NPI)",                                               "Print layout (NPI)");
        Add("specdrawtab.kind.npicutlayout",          "Layout bế (NPI)",                                               "Cut layout (NPI)");
        Add("specdrawtab.kind.ipqcprintref",          "Mẫu in tham chiếu IPQC",                                        "IPQC Print reference");
        Add("specdrawtab.kind.ipqccutref",            "Mẫu bế tham chiếu IPQC",                                        "IPQC Cut reference");
        Add("specdrawtab.kind.fqcchecksheet",         "Phiếu kiểm FQC",                                                "FQC Checksheet");
        Add("specdrawtab.kind.oqcchecksheet",         "Phiếu kiểm OQC",                                                "OQC Checksheet");
        Add("specdrawtab.kind.customerapproval",      "Phê duyệt khách hàng",                                          "Customer approval");
        Add("specdrawtab.kind.internalproof",         "Bản duyệt nội bộ",                                              "Internal proof");

        // Drawing-slot status labels
        Add("specdrawtab.slotstatus.draft",           "Nháp",                                                          "Draft");
        Add("specdrawtab.slotstatus.pending",         "Chờ duyệt",                                                     "Pending Approval");
        Add("specdrawtab.slotstatus.approved",        "Đã duyệt",                                                      "Approved");
        Add("specdrawtab.slotstatus.superseded",      "Đã thay thế",                                                   "Superseded");
        Add("specdrawtab.slotstatus.withdrawn",       "Đã thu hồi",                                                    "Withdrawn");

        // Drawing-version status labels
        Add("specdrawtab.verstatus.draft",            "Nháp",                                                          "Draft");
        Add("specdrawtab.verstatus.pending",          "Chờ duyệt",                                                     "Pending Approval");
        Add("specdrawtab.verstatus.approved",         "Đã duyệt",                                                      "Approved");
        Add("specdrawtab.verstatus.rejected",         "Bị từ chối",                                                    "Rejected");
        Add("specdrawtab.verstatus.superseded",       "Đã thay thế",                                                   "Superseded");

        // Approval-role labels (NPI / QC kept as technical role tokens)
        Add("specdrawtab.approlerole.npi",            "NPI",                                                           "NPI");
        Add("specdrawtab.approlerole.production",     "Sản xuất",                                                      "Production");
        Add("specdrawtab.approlerole.qc",             "QC",                                                            "QC");

        // Approval-status labels
        Add("specdrawtab.appstatus.pending",          "Chờ duyệt",                                                     "Pending");
        Add("specdrawtab.appstatus.approved",         "Đã duyệt",                                                      "Approved");
        Add("specdrawtab.appstatus.rejected",         "Bị từ chối",                                                    "Rejected");
    }
}

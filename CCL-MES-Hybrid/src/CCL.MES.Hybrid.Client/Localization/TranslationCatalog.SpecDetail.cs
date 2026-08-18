namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2E — Pages/SpecDetailPage.razor (specdetail.*).
public sealed partial class TranslationCatalog
{
    private void RegisterSpecDetail()
    {
        //     key                                          vi                                                              en

        // Page title + header crumb + back link.
        Add("specdetail.title.spec",                     "Spec",                                                         "Spec");
        Add("specdetail.title.details",                  "Chi tiết",                                                     "Details");
        Add("specdetail.back.list",                      "Về danh sách",                                                 "Back to list");
        Add("specdetail.crumb.rev",                      "Phiên bản",                                                    "Rev");

        // Lifecycle action buttons.
        Add("specdetail.action.approve",                 "Duyệt",                                                      "Approve");
        Add("specdetail.action.approving",               "Đang duyệt…",                                                  "Approving…");
        Add("specdetail.action.edit",                    "Sửa",                                                          "Edit");
        Add("specdetail.action.copy",                    "Sao chép",                                                     "Copy");
        Add("specdetail.action.revise",                  "Tạo phiên bản mới",                                            "Revise");
        Add("specdetail.action.supersede",               "Đánh dấu thay thế",                                            "Mark Superseded");
        Add("specdetail.action.trash",                   "Chuyển vào Thùng rác",                                         "Move to Trash");

        // Print / PDF export.
        Add("specdetail.pdf.print",                      "In PDF",                                                    "Print PDF");
        Add("specdetail.pdf.generating",                 "Đang tạo tệp…",                                                "Generating file…");
        Add("specdetail.pdf.failed",                     "Xuất tệp thất bại: {0}",                                       "Export failed: {0}");
        // Print / export toolbar — In (native OS panel) · PDF · Excel.
        Add("specdetail.print.group.aria",               "In và xuất tờ Spec",                                           "Print and export the Spec sheet");
        Add("specdetail.print.native",                   "In",                                                        "Print");
        Add("specdetail.export.pdf",                     "⬇ PDF",                                                        "⬇ PDF");
        Add("specdetail.export.excel",                   "⬇ Excel",                                                      "⬇ Excel");
        // P11.x — native WebView print (hộp in hệ điều hành, WYSIWYG).
        Add("specdetail.print.native.opened",            "Đã mở hộp in của hệ điều hành — chọn khổ giấy (A4/A3), hướng, tỷ lệ hoặc Lưu thành PDF.", "Opened the system print panel — choose paper size (A4/A3), orientation, scale, or Save as PDF.");

        // Export banner actions + outcome messages.
        Add("specdetail.export.openfile",                "Mở tệp",                                                       "Open file");
        Add("specdetail.export.saved.opened",            "Đã lưu {0} ({1}) — đã mở để xem.",                             "Saved {0} ({1}) — opened for viewing.");
        Add("specdetail.export.saved.at",                "Đã lưu {0} ({1}) tại {2}.",                                    "Saved {0} ({1}) at {2}.");
        Add("specdetail.export.cancelled.opened",        "Bạn đã hủy hộp thoại lưu — tệp vẫn nằm trong thư mục tải xuống của ứng dụng và đã được mở để xem.", "You cancelled the save dialog — the file is still in the app's downloads folder and has been opened for viewing.");
        Add("specdetail.export.cancelled.saved",         "Bạn đã hủy hộp thoại lưu — tệp đã được lưu trong thư mục tải xuống của ứng dụng (bấm Mở tệp để xem).", "You cancelled the save dialog — the file was saved in the app's downloads folder (click Open file to view).");

        // Load / error states.
        Add("specdetail.loading",                        "Đang tải chi tiết Spec…",                                      "Loading Spec details…");
        Add("specdetail.load.failed",                    "Không tải được chi tiết Spec.",                                "Failed to load Spec details.");
        Add("specdetail.retry",                          "Thử lại",                                                      "Retry");
        Add("specdetail.error.notfound",                 "Không tìm thấy Spec này (404).",                               "This Spec was not found (404).");
        Add("specdetail.error.connect",                  "Không kết nối được tới máy chủ: {0}",                          "Could not connect to the server: {0}");

        // Tab labels.
        Add("specdetail.tab.spec",                       "Thông số kỹ thuật",                                            "Specification");
        Add("specdetail.tab.drawings",                   "Bản vẽ",                                                       "Drawings");
        Add("specdetail.tab.qcplans",                    "Kế hoạch QC",                                                  "QC Plans");
        Add("specdetail.tab.qccapture",                  "Ghi nhận QC",                                                  "QC Capture");
        Add("specdetail.tab.artwork",                    "Thiết kế",                                                     "Artwork");
        Add("specdetail.tab.setup",                      "Cài đặt máy",                                                  "Setup");

        // Specification display mode toggle.
        Add("specdetail.display.aria",                   "Chế độ hiển thị Spec",                                         "Spec display");
        Add("specdetail.mode.summary",                   "Tóm tắt",                                                      "Summary");
        Add("specdetail.mode.full",                      "Đầy đủ",                                                       "Full");
        Add("specdetail.mode.compare",                   "So sánh với phiên bản trước",                                  "Compare with previous rev");
        Add("specdetail.mode.compare.on",                "(bật)",                                                        "(on)");

        // Compare-with-previous states.
        Add("specdetail.compare.loading",                "Đang tải phiên bản trước để so sánh…",                         "Loading the previous rev to compare…");
        Add("specdetail.compare.failed",                 "Không tải được phiên bản trước.",                              "Failed to load the previous rev.");
        Add("specdetail.compare.prev.missing",           "Phiên bản trước (id={0}) đã bị xóa hoặc không truy cập được.", "The previous rev (id={0}) has been deleted or is not accessible.");

        // Copy-new-Spec chooser modal.
        Add("specdetail.copy.header",                    "Sao chép Spec mới",                                            "Copy new Spec");
        Add("specdetail.copy.lead",                      "Bạn muốn sao chép spec này như thế nào?",                      "How do you want to copy this spec?");
        Add("specdetail.copy.updatever.title",           "Cập nhật phiên bản",                                        "Update ver");
        Add("specdetail.copy.updatever.sub",             "Phiên bản mới của CHÍNH spec này — cùng sản phẩm & mã IFS, phiên bản kế tiếp (B, C…), liên kết với phiên bản hiện tại.", "New version of THIS spec — same product & IFS code, next revision (B, C…), linked to the current rev.");
        Add("specdetail.copy.new.title",                 "Mới",                                                        "New");
        Add("specdetail.copy.new.sub",                   "Spec độc lập hoàn toàn mới — sản phẩm mới, mã IFS để trống. Chỉnh sửa tự do mọi thứ.", "Brand-new independent spec — fresh product, blank IFS code. Edit everything freely.");
        Add("specdetail.cancel",                         "Hủy",                                                          "Cancel");

        // Move-to-Trash confirm modal.
        Add("specdetail.trash.title",                    "Chuyển Spec {0} vào Thùng rác?",                               "Move Spec {0} to Trash?");
        Add("specdetail.trash.message",                  "Spec sẽ bị xóa mềm. Có thể khôi phục từ tab Thùng rác. Lưu ý: máy chủ sẽ chặn nếu vẫn còn Lệnh SX đang dùng Spec này.", "The Spec will be soft-deleted. It can be restored from the Trash tab. Note: the server will block this if a Work Order is still using this Spec.");
        Add("specdetail.trash.pending",                  "Đang xóa…",                                                    "Deleting…");

        // Post-mutation banners.
        Add("specdetail.banner.approved",                "Đã duyệt — trạng thái mới: {0}",                             "Approved — new status: {0}");
        Add("specdetail.banner.saved",                   "Đã lưu thay đổi.",                                           "Changes saved.");
        Add("specdetail.banner.nochanges",               "Không có thay đổi để lưu.",                                    "No changes to save.");
        Add("specdetail.banner.superseded",              "Đã đánh dấu thay thế — trạng thái mới: {0}",                 "Marked Superseded — new status: {0}");

        // Drawings tab.
        Add("specdetail.drawings.loading",               "Đang tải bản vẽ…",                                             "Loading drawings…");
        Add("specdetail.drawings.failed",                "Không tải được Bản vẽ.",                                       "Failed to load Drawings.");

        // Drawing-kind labels (technical acronyms NPI/IPQC/FQC/OQC kept).
        Add("specdetail.drawingkind.customerdrawing",    "Bản vẽ khách hàng",                                            "Customer drawing");
        Add("specdetail.drawingkind.npiprintlayout",     "Layout in (NPI)",                                              "Print layout (NPI)");
        Add("specdetail.drawingkind.npicutlayout",       "Layout bế (NPI)",                                              "Cut layout (NPI)");
        Add("specdetail.drawingkind.ipqcprintref",       "Tham chiếu in IPQC",                                           "IPQC Print reference");
        Add("specdetail.drawingkind.ipqccutref",         "Tham chiếu bế IPQC",                                           "IPQC Cut reference");
        Add("specdetail.drawingkind.fqcchecksheet",      "Phiếu kiểm FQC",                                               "FQC Checksheet");
        Add("specdetail.drawingkind.oqcchecksheet",      "Phiếu kiểm OQC",                                               "OQC Checksheet");
        Add("specdetail.drawingkind.customerapproval",   "Phê duyệt khách hàng",                                         "Customer approval");
        Add("specdetail.drawingkind.internalproof",      "Bản duyệt nội bộ",                                             "Internal proof");

        // QC Plans tab.
        Add("specdetail.qcplans.loading",                "Đang tải Kế hoạch QC…",                                        "Loading QC Plans…");
        Add("specdetail.qcplans.failed",                 "Không tải được Kế hoạch QC.",                                  "Failed to load QC Plans.");

        // QC Capture tab.
        Add("specdetail.qccapture.loading",              "Đang tải Ghi nhận QC…",                                        "Loading QC Capture…");
        Add("specdetail.qccapture.failed",               "Không tải được Ghi nhận QC.",                                  "Failed to load QC Capture.");

        // Artwork tab (stub).
        Add("specdetail.artwork.preview",                "Khu vực xem trước thiết kế (bật/tắt 4 lớp SVG).",              "Artwork preview area (SVG 4-layer toggle).");
        Add("specdetail.artwork.deferred",               "Sẽ có ở bản 5e — kèm 3 mẫu (brady-safety / ccl-cosmetic / panel-face) khớp với SpecHub.", "Coming in 5e — with 3 templates (brady-safety / ccl-cosmetic / panel-face) matching SpecHub.");

        // Setup tab.
        Add("specdetail.setup.section.pressmaterial",    "Máy in / Vật liệu",                                            "Press / Material");
        Add("specdetail.setup.substrate",                "Vật liệu nền",                                                 "Substrate");
        Add("specdetail.setup.brand",                    "Thương hiệu",                                                  "Brand");
        Add("specdetail.setup.adhesive",                 "Keo dán",                                                      "Adhesive");
        Add("specdetail.setup.adhesivebrand",            "Thương hiệu keo",                                              "Adhesive brand");
        Add("specdetail.setup.thickness",                "Độ dày (µm)",                                                  "Thickness (µm)");
        Add("specdetail.setup.lamination",               "Cán màng",                                                     "Lamination");
        Add("specdetail.setup.varnish",                  "Phủ bóng",                                                     "Varnish");
        Add("specdetail.setup.pitch",                    "Bước răng (mm)",                                               "Pitch (mm)");
        Add("specdetail.setup.printingcavity",           "Số hàng in",                                                   "Printing cavity");
        Add("specdetail.setup.whiteunderprint",          "In lót trắng",                                                 "White underprint");
        Add("specdetail.setup.deferred",                 "Bảng dòng in Flexo / màu lụa sẽ có ở bản 5c (Showcard đầy đủ).", "The Flexo print rows / Silk colors table is coming in 5c (Full showcard).");

        // Yes / No cell values.
        Add("specdetail.yes",                            "Có",                                                           "Yes");
        Add("specdetail.no",                             "Không",                                                        "No");
    }
}

namespace CCL.MES.Hybrid.Client.Localization;

// QA / mini-QMS platform shell + module chrome (qms.* UI labels). Data values
// (lot ids, material names, results) are static mock content, not translated —
// only the chrome (topbar, stepper, headers, buttons, pills) goes through T().
public sealed partial class TranslationCatalog
{
    private void RegisterQmsUi()
    {
        //     key                        vi                                          en
        // ── Shell — topbar ────────────────────────────────────────────────
        Add("qms.module.prefix",       "Module",                                   "Module");
        Add("qms.shift.label",         "Ca sản xuất",                              "Production shift");

        // ── Module titles ─────────────────────────────────────────────────
        Add("qms.mod.dashboard.title", "Tổng quan chất lượng",                     "Quality overview");
        Add("qms.mod.iqc.title",       "Kiểm tra nguyên vật liệu đầu vào",         "Incoming material inspection");
        Add("qms.mod.ipqc.title",      "Kiểm tra trong quá trình sản xuất",        "In-process inspection");
        Add("qms.mod.oqc.title",       "Kiểm tra xuất hàng",                       "Outgoing inspection");
        Add("qms.mod.icra.title",      "Đối sách & hành động khắc phục",           "Corrective action / CAPA");

        // ── Stepper steps (label flips; the EN sub-line is the same both) ──
        Add("qms.step.hsf",            "Hồ sơ tài liệu",                           "Documents");
        Add("qms.step.hsf.en",         "HSF Documents",                            "HSF Documents");
        Add("qms.step.visual",         "Ngoại quan",                               "Visual");
        Add("qms.step.visual.en",      "Visual",                                   "Visual");
        Add("qms.step.functional",     "Chức năng",                                "Functional");
        Add("qms.step.functional.en",  "Functional",                              "Functional");
        Add("qms.step.defect",         "Mã lỗi & kết luận",                        "Defect & verdict");
        Add("qms.step.defect.en",      "Defect Codes",                            "Defect Codes");
        Add("qms.step.history",        "Tra cứu lịch sử",                          "History");
        Add("qms.step.history.en",     "History Lookup",                          "History Lookup");
        Add("qms.step.dimension",      "Kích thước",                               "Dimension");
        Add("qms.step.dimension.en",   "Dimension",                               "Dimension");

        // ── Verdict / status pills ────────────────────────────────────────
        Add("qms.verdict.pass",        "PASS",                                     "PASS");
        Add("qms.verdict.ng",          "NG",                                       "NG");
        Add("qms.status.valid",        "Còn hạn",                                  "Valid");
        Add("qms.status.expired",      "Hết hạn",                                  "Expired");
        Add("qms.toggle.ok",           "OK",                                       "OK");
        Add("qms.toggle.ng",           "NG",                                       "NG");

        // ── Common actions ────────────────────────────────────────────────
        Add("qms.act.savedraft",       "Lưu nháp",                                 "Save draft");
        Add("qms.act.complete",        "Hoàn tất phiếu",                           "Complete ticket");
        Add("qms.act.savefirst",       "Lưu kết quả mẫu đầu",                      "Save first-article");
        Add("qms.act.assigndefect",    "Gắn mã lỗi P",                             "Assign P-defect");
        Add("qms.act.adddoc",          "+ Thêm tài liệu",                          "+ Add document");
        Add("qms.act.additem",         "+ Thêm hạng mục",                          "+ Add item");
        Add("qms.act.adddefect",       "+ Thêm",                                   "+ Add");
        Add("qms.act.remove",          "Remove",                                   "Remove");
        Add("qms.act.lookup",          "Tra cứu",                                  "Look up");
        Add("qms.act.tracesource",     "Tra ngược dữ liệu",                        "Trace source");

        // ── IQC — document (HSF) table ────────────────────────────────────
        Add("qms.iqc.section",         "Hồ sơ tài liệu — HSF",                     "Documents — HSF");
        Add("qms.iqc.section.desc",    "Tài liệu hết hạn tự động chuyển đỏ. Gỡ tài liệu cũ khi đã thay bằng bản mới — dữ liệu lịch sử vẫn được giữ.",
                                                                                   "Expired documents turn red automatically. Remove superseded files once replaced — history is preserved.");
        Add("qms.iqc.col.doc",         "Tài liệu",                                 "Document");
        Add("qms.iqc.col.no",          "Số hiệu",                                  "Number");
        Add("qms.iqc.col.issue",       "Ngày issue",                               "Issue date");
        Add("qms.iqc.col.expiry",      "Hạn sử dụng",                              "Expiry");
        Add("qms.iqc.col.modified",    "Người sửa đổi lần cuối",                   "Last modified by");
        Add("qms.iqc.col.status",      "Trạng thái",                               "Status");
        Add("qms.iqc.col.action",      "Thao tác",                                 "Action");
        Add("qms.f.receipt",           "Phiếu nhập",                               "Receipt");
        Add("qms.f.material",          "NVL",                                      "Material");
        Add("qms.f.supplier",          "Nhà cung cấp",                             "Supplier");
        Add("qms.f.inspector",         "Người kiểm",                               "Inspector");

        // ── IPQC — material reconciliation + visual check ─────────────────
        Add("qms.ipqc.match",          "Đối chiếu nguyên vật liệu",                "Material reconciliation");
        Add("qms.ipqc.match.desc",     "NVL đã khai báo ở lần đầu tạo mã hàng, hệ thống tự hiện lại. Đối chiếu thực tế tại máy rồi xác nhận từng dòng — phải OK toàn bộ mới cho phép setup máy.",
                                                                                   "Materials declared when the product was created are shown back. Reconcile against the machine and confirm each row — all must be OK before setup is allowed.");
        Add("qms.ipqc.col.sysmat",     "NVL theo hệ thống",                        "Material (system)");
        Add("qms.ipqc.col.matcode",    "Mã NVL",                                   "Material code");
        Add("qms.ipqc.col.sourcelot",  "Lô IQC nguồn",                             "Source IQC lot");
        Add("qms.ipqc.col.actual",     "Thực tế tại máy",                          "Actual at machine");
        Add("qms.ipqc.col.confirm",    "Xác nhận",                                 "Confirm");
        Add("qms.ipqc.banner",         "Có NVL lệch so với master data — khoá bước setup máy cho tới khi xử lý xong.",
                                                                                   "A material diverges from master data — machine setup is locked until resolved.");
        Add("qms.ipqc.check",          "Kiểm tra ngoại quan",                      "Visual inspection");
        Add("qms.ipqc.check.desc",     "Mẫu đầu — sau setup máy. Hạng mục do IPQC Leader cấu hình theo mã hàng & process.",
                                                                                   "First article — after setup. Items configured by the IPQC leader per product & process.");
        Add("qms.f.lot",               "Phiếu hàng · Lot ticket",                  "Lot ticket");
        Add("qms.f.product",           "Mã hàng",                                  "Product");
        Add("qms.f.machine",           "Máy sản xuất",                             "Machine");
        Add("qms.f.operator",          "Người sản xuất",                           "Operator");
        Add("qms.f.masterdata",        "Master data",                              "Master data");

        // ── Shared check table (IPQC / OQC) ───────────────────────────────
        Add("qms.check.col.item",      "Hạng mục",                                 "Item");
        Add("qms.check.col.process",   "Process",                                  "Process");
        Add("qms.check.col.method",    "Phương pháp",                              "Method");
        Add("qms.check.col.spec",      "Tiêu chuẩn (spec)",                        "Spec");
        Add("qms.check.col.result",    "Kết quả",                                  "Result");
        Add("qms.check.col.verdict",   "Đánh giá",                                 "Verdict");

        // ── OQC — auto-trace + lot decision + iCRA trigger ────────────────
        Add("qms.oqc.lotinput",        "Nhập phiếu hàng · Lot ticket",             "Lot ticket entry");
        Add("qms.oqc.lotformat",       "Định dạng: LOT-YYMMDD-máy-STT",            "Format: LOT-YYMMDD-machine-seq");
        Add("qms.oqc.trace",           "Truy xuất tự động từ IPQC → IQC",          "Auto-trace IPQC → IQC");
        Add("qms.oqc.check",           "Kiểm tra ngoại quan — lô xuất",            "Visual inspection — outgoing lot");
        Add("qms.oqc.notes",           "Mã lỗi O & ghi chú",                       "O-defect codes & notes");
        Add("qms.f.ipqcfirst",         "IPQC mẫu đầu",                             "IPQC first article");
        Add("qms.dec.title",           "Quyết định lô",                            "Lot decision");
        Add("qms.dec.lotqty",          "SL lô",                                    "Lot qty");
        Add("qms.dec.sampleqty",       "SL kiểm mẫu",                              "Sampled qty");
        Add("qms.dec.ngfound",         "NG phát hiện",                             "NG found");
        Add("qms.dec.accept",          "Accept",                                   "Accept");
        Add("qms.dec.reject",          "Reject",                                   "Reject");
        Add("qms.icra.kicker",         "Kích hoạt iCRA",                           "Trigger iCRA");
        Add("qms.icra.trigger.title",  "Lô bị reject → tạo yêu cầu đối sách",      "Lot rejected → raise a corrective request");
        Add("qms.icra.trigger.desc",   "Bản ghi iCRA sẽ gắn theo mã sản phẩm và hiển thị cho IQC/IPQC cho tới khi Admin gỡ bỏ.",
                                                                                   "The iCRA record binds to the product and shows to IQC/IPQC until an admin clears it.");
        Add("qms.icra.trigger.cta",    "Tạo iCRA →",                               "Create iCRA →");

        // ── iCRA module (list placeholder) ────────────────────────────────
        Add("qms.icra.list.title",     "Yêu cầu đối sách đang mở",                 "Open corrective requests");
        Add("qms.icra.list.desc",      "Danh sách iCRA gắn theo mã sản phẩm. Mở khi một lô OQC bị reject.",
                                                                                   "iCRA requests bound per product. Opened when an OQC lot is rejected.");
        Add("qms.icra.col.code",       "Mã iCRA",                                  "iCRA code");
        Add("qms.icra.col.product",    "Mã sản phẩm",                              "Product");
        Add("qms.icra.col.reason",     "Lý do",                                    "Reason");
        Add("qms.icra.col.opened",     "Mở lúc",                                   "Opened");
        Add("qms.icra.col.state",      "Trạng thái",                               "State");
        Add("qms.icra.state.open",     "Đang mở",                                  "Open");
    }
}

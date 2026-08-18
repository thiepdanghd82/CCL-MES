namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Pages/QmsQueue.razor (qms.*).
public sealed partial class TranslationCatalog
{
    private void RegisterQms()
    {
        //     key                    vi                                        en
        Add("qms.pagetitle",       "Hàng chờ kiểm",                           "Inspection Queue");
        Add("qms.title",           "Hàng chờ kiểm (QMS)",                     "Inspection Queue (QMS)");
        Add("qms.refresh",         "Làm mới",                                 "Refresh");
        Add("qms.loading",         "Đang tải…",                               "Loading…");
        Add("qms.loading.queue",   "Đang tải hàng chờ…",                      "Loading queue…");
        Add("qms.load.failed",     "Không tải được hàng chờ: {0}",            "Failed to load queue: {0}");
        Add("qms.empty",           "Không có lệnh SX nào đang chờ {0}.",       "No WOs waiting for {0}.");

        Add("qms.col.product",     "Sản phẩm",                                "Product");
        Add("qms.col.machine",     "Máy",                                     "Machine");
        Add("qms.col.plandone",    "Kế hoạch/Đã làm",                         "Plan/Done");
        Add("qms.col.waitingsince","Chờ từ",                                  "Waiting since");

        // QMS Dashboard (/qms/dashboard) — KPI overview by stage.
        Add("qms.dash.pagetitle",  "Tổng quan QMS",                           "QMS Overview");
        Add("qms.dash.title",      "Tổng quan chất lượng (QMS)",              "Quality Overview (QMS)");
        Add("qms.dash.ipqc",       "IPQC — Trong chuyền",                     "IPQC — In-process");
        Add("qms.dash.fqc",        "FQC — Kiểm cuối",                         "FQC — Final");
        Add("qms.dash.oqc",        "OQC — Đầu ra",                            "OQC — Outgoing");
        Add("qms.dash.total",      "Tổng đang chờ",                           "Total waiting");
        Add("qms.dash.sub.waiting","lệnh SX đang chờ kiểm",                   "WOs awaiting inspection");
        Add("qms.dash.open",       "Mở hàng chờ →",                           "Open queue →");
        Add("qms.dash.queue",      "Hàng chờ đầy đủ",                         "Full inspection queue");
        Add("qms.dash.queue.desc", "Xem toàn bộ lệnh SX đang chờ các công đoạn kiểm.",
                                                                              "See every WO waiting across QC stages.");

        // IQC module (/qms/iqc) — incoming inspection (API-backed, UI pending).
        Add("qms.iqc.pagetitle",   "IQC — Kiểm đầu vào",                      "IQC — Incoming");
        Add("qms.iqc.title",       "IQC — Kiểm tra chất lượng đầu vào",       "IQC — Incoming Quality");
        Add("qms.iqc.subtitle",    "Kiểm tra nguyên vật liệu đầu vào trước khi nhập kho.",
                                                                              "Inspect incoming raw materials before stock-in.");
        Add("qms.iqc.heading",     "Danh sách IQC — đang hoàn thiện",         "IQC worklist — in progress");
        Add("qms.iqc.desc",        "Dữ liệu IQC đã có ở API (/api/v2/iqc). Giao diện danh sách trên app đang được nối; tạm thời tra cứu qua trang web nội bộ.",
                                                                              "IQC data is available on the API (/api/v2/iqc). The in-app worklist UI is being wired; use the internal web page in the meantime.");

        // iCRA module (/qms/icra) — corrective action / CAPA (new; placeholder).
        Add("qms.icra.pagetitle",  "iCRA — Đối sách / CAPA",                  "iCRA — CAPA");
        Add("qms.icra.title",      "iCRA — Đối sách & hành động khắc phục",   "iCRA — Corrective Action / CAPA");
        Add("qms.icra.subtitle",   "Theo dõi đối sách và hành động khắc phục lỗi chất lượng.",
                                                                              "Track corrective actions and CAPA for quality issues.");
        Add("qms.icra.heading",    "Bảng iCRA — sắp có",                      "iCRA board — coming soon");
        Add("qms.icra.desc",       "Module đối sách/CAPA đang được phát triển: mở phiếu, phân công, theo dõi đóng lỗi. Liên hệ QA để cập nhật lộ trình.",
                                                                              "The corrective-action / CAPA module is under development: raise, assign and close issues. Contact QA for the roadmap.");
    }
}

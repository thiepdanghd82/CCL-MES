namespace CCL.MES.Hybrid.Client.Localization;

// Batch 1 — left sidebar (NavMenu.razor). Section headers + links.
public sealed partial class TranslationCatalog
{
    private void RegisterNav()
    {
        //     key                       vi                          en
        // CCL iX — điều khiển rail (nguyên tắc 5: thu gọn do người dùng chủ động).
        Add("nav.rail.collapse",      "Thu gọn",                   "Collapse");
        Add("nav.rail.expand",        "Mở rộng",                   "Expand");

        Add("nav.home",               "Trang chủ",                 "Home");

        Add("nav.section.npi",        "DỮ LIỆU NPI",               "NPI DATA");
        Add("nav.npi.structure",      "Cấu trúc (BOM)",            "Structure (BOM)");
        Add("nav.npi.routings",       "Công đoạn",                 "Routings");
        Add("nav.npi.rawmaterials",   "Nguyên vật liệu",           "Raw Materials");
        Add("nav.npi.workcenters",    "Trung tâm sản xuất",        "Work Centers");
        Add("nav.npi.spec",           "Spec kỹ thuật",             "Engineer Spec");

        Add("nav.section.production",  "SẢN XUẤT",                  "PRODUCTION");
        Add("nav.workorders",         "Lệnh SX — Quét",            "Work Orders — Scan");
        Add("nav.semiproducts",       "Kho bán thành phẩm",        "Semi-Finished Store");

        Add("nav.section.monitoring",  "GIÁM SÁT",                  "MONITORING");
        Add("nav.machines",           "Bảng điều khiển máy",       "Machine Dashboard");
        Add("nav.shoporders",         "Lịch sử lệnh SX",           "Shop Order History");

        // QC QMS Data group — 5 primary modules (2-line: title + subtitle)
        // mirroring the QA mini-QMS mock (Dashboard/IQC/IPQC/OQC/iCRA) + the
        // compact secondary links that keep the rest of the QC surfaces.
        Add("nav.qms.title",          "QC QMS Data",               "QC QMS Data");

        Add("nav.qms.m.dashboard",    "Tổng quan",                 "Overview");
        Add("nav.qms.m.dashboard.sub","Bảng KPI chất lượng",       "Dashboard");
        Add("nav.qms.m.iqc",          "IQC",                       "IQC");
        Add("nav.qms.m.iqc.sub",      "Kiểm đầu vào",              "Incoming quality");
        Add("nav.qms.m.ipqc",         "IPQC",                      "IPQC");
        Add("nav.qms.m.ipqc.sub",     "Kiểm trong chuyền",         "In-process quality");
        Add("nav.qms.m.oqc",          "OQC",                       "OQC");
        Add("nav.qms.m.oqc.sub",      "Kiểm đầu ra",               "Outgoing quality");
        Add("nav.qms.m.icra",         "iCRA",                      "iCRA");
        Add("nav.qms.m.icra.sub",     "Đối sách · CAPA",           "Corrective action · CAPA");

        Add("nav.qms.queue",          "Hàng chờ kiểm",             "Inspection Queue");
        Add("nav.qms.fqc",            "FQC — Kiểm cuối",           "FQC — Final");
        Add("nav.qms.history",        "Lịch sử QC",                "QC History");
        Add("nav.qms.library",        "Thư viện QC",               "QC Library");
        Add("nav.qms.traceability",   "Dữ liệu truy xuất",         "Traceability data");

        Add("nav.section.settings",    "CÀI ĐẶT",                   "SETTINGS");
        Add("nav.system",             "Hệ thống",                  "System");
    }
}

namespace CCL.MES.Hybrid.Client.Localization;

// Batch 1 — left sidebar (NavMenu.razor). Section headers + links.
public sealed partial class TranslationCatalog
{
    private void RegisterNav()
    {
        //     key                       vi                          en
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

        // CCL-QMS group — mirrors the NPI DATA group. IPQC/FQC/OQC are
        // stage-scoped views of the same Inspection Queue (/qms/{stage}).
        Add("nav.section.qms",         "CCL-QMS",                   "CCL-QMS");
        Add("nav.qms.queue",          "Hàng chờ kiểm",             "Inspection Queue");
        Add("nav.qms.ipqc",           "IPQC — Kiểm trong chuyền",  "IPQC — In-Process");
        Add("nav.qms.fqc",            "FQC — Kiểm cuối",           "FQC — Final");
        Add("nav.qms.oqc",            "OQC — Kiểm xuất hàng",      "OQC — Outgoing");
        Add("nav.qms.history",        "Lịch sử QC",                "QC History");
        Add("nav.qms.library",        "Thư viện QC",               "QC Library");
        Add("nav.qms.traceability",   "Dữ liệu truy xuất",         "Traceability data");

        Add("nav.section.settings",    "CÀI ĐẶT",                   "SETTINGS");
        Add("nav.system",             "Hệ thống",                  "System");
    }
}

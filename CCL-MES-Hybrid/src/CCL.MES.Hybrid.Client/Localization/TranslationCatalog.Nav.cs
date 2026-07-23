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
        Add("nav.inspectionqueue",    "Hàng chờ kiểm",             "Inspection Queue");
        Add("nav.qchistory",          "Lịch sử QC",                "QC History");
        Add("nav.qclibrary",          "Thư viện QC",               "QC Library");

        Add("nav.section.quality",     "KIỂM SOÁT CHẤT LƯỢNG",      "QUALITY CONTROL");
        Add("nav.traceability",       "Dữ liệu truy xuất",         "Traceability data");

        Add("nav.section.settings",    "CÀI ĐẶT",                   "SETTINGS");
        Add("nav.system",             "Hệ thống",                  "System");
    }
}

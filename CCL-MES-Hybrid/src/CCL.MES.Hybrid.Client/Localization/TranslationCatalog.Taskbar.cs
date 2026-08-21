namespace CCL.MES.Hybrid.Client.Localization;

// P2-PR1 — window-manager + taskbar surface. Window TITLE keys mirror
// WindowRegistryKeys.TitleKeys.* (the host binds those constants); taskbar chip
// tooltips + the soft-cap banner live here too. EN + VI parity (gate-i18n).
public sealed partial class TranslationCatalog
{
    private void RegisterTaskbar()
    {
        //     key                                 vi                                     en
        // Window titles (WindowRegistryKeys.TitleKeys.*)
        Add("windows.qc_history.title",          "Lịch sử QC",                          "QC History");
        Add("windows.shop_order_history.title",  "Lịch sử lệnh SX",                     "Shop Order History");
        Add("windows.machine_dashboard.title",   "Bảng điều khiển máy",                 "Machine Dashboard");
        Add("windows.qc_library.title",          "Thư viện QC",                         "QC Library");

        // P2-PR2 window titles (WindowRegistryKeys.TitleKeys.* subset).
        // Home is NOT a window — "/" is the full-page landing, not a floating window.
        Add("windows.npi_structure.title",       "Cấu trúc (BOM)",                      "Structure (BOM)");
        Add("windows.npi_routine.title",         "Định tuyến",                          "Routings");
        Add("windows.npi_rawmaterials.title",    "Vật tư",                              "Raw Materials");
        Add("windows.npi_workcenters.title",     "Trạm sản xuất",                       "Work Centers");
        Add("windows.semi_products.title",       "Kho bán thành phẩm",                  "Semi-Finished Store");
        Add("windows.qms_dashboard.title",       "Tổng quan chất lượng",                "Quality Overview");
        Add("windows.qms_ipqc.title",            "IPQC — Kiểm trong chuyền",            "IPQC — In-process");
        Add("windows.qms_oqc.title",             "OQC — Kiểm đầu ra",                   "OQC — Outgoing");
        Add("windows.qms_icra.title",            "iCRA — Đối sách · CAPA",              "iCRA — Corrective action");

        // P2-PR3 route-param tabs (WindowRegistryKeys.TitleKeys.* subset).
        Add("windows.qms_queue.title",           "Hàng chờ kiểm (QMS)",                 "Inspection Queue (QMS)");
        Add("windows.qms_queue_fqc.title",       "Hàng chờ kiểm · FQC",                 "Inspection Queue · FQC");
        Add("windows.specs.title",               "Đặc tả kỹ thuật",                     "Specifications");
        Add("windows.spec_detail.title",         "Chi tiết đặc tả",                     "Spec Detail");

        // P2 showcard-migration — Traceability list + per-WO detail.
        Add("windows.traceability.title",        "Truy xuất nguồn gốc",                 "Traceability");
        Add("windows.trace_detail.title",        "Truy xuất WO",                        "Trace WO");

        // W5 showcard-migration — IQC Materials inspection window (dynamic title
        // fallback; the caller passes the ReceiptNo / "new ticket" phrase).
        Add("windows.iqc_inspection.title",      "Phiếu thanh tra IQC",                 "IQC Inspection");

        // Empty-workspace background (P2-PR2)
        Add("workspace.empty.title",             "Không gian làm việc",                 "Workspace");
        Add("workspace.empty.hint",              "Mở một thẻ từ thanh bên để bắt đầu",  "Open a tab from the sidebar to begin");

        // Taskbar chip tooltips
        Add("taskbar.restore",                   "Khôi phục",                           "Restore");
        Add("taskbar.close",                     "Đóng",                                "Close");
        Add("taskbar.maximize",                  "Phóng to",                            "Maximize");
        Add("taskbar.empty",                     "Thanh cửa sổ",                        "Window taskbar");

        // Soft-cap banner — {0} = SoftCap (8)
        Add("window.max_reached",                "Đã đạt tối đa {0} cửa sổ",            "Maximum {0} windows reached");
    }
}

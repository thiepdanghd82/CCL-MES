namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Pages/NpiRawMaterials.razor (npiraw.*).
public sealed partial class TranslationCatalog
{
    private void RegisterNpiRaw()
    {
        //     key                              vi                                    en
        Add("npiraw.pagetitle",             "Nguyên vật liệu — NPI",              "Raw Materials — NPI");
        Add("npiraw.title",                 "Nguyên vật liệu",                    "Raw Materials");

        Add("npiraw.search.placeholder",    "Tìm theo mã hàng / nhà cung cấp / nhà máy…", "Search by part / supplier / site…");
        Add("npiraw.search",                "Tìm kiếm",                           "Search");
        Add("npiraw.reload",                "Tải lại",                            "Reload");
        Add("npiraw.import",                "Nhập…",                              "Import…");

        Add("npiraw.loading",               "Đang tải…",                          "Loading…");
        Add("npiraw.error.load",            "Không tải được dữ liệu.",            "Failed to load data.");
        Add("npiraw.retry",                 "Thử lại",                            "Retry");
        Add("npiraw.empty",                 "Không có dữ liệu phù hợp.",          "No matching data.");

        Add("npiraw.pager.prev",            "Trước",                              "Previous");
        Add("npiraw.pager.next",            "Sau",                                "Next");
        Add("npiraw.pager.status",          "Trang {0} / {1} — {2} dòng",         "Page {0} / {1} — {2} rows");

        // Column headers
        Add("npiraw.col.part_no",           "Mã hàng",                            "Part No");
        Add("npiraw.col.part_desc",         "Mô tả",                              "Description");
        Add("npiraw.col.supplier_id",       "Mã nhà cung cấp",                    "Supplier ID");
        Add("npiraw.col.supplier_name",     "Nhà cung cấp",                       "Supplier");
        Add("npiraw.col.price",             "Đơn giá",                            "Price");
        Add("npiraw.col.currency",          "Tiền tệ",                            "Currency");
        Add("npiraw.col.purch_uom",         "ĐVT mua",                            "Purchase UOM");
        Add("npiraw.col.inv_uom",           "ĐVT tồn kho",                        "Inventory UOM");
        Add("npiraw.col.site",              "Nhà máy",                            "Site");
        Add("npiraw.col.status",            "Trạng thái",                         "Status");
        Add("npiraw.col.min_qty",           "SL tối thiểu",                       "Min Qty");
        Add("npiraw.col.leadtime",          "Thời gian giao (ngày)",              "Leadtime (days)");
        Add("npiraw.col.price_incl_tax",    "Giá gồm thuế",                       "Price incl. Tax");
        Add("npiraw.col.price_uom",         "ĐVT giá",                            "Price UOM");
        Add("npiraw.col.site_desc",         "Mô tả nhà máy",                      "Site Description");
        Add("npiraw.col.std_multiple",      "Bội số chuẩn",                       "Std multiple");
        Add("npiraw.col.std_pack",          "Quy cách đóng gói",                  "Std pack");
        Add("npiraw.col.conversion",        "Hệ số quy đổi",                      "Conversion Factor");
        Add("npiraw.col.tax_code",          "Mã thuế",                            "Tax Code");
        Add("npiraw.col.tax_code_desc",     "Mô tả thuế",                         "Tax Description");
        Add("npiraw.col.country_origin",    "Xuất xứ",                            "Country of Origin");
        Add("npiraw.col.acquisition_type",  "Hình thức mua",                      "Acquisition");
        Add("npiraw.col.supplier_part_no",  "Mã hàng NCC",                        "Supplier Part");
        Add("npiraw.col.supplier_part_desc","Mô tả mã hàng NCC",                  "Supplier Part Description");
        Add("npiraw.col.net_weight",        "Khối lượng",                         "Weight");
        Add("npiraw.col.net_weight_uom",    "ĐVT khối lượng",                     "Weight UOM");
        Add("npiraw.col.next_order_date",   "Ngày đặt kế tiếp",                   "Next Order Date");
        Add("npiraw.col.notes",             "Ghi chú",                            "Notes");
        // rawmaterials-bom-xlsx-import — 11 cột "Materials BOM".
        Add("npiraw.col.mother_code",           "Mã mẹ",                          "Mother Code");
        Add("npiraw.col.dimension_quality",     "Kích thước / Quy cách",          "Dimension / Quality");
        Add("npiraw.col.width_mm",              "Khổ (mm)",                       "Width (mm)");
        Add("npiraw.col.part_type",             "Loại hàng",                      "Part Type");
        Add("npiraw.col.planner",               "Người hoạch định",               "Planner");
        Add("npiraw.col.acct_group_desc",       "Nhóm kế toán",                   "Accounting Group");
        Add("npiraw.col.product_family",        "Nhóm sản phẩm",                  "Product Family");
        Add("npiraw.col.product_family_desc",   "Mô tả nhóm sản phẩm",            "Product Family Description");
        Add("npiraw.col.type_designation",      "Ký hiệu loại",                   "Type Designation");
        Add("npiraw.col.thickness",             "Độ dày",                         "Thickness");
        Add("npiraw.col.leadtime_code",         "Mã thời gian giao",              "Lead Time Code");
    }
}

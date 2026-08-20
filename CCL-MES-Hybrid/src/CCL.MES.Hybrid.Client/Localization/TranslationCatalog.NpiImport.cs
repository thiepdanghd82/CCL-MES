namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2E — Shared/NpiImportModal.razor (npiimport.*).
public sealed partial class TranslationCatalog
{
    private void RegisterNpiImport()
    {
        //     key                              vi                                                          en
        Add("npiimport.header",              "Nhập {0}",                                                  "Import {0}");

        Add("npiimport.instructions.before", "Chọn tệp .xlsx hoặc .csv xuất từ IFS. Cột được ghép theo TÊN tiêu đề (không theo vị trí); hàng tiêu đề tự dò trong 10 dòng đầu (ví dụ:",
                                             "Choose an .xlsx or .csv exported from IFS. Columns are matched by header NAME (not position); the header row is auto-detected within the first 10 rows (e.g.");
        Add("npiimport.instructions.after",  ").",                                                        ").");

        // rawmaterials-bom-xlsx-import — gợi ý riêng cho tab Nguyên vật liệu.
        Add("npiimport.instructions.rawmaterials",
                                             "Chọn tệp \"Materials BOM\" (.xlsx) hoặc export IFS (.csv). Ghép cột theo tên tiêu đề; trùng Mã hàng sẽ được cập nhật, không tạo trùng.",
                                             "Choose a \"Materials BOM\" (.xlsx) file or an IFS export (.csv). Columns match by header name; existing Part No rows are updated, not duplicated.");

        Add("npiimport.importing",           "Đang nhập… vui lòng chờ.",                                 "Importing… please wait.");
        Add("npiimport.result",              "Thêm mới {0}, cập nhật {1}, bỏ qua {2} dòng.",             "Inserted {0}, updated {1}, skipped {2} rows.");

        Add("npiimport.err.header_not_found","Không tìm thấy hàng tiêu đề (cần cột \"Part No\" trong 10 dòng đầu).",
                                             "No header row found (a \"Part No\" column is required in the first 10 rows).");
        Add("npiimport.err.xlsx_unreadable", "Không đọc được tệp .xlsx. Kiểm tra tệp có đúng định dạng Excel không.",
                                             "The .xlsx file could not be read. Check that it is a valid Excel file.");
        Add("npiimport.err.no_file",         "Chưa chọn tệp để nhập.",                                   "No file was selected to import.");

        Add("npiimport.kind.routings",       "Công đoạn",                                                 "Routings");
        Add("npiimport.kind.rawmaterials",   "Nguyên vật liệu",                                           "Raw Materials");
        Add("npiimport.kind.structure",      "Cấu trúc (BOM)",                                            "Structure (BOM)");
    }
}

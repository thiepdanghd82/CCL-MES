namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2E — Shared/NpiImportModal.razor (npiimport.*).
public sealed partial class TranslationCatalog
{
    private void RegisterNpiImport()
    {
        //     key                              vi                                                          en
        Add("npiimport.header",              "Nhập {0} (CSV)",                                            "Import {0} (CSV)");

        Add("npiimport.instructions.before", "Chọn một tệp CSV xuất từ IFS. Các dòng sẽ được thêm vào; cột được ghép theo tên tiêu đề (ví dụ: ",
                                             "Choose a CSV exported from IFS. Rows are appended; columns are matched by header name (e.g. ");
        Add("npiimport.instructions.after",  ").",                                                        ").");

        Add("npiimport.importing",           "Đang nhập… vui lòng chờ.",                                 "Importing… please wait.");
        Add("npiimport.result",              "Đã nhập {0} dòng ({1} dòng bị bỏ qua).",                   "Imported {0} rows ({1} skipped).");

        Add("npiimport.kind.routings",       "Công đoạn",                                                 "Routings");
        Add("npiimport.kind.rawmaterials",   "Nguyên vật liệu",                                           "Raw Materials");
        Add("npiimport.kind.structure",      "Cấu trúc (BOM)",                                            "Structure (BOM)");
    }
}

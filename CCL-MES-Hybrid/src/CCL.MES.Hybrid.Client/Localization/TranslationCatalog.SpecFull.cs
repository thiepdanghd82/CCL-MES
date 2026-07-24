namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2E — Shared/SpecShowcardFull.razor (specfull.*).
public sealed partial class TranslationCatalog
{
    private void RegisterSpecFull()
    {
        //     key                                    vi                                        en

        // Empty state.
        Add("specfull.empty",                     "Chưa có dữ liệu showcard.",              "No showcard data yet.");

        // Per-section render guard (Lesson #27) — dev/ops triage diagnostic.
        Add("specfull.section.error",             "Không thể hiển thị mục [{0}]: {1}",       "Could not display section [{0}]: {1}");

        // Doc header (navy strip).
        Add("specfull.doc.refno",                 "SỐ THAM CHIẾU:",                         "REF NO:");
        Add("specfull.doc.spec",                  "Spec",                                   "Spec");

        // Category header (per template).
        Add("specfull.category.silk",             "QUY CÁCH IN LỤA",                        "SILKSCREEN SPECIFICATION");
        Add("specfull.category.silk.sub",         "Quy cách in lụa",                        "Silkscreen specification");
        Add("specfull.category.flexo",            "QUY CÁCH IN FLEXO",                      "FLEXO SPECIFICATION");
        Add("specfull.category.flexo.sub",        "Quy cách in flexo",                      "Flexo specification");
        Add("specfull.category.indigo",           "QUY CÁCH INDIGO (HP)",                   "INDIGO (HP) SPECIFICATION");
        Add("specfull.category.indigo.sub",       "Quy cách in tem HP Indigo",              "HP Indigo seal specification");
        Add("specfull.category.letter",           "QUY CÁCH IN TYPO",                       "LETTERPRESS SPECIFICATION");
        Add("specfull.category.letter.sub",       "Quy cách in typo",                       "Letterpress specification");
        Add("specfull.category.generic",          "QUY CÁCH CÔNG ĐOẠN",                     "PROCESS SPECIFICATION");
        Add("specfull.category.generic.sub",      "Quy cách công đoạn",                     "Process specification");

        // Compliance strip.
        Add("specfull.compliance.label",          "Tiêu chuẩn:",                            "Compliance:");

        // Generic fallback warning (split around <strong>{line}</strong>).
        Add("specfull.generic.warning.pre",       "Mẫu quy cách chuẩn cho công đoạn",       "The standard template for the");
        Add("specfull.generic.warning.post",      "sẽ được bổ sung khi có mẫu thực tế — hiện chỉ hiển thị các mục đã có dữ liệu.",
                                                                                            "line will be added once a real sample is available — showing the sections that already have data.");
        Add("specfull.generic.empty",             "Chưa có dữ liệu quy cách con cho công đoạn {0}.",
                                                                                            "No sub-spec data yet for the {0} line.");

        // Product Information block.
        Add("specfull.product.title",             "Thông tin sản phẩm",                     "Product Information");

        // Shared column headers (main + sub labels).
        Add("specfull.col.customer",              "Khách hàng",                             "Customer");
        Add("specfull.col.customer.sub",          "Khách hàng",                             "Customer");
        Add("specfull.col.ifscode",               "Mã IFS",                                 "IFS code");
        Add("specfull.col.ifscode.sub",           "Mã IFS / Spec",                          "IFS / Spec Code");
        Add("specfull.col.partno",                "Mã chi tiết",                            "Part No");
        Add("specfull.col.partno.sub",            "Mã chi tiết",                            "Part Code");
        Add("specfull.col.partname",              "Tên chi tiết",                           "Part Name");
        Add("specfull.col.partname.sub.component","Tên bộ phận",                            "Component Name");
        Add("specfull.col.partname.sub.product",  "Tên sản phẩm",                           "Product Name");
        Add("specfull.col.productsize",           "Kích thước SP",                          "Product Size");
        Add("specfull.col.productsize.sub",       "Kích thước SP (mm)",                     "Product Size (mm)");
        Add("specfull.col.substrate",             "Vật liệu nền",                           "Substrate");
        Add("specfull.col.substrate.sub",         "Vật liệu",                               "Material");
        Add("specfull.col.material",              "Vật liệu",                               "Material");
        Add("specfull.col.material.sub",          "Vật liệu",                               "Material");
        Add("specfull.col.matsize",               "KT vật liệu",                            "Mat. Size");
        Add("specfull.col.matsize.sub",           "Kích thước vật liệu",                    "Material Size");
        Add("specfull.col.lam",                   "Cán màng",                               "Lam.");
        Add("specfull.col.lam.sub",               "Cán màng",                               "Lamination");
        Add("specfull.col.lamsize",               "KT cán màng",                            "Lam. Size");
        Add("specfull.col.lamcav",                "Số con cán màng",                        "Lam. Cav.");

        // Print Parameters block (silk).
        Add("specfull.printparams.title",         "Thông số in",                            "Print Parameters");
        Add("specfull.printparams.cavity",        "Số con in",                              "Printing cavity");
        Add("specfull.printparams.cavity.sub",    "Số lượng mỗi lần in",                    "Quantity per print");
        Add("specfull.printparams.pitch",         "Bước dài",                               "Length pitch");
        Add("specfull.printparams.pitch.sub",     "Bước (mm)",                              "Pitch (mm)");
        Add("specfull.printparams.productsize",   "Kích thước SP",                          "Product size");
        Add("specfull.printparams.productsize.sub","Kích thước sản phẩm",                   "Product Size");
        Add("specfull.printparams.adhesive",      "Keo dán",                                "Adhesive");
        Add("specfull.printparams.adhesive.sub",  "Keo dán",                                "Adhesive");

        // Silk legend.
        Add("specfull.silk.legend.squeegee",      "Mã dao gạt:",                            "Squeegee codes:");
        Add("specfull.silk.legend.drying",        "Mã sấy:",                                "Drying codes:");
        Add("specfull.silk.legend.heat",          "Sấy nhiệt",                              "Heat");
        Add("specfull.silk.legend.selfdry",       "Tự khô",                                 "Self-dry");

        // Silk print-process table.
        Add("specfull.silk.table.title",          "Quy trình in · Sấy · Thông số bản in — {0} màu",
                                                                                            "Print Process · Drying · Plate Parameter — {0} colors");
        Add("specfull.col.no",                    "STT",                                    "No");
        Add("specfull.col.surf",                  "Mặt",                                    "Surf");
        Add("specfull.col.color",                 "Màu",                                    "Color");
        Add("specfull.col.inkname",               "Tên mực",                                "Ink Name");
        Add("specfull.col.inkcode",               "Mã mực",                                 "Ink Code");
        Add("specfull.col.maker",                 "Hãng SX",                                "Maker");
        Add("specfull.col.retarder",              "Dung môi chậm",                          "Retarder");
        Add("specfull.col.visc",                  "Độ nhớt",                                "Visc");
        Add("specfull.col.speed",                 "Tốc độ",                                 "Speed");
        Add("specfull.col.squee",                 "Dao gạt",                                "Squee.");
        Add("specfull.col.dry",                   "Sấy",                                    "Dry");
        Add("specfull.col.min",                   "phút",                                   "min");
        Add("specfull.col.emul",                  "Nhũ",                                    "Emul.");
        Add("specfull.col.platesize",             "KT bản",                                 "Plate Size");
        Add("specfull.col.mesh",                  "Lưới",                                   "Mesh");
        Add("specfull.col.angle",                 "Góc",                                    "Angle");
        Add("specfull.col.platecode",             "Mã bản",                                 "Plate Code");
        Add("specfull.col.ctrl",                  "Số KS",                                  "Ctrl#");
        Add("specfull.col.remark",                "Ghi chú",                                "Remark");

        // Flexo/Ink print sub-table.
        Add("specfull.flexo.print.title",         "In — {0} công đoạn",                     "Printing — {0} processes");
        Add("specfull.col.process",               "Công đoạn",                              "Process");
        Add("specfull.col.thickness",             "Độ dày",                                 "Thickness");
        Add("specfull.col.size",                  "Kích thước",                             "Size");
        Add("specfull.col.cylinder",              "Trục",                                   "Cylinder");
        Add("specfull.col.pitch",                 "Bước",                                   "Pitch");
        Add("specfull.col.thead",                 "Lực căng đầu",                           "T.Head");
        Add("specfull.col.tend",                  "Lực căng cuối",                          "T.End");
        Add("specfull.col.troll",                 "Lực căng cuộn",                          "T.Roll");
        Add("specfull.col.platecav",              "Số con bản",                             "Plate Cav.");
        Add("specfull.col.tension",               "Lực căng",                               "Tension");

        // Flexo/Ink cutting sub-table.
        Add("specfull.flexo.cut.title",           "Cắt — {0} công đoạn",                    "Cutting — {0} processes");
        Add("specfull.col.lamination",            "Cán màng",                               "Lamination");
        Add("specfull.col.cutterlot",             "Lô dao cắt",                             "Cutter Lot");
        Add("specfull.col.cuttername",            "Tên dao cắt",                            "Cutter Name");
        Add("specfull.col.pcssheet",              "Con/tờ",                                 "Pcs/Sheet");
        Add("specfull.col.cavity",                "Số con",                                 "Cavity");
        Add("specfull.col.packing",               "Đóng gói",                               "Packing");
        Add("specfull.col.papersp",               "Tốc độ giấy",                            "Paper Sp.");
        Add("specfull.col.cutsp",                 "Tốc độ cắt",                             "Cut Sp.");
        Add("specfull.col.cutpres",               "Áp lực cắt",                             "Cut Pres.");
        Add("specfull.col.headt",                 "Lực căng đầu",                           "Head T.");
        Add("specfull.col.rollt",                 "Lực căng cuộn",                          "Roll T.");

        // Flexo/Ink ink sub-table.
        Add("specfull.flexo.ink.title",           "Mực — {0} loại mực",                     "Ink — {0} inks");
        Add("specfull.col.description",           "Mô tả",                                  "Description");
        Add("specfull.col.brand",                 "Thương hiệu",                            "Brand");
        Add("specfull.col.anilox",                "Trục Anilox",                            "Anilox");
        Add("specfull.col.pressure",              "Áp lực",                                 "Pressure");
        Add("specfull.col.uvpower",               "Công suất UV",                           "UV Power");
        Add("specfull.col.irpower",               "Công suất IR",                           "IR Power");

        // Remarks block.
        Add("specfull.remarks.title",             "Ghi chú",                                "Remarks");
        Add("specfull.remarks.print",             "Ghi chú in",                             "Print remarks");
        Add("specfull.remarks.cut",               "Ghi chú cắt",                            "Cut remarks");

        // Revision History block.
        Add("specfull.revision.title",            "Lịch sử phiên bản",                      "Revision History");
        Add("specfull.col.rev",                   "Phiên bản",                              "Rev");
        Add("specfull.col.contents",              "Nội dung",                               "Contents");
        Add("specfull.col.date",                  "Ngày",                                   "Date");
        Add("specfull.col.by",                    "Người",                                  "By");
        Add("specfull.revision.empty",            "— Chưa có lịch sử —",                    "— No history —");

        // Approval Signatures block.
        Add("specfull.signatures.title",          "Chữ ký phê duyệt",                       "Approval Signatures");
        Add("specfull.sign.issued",               "Người lập (R&D)",                        "Issued by R&D");
        Add("specfull.sign.confirmed",            "Người xác nhận (R&D)",                   "Confirmed by R&D");
        Add("specfull.sign.released",             "Người ban hành",                         "Released by");
        Add("specfull.sign.updated",              "Người cập nhật",                         "Updated by");
        Add("specfull.sign.date",                 "Ngày",                                   "Date");

        // Change Log block.
        Add("specfull.changelog.title",           "Nhật ký thay đổi",                       "Change Log");
        Add("specfull.changelog.empty",           "— Chưa có mục nhật ký nào —",            "— No log entries yet —");
        Add("specfull.col.time",                  "Thời gian",                              "Time");
        Add("specfull.col.action",                "Hành động",                              "Action");

        // Change-log action labels.
        Add("specfull.action.create",             "Tạo Spec",                               "Create Spec");
        Add("specfull.action.approve",            "Phê duyệt",                              "Approve");
        Add("specfull.action.release",            "Ban hành",                               "Release");
        Add("specfull.action.revise",             "Tạo bản sửa đổi",                        "Create Revise");
        Add("specfull.action.supersede",          "Đánh dấu thay thế",                      "Mark Superseded");
        Add("specfull.action.trash",              "Chuyển vào thùng rác",                   "Move to Trash");
        Add("specfull.action.restore",            "Khôi phục",                              "Restore");
        Add("specfull.action.import",             "Nhập từ xlsx",                           "Import from xlsx");
        Add("specfull.action.update",             "Chỉnh sửa",                              "Edit");
    }
}

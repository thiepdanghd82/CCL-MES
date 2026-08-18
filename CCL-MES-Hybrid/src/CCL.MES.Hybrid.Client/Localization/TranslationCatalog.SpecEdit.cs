namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2E — Shared/SpecShowcardEdit.razor (specedit.*).
public sealed partial class TranslationCatalog
{
    private void RegisterSpecEdit()
    {
        //     key                              vi                                              en
        Add("specedit.editing",              "Đang sửa {0} · Rev {1}",                        "Editing {0} · Rev {1}");
        Add("specedit.editableHint",         "— các ô bên dưới có thể chỉnh sửa",             "— cells below are editable");
        Add("specedit.cancel",               "Hủy",                                           "Cancel");
        Add("specedit.saving",               "Đang lưu…",                                     "Saving…");
        Add("specedit.save",                 "Lưu thay đổi",                               "Save changes");

        Add("specedit.error.connect",        "Không kết nối được máy chủ: {0}",               "Could not connect to the server: {0}");
        Add("specedit.removeRow",            "Xóa dòng",                                      "Remove row");

        // ── Sections ──────────────────────────────────────────────────────
        Add("specedit.section.header",       "Tiêu đề",                                       "Header");
        Add("specedit.section.product",      "Thông tin sản phẩm",                            "Product Information");
        Add("specedit.section.printParams",  "Thông số in",                                   "Print Parameters");
        Add("specedit.section.remarks",      "Ghi chú",                                       "Remarks");

        // ── Header fields ─────────────────────────────────────────────────
        Add("specedit.field.title",          "Tiêu đề",                                       "Title");
        Add("specedit.field.refNo",          "Số tham chiếu",                                 "REF NO");
        Add("specedit.field.inspectionLevel","Spec / Mức kiểm tra",                           "Spec / Inspection level");
        Add("specedit.field.processCode",    "Mã công đoạn",                                  "Process code");

        // ── Product fields ────────────────────────────────────────────────
        Add("specedit.field.ifsCode",        "Mã IFS",                                        "IFS code");
        Add("specedit.field.customer",       "Khách hàng",                                    "Customer");
        Add("specedit.field.partNo",         "Mã chi tiết",                                   "Part No");
        Add("specedit.field.partName",       "Tên chi tiết",                                  "Part Name");
        Add("specedit.field.substrate",      "Vật liệu (đế in)",                              "Material (substrate)");
        Add("specedit.field.adhesive",       "Keo dán",                                       "Adhesive");
        Add("specedit.field.thickness",      "Độ dày (µm)",                                   "Thickness (µm)");

        // ── Print parameter fields ────────────────────────────────────────
        Add("specedit.field.printingCavity", "Số khuôn in",                                   "Printing cavity");
        Add("specedit.field.lengthPitch",    "Bước chiều dài (mm)",                           "Length pitch (mm)");
        Add("specedit.field.productSizeW",   "Khổ sản phẩm R (mm)",                           "Product size W (mm)");
        Add("specedit.field.productSizeH",   "Khổ sản phẩm C (mm)",                           "Product size H (mm)");

        // ── Ink table ─────────────────────────────────────────────────────
        Add("specedit.ink.info",             "Thông tin mực — {0} mực",                       "Ink Information — {0} inks");
        Add("specedit.ink.addRow",           "+ Thêm dòng mực",                               "+ Add ink row");
        Add("specedit.ink.empty",            "Chưa có mực. Bấm “+ Thêm dòng mực”.",           "No inks. Click “+ Add ink row”.");

        // ── Colour / plate table ──────────────────────────────────────────
        Add("specedit.color.info",           "Công đoạn in · Thông số bản in — {0} màu",      "Print Process · Plate Parameter — {0} colors");
        Add("specedit.color.addRow",         "+ Thêm dòng màu",                               "+ Add color row");
        Add("specedit.color.empty",          "Chưa có màu. Bấm “+ Thêm dòng màu”.",           "No colors. Click “+ Add color row”.");

        // ── Table columns (short header labels) ───────────────────────────
        Add("specedit.col.no",               "STT",                                           "No");
        Add("specedit.col.surf",             "Mặt",                                           "Surf");
        Add("specedit.col.color",            "Màu",                                           "Color");
        Add("specedit.col.inkName",          "Tên mực",                                       "Ink Name");
        Add("specedit.col.inkCode",          "Mã mực",                                        "Ink Code");
        Add("specedit.col.inkDescription",   "Mô tả mực",                                     "Ink Description");
        Add("specedit.col.brand",            "Hãng",                                          "Brand");
        Add("specedit.col.maker",            "NSX",                                           "Maker");
        Add("specedit.col.retarder",         "Chất làm chậm",                                 "Retarder");
        Add("specedit.col.visc",             "Độ nhớt",                                       "Visc");
        Add("specedit.col.speed",            "Tốc độ",                                         "Speed");
        Add("specedit.col.squeegee",         "Dao gạt",                                        "Squee.");
        Add("specedit.col.dry",              "Sấy",                                            "Dry");
        Add("specedit.col.tempC",            "°C",                                             "°C");
        Add("specedit.col.min",              "phút",                                           "min");
        Add("specedit.col.uv",               "UV",                                             "UV");
        Add("specedit.col.emul",             "Nhũ",                                            "Emul.");
        Add("specedit.col.plateSize",        "Khổ bản",                                        "Plate Size");
        Add("specedit.col.mesh",             "Lưới",                                           "Mesh");
        Add("specedit.col.angle",            "Góc",                                            "Angle");
        Add("specedit.col.plateCode",        "Mã bản",                                         "Plate Code");
        Add("specedit.col.ctrlNo",           "Số KS",                                          "Ctrl#");
        Add("specedit.col.remark",           "Ghi chú",                                        "Remark");
        Add("specedit.col.anilox",           "Anilox",                                         "Anilox");
        Add("specedit.col.pressure",         "Lực ép",                                         "Press.");
        Add("specedit.col.uvW",              "UV W",                                           "UV W");
        Add("specedit.col.irW",              "IR W",                                           "IR W");
    }
}

namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2E — Shared/SpecContextMenu.razor (specmenu.*).
public sealed partial class TranslationCatalog
{
    private void RegisterSpecMenu()
    {
        //     key                    vi                            en
        Add("specmenu.open",       "Mở chi tiết",                "Open details");
        Add("specmenu.info",       "Xem thông tin",              "View info");
        Add("specmenu.edit",       "Sửa (Bản nháp)",             "Edit (Draft)");
        Add("specmenu.copy",       "Sao chép",                   "Copy");
        Add("specmenu.supersede",  "Đánh dấu thay thế",          "Mark Superseded");
        Add("specmenu.restore",    "Khôi phục từ Thùng rác",     "Restore from Trash");
        Add("specmenu.trash",      "Chuyển vào Thùng rác",       "Move to Trash");
    }
}

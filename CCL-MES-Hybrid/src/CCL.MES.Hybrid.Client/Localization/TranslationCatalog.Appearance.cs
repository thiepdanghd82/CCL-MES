namespace CCL.MES.Hybrid.Client.Localization;

// Batch 1 — Appearance / language picker page (SettingsAppearance.razor).
// This is the page that flips the language, so it MUST be localized or the
// operator flips to VI and the picker page itself stays English.
public sealed partial class TranslationCatalog
{
    private void RegisterAppearance()
    {
        //     key                          vi                                              en
        Add("appearance.pagetitle",      "Giao diện — CCL MES",                          "Appearance — CCL MES");
        Add("appearance.title",          "Giao diện",                                    "Appearance");
        Add("appearance.language.help",  "Chọn ngôn ngữ giao diện. Lựa chọn được lưu cho lần đăng nhập sau.",
                                          "Choose the interface language. The setting will be saved for your next sign-in.");
        Add("appearance.aria.language",  "Ngôn ngữ hiển thị",                            "Display language");
        Add("appearance.lang.vi.hint",   "Đầy đủ — phủ toàn bộ giao diện hiện tại.",     "Complete — covers the current interface.");
        Add("appearance.lang.en.hint",   "Bản dịch giao diện — áp dụng ngay khi chọn.",  "Interface translation — applies instantly.");

        // CCL iX — công tắc mật độ. Đây là thiết lập có ảnh hưởng THẬT tới
        // người đứng máy (vùng chạm 44px khi đeo găng), nên đặt TRƯỚC nhóm
        // Chủ đề (đang khoá).
        Add("appearance.density.title",   "Mật độ hiển thị",                              "Display density");
        Add("appearance.density.help",    "Chọn theo nơi dùng máy. Chế độ Xưởng phóng to vùng chạm lên 44px và cỡ chữ lên 16px cho người đeo găng, nhìn từ xa hơn.",
                                           "Pick the one that matches where the device is used. Shop floor enlarges touch targets to 44px and text to 16px for gloved hands at a greater viewing distance.");
        Add("appearance.aria.density",    "Mật độ hiển thị",                              "Display density");
        Add("appearance.density.office",  "Văn phòng",                                    "Office");
        Add("appearance.density.office.sub", "Dày — nhiều dòng trên một màn. Cho kỹ sư và QA ngồi bàn.",
                                           "Dense — more rows per screen. For engineers and QA at a desk.");
        Add("appearance.density.shopfloor", "Xưởng",                                      "Shop floor");
        Add("appearance.density.shopfloor.sub", "Thoáng — vùng chạm 44px, chữ 16px. Cho người đứng máy, đeo găng.",
                                           "Roomy — 44px touch targets, 16px text. For operators at the machine, wearing gloves.");

        Add("appearance.theme.title",    "Chủ đề (sắp có — P10.6g)",                     "Theme (coming soon — P10.6g)");
        Add("appearance.theme.help",     "Chế độ Sáng / Tối / Theo hệ thống — sẽ có ở P10.6g. Hiện app dùng bảng màu (Sáng) cố định tối ưu cho màn hình xưởng.",
                                          "Light / dark / system mode — arriving in P10.6g. For now the app uses a fixed (light) color palette optimized for shop-floor screens.");
        Add("appearance.theme.light",    "Sáng",                                         "Light");
        Add("appearance.theme.light.sub","Mặc định hiện tại.",                           "Current default.");
        Add("appearance.theme.dark",     "Tối",                                          "Dark");
        Add("appearance.theme.dark.sub", "Chưa khả dụng.",                               "Not yet available.");
        Add("appearance.theme.system",   "Theo hệ thống",                                "System");
        Add("appearance.theme.system.sub","Chưa khả dụng.",                              "Not yet available.");
    }
}

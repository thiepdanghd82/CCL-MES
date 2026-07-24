namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2E — Shared/RecentScansWidget.razor (recentscans.*).
public sealed partial class TranslationCatalog
{
    private void RegisterRecentScans()
    {
        //     key                            vi                                     en
        Add("recentscans.title",           "QUÉT GẦN ĐÂY",                        "RECENT SCANS");
        Add("recentscans.clear",           "Xoá",                                 "Clear");
        Add("recentscans.clear.tooltip",   "Xoá lịch sử quét trên máy này",       "Clear local scan history");
        Add("recentscans.clear.confirm",   "Xoá toàn bộ lịch sử quét trên máy này?", "Clear all local scan history?");
        Add("recentscans.notfound",        "không tìm thấy",                      "not found");
        Add("recentscans.time.justnow",    "vừa xong",                            "just now");
        Add("recentscans.time.min",        "{0} phút trước",                      "{0} min ago");
        Add("recentscans.time.hour",       "{0} giờ trước",                       "{0} h ago");
        Add("recentscans.time.day",        "{0} ngày trước",                      "{0} d ago");
    }
}

namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2E — Pages/Home.razor (home.*).
public sealed partial class TranslationCatalog
{
    private void RegisterHome()
    {
        //     key                              vi                                             en
        Add("home.pagetitle",                 "Trang chủ — CCL MES",                          "Home — CCL MES");
        Add("home.company",                   "CCL Design Việt Nam",                          "CCL Design Vietnam");
        Add("home.clock.aria",                "Đồng hồ hệ thống",                             "System clock");

        Add("home.operator.fallback",         "thao tác viên",                                "operator");

        Add("home.greeting.hello",            "Xin chào",                                     "Hello");
        Add("home.greeting.morning",          "Chào buổi sáng",                               "Good morning");
        Add("home.greeting.afternoon",        "Chào buổi chiều",                              "Good afternoon");
        Add("home.greeting.evening",          "Chào buổi tối",                                "Good evening");

        // KPI cards
        Add("home.kpi.specs",                 "Spec trong thư viện",                          "Specs in Library");
        Add("home.kpi.specs.sub",             "tổng số bản revision đã lưu",                  "total stored revisions");
        Add("home.kpi.pending",               "Chờ phê duyệt",                                "Pending Approvals");
        Add("home.kpi.pending.sub.clear",     "— đã xử lý hết",                               "— all clear");
        Add("home.kpi.pending.sub.review",    "đang chờ duyệt",                               "awaiting review");
        Add("home.kpi.drafts",                "Bản nháp",                                     "Drafts");
        Add("home.kpi.drafts.sub.none",       "— không có bản nháp",                          "— no drafts");
        Add("home.kpi.drafts.sub.progress",   "đang thực hiện",                               "in progress");
        Add("home.kpi.today",                 "Hoạt động hôm nay",                            "Today's Activity");
        Add("home.kpi.today.sub.quiet",       "— ngày yên ắng",                               "— quiet day");
        Add("home.kpi.today.sub.touched",     "lệnh SX có thao tác hôm nay",                  "WOs touched today");

        // Today's Focus panel
        Add("home.focus.title",               "Trọng tâm hôm nay",                            "Today's Focus");
        Add("home.focus.viewall",             "Xem tất cả →",                                 "View all →");
        Add("home.focus.empty.npi",           "Thư viện đang trống. Mở NPI để nạp mẫu hoặc nhập tệp xlsx", "Library is empty. Open NPI to load samples or import xlsx");
        Add("home.focus.empty.none",          "Không có việc cần xử lý hôm nay",            "Nothing needs attention today");

        // Modules grid
        Add("home.modules.title",             "Phân hệ",                                      "Modules");
        Add("home.module.spec.name",          "Spec kỹ thuật",                                "Engineer Spec");
        Add("home.module.spec.desc",          "Thư viện spec NPI",                            "NPI spec library");
        Add("home.module.npi.name",           "Dữ liệu NPI",                                  "NPI Data");
        Add("home.module.npi.desc",           "BOM / Công đoạn / NVL / Trung tâm SX",         "BOM / Routing / Materials / WC");
        Add("home.module.production.name",    "Sản xuất",                                     "Production");
        Add("home.module.production.desc",    "Quét lệnh SX · Quy trình MES 5 giai đoạn",     "Scan WO · 5-stage MES flow");
        Add("home.module.machines.name",      "Bảng điều khiển máy",                          "Machine Dashboard");
        Add("home.module.machines.desc",      "Giám sát 43 trung tâm sản xuất",               "Monitor 43 work centers");
        Add("home.module.settings.name",      "Cài đặt",                                      "Settings");
        Add("home.module.settings.desc",      "Hồ sơ / tài khoản / nhật ký kiểm toán",        "Profile / accounts / audit log");

        // Quick Actions
        Add("home.qa.title",                  "Thao tác nhanh",                               "Quick Actions");
        Add("home.qa.newspec",               "Spec mới",                                     "New Spec");
        Add("home.qa.import",                 "Nhập xlsx",                                     "Import xlsx");
        Add("home.qa.machines",               "Máy móc",                                      "Machines");
        Add("home.qa.qcqueue",                "Hàng chờ QC",                                  "QC Queue");
        Add("home.qa.history",                "Lịch sử",                                      "History");
        Add("home.qa.accounts",               "Tài khoản",                                    "Accounts");
        Add("home.qa.help",                   "Trợ giúp",                                     "Help");
    }
}

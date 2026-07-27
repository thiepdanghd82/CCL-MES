namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2D — Pages/SettingsConnection.razor (connection.*).
public sealed partial class TranslationCatalog
{
    private void RegisterConnection()
    {
        //     key                                    vi                                                                          en
        Add("connection.pagetitle",                "Kết nối — CCL MES",                                                         "Connection — CCL MES");
        Add("connection.title",                    "Kết nối",                                                                   "Connection");

        Add("connection.server.title",             "Máy chủ",                                                                   "Server");
        Add("connection.server.apiaddress",        "Địa chỉ API",                                                               "API address");
        Add("connection.server.status",            "Trạng thái kết nối",                                                        "Connection status");
        Add("connection.server.connected",         "Đã kết nối — sẵn sàng đăng nhập và đọc dữ liệu.",                           "Connected — ready to sign in and read data.");
        Add("connection.server.disconnected",      "Mất kết nối — giao diện sẽ chờ mạng để tải dữ liệu.",                       "Disconnected — the UI will wait for the network to load data.");

        Add("connection.station.title",            "Trạm",                                                                      "Station");
        Add("connection.station.mode",             "Chế độ trạm",                                                               "Station mode");
        Add("connection.station.mode.kiosk",       "Kiosk — khoá vào màn hình Lệnh sản xuất.",                                  "Kiosk — locked to the Work Orders screen.");
        Add("connection.station.mode.interactive", "Tương tác — đầy đủ menu cho vận hành viên và quản trị.",                    "Interactive — full menu for operator + admin.");
        Add("connection.station.deviceid",         "Mã thiết bị",                                                               "Device ID");
        Add("connection.station.hardware",         "Tính năng phần cứng",                                                       "Hardware features");
        Add("connection.station.hardware.on",      "Bật (P10.3 W1)",                                                            "On (P10.3 W1)");
        Add("connection.station.hardware.off",     "Tắt",                                                                       "Off");

        Add("connection.action.changemode",        "Đổi chế độ trạm",                                                           "Change station mode");
        Add("connection.action.configuredevices",  "Cấu hình thiết bị",                                                         "Configure devices");
    }
}

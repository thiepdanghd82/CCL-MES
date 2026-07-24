namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Pages/Hardware.razor (hardware.*).
public sealed partial class TranslationCatalog
{
    private void RegisterHardware()
    {
        //     key                          vi                                                    en
        Add("hardware.page.title",        "Phần cứng — CCL MES",                                 "Hardware — CCL MES");
        Add("hardware.heading",           "Cấu hình thiết bị",                                   "Device Configuration");

        Add("hardware.gate.title",        "Các tính năng phần cứng đang tắt.",                   "Hardware features are turned off.");
        Add("hardware.gate.body.pre",     "P10.3 (Phần cứng native) đang ở pha W1 — giao diện + stub đã sẵn sàng, nhưng giao diện thiết bị thật chỉ bật ở các pha sau (W2 = camera Mac Catalyst, W3 = camera Windows + USB-HID, W4 = nối vào WO Accept + /mode kiosk). Bật bằng cách đặt",
                                          "P10.3 (Hardware native) is at phase W1 — interface + stub are ready, but the real device interface turns on in later phases (W2 = camera Mac Catalyst, W3 = camera Windows + USB-HID, W4 = wire into WO Accept + /mode kiosk). Turn it on by setting");
        Add("hardware.gate.body.in",      "trong",                                               "in");
        Add("hardware.gate.body.post",    "hoặc qua biến môi trường ghi đè.",                    "or an env override.");
        Add("hardware.gate.deviceid",     "Mã thiết bị của trạm này",                            "Device id of this station");

        Add("hardware.station",           "Trạm",                                                "Station");

        Add("hardware.section.scanner",   "Máy quét",                                            "Scanner");
        Add("hardware.section.printer",   "Máy in nhãn",                                         "Label Printer");
        Add("hardware.section.scale",     "Cân",                                                 "Scale");

        Add("hardware.printer.title",     "Máy in nhãn (ZPL)",                                   "Label Printer (ZPL)");
        Add("hardware.printer.stub",      "(Stub — khả dụng khi quy trình in nhãn được duyệt)", "(Stub — available once the label printing workflow is approved)");
        Add("hardware.scale.title",       "Cân điện tử",                                         "Electronic Scale");
        Add("hardware.scale.stub",        "(Stub — khả dụng khi quy trình cân được duyệt)",     "(Stub — available once the weighing workflow is approved)");
    }
}

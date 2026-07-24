namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2E — Shared/SpecDrawingShowcards.razor (specdrawcard.*).
public sealed partial class TranslationCatalog
{
    private void RegisterSpecDrawCard()
    {
        //     key                                  vi                                              en
        Add("specdrawcard.group.customer",       "Bản vẽ khách hàng",                             "Customer drawing");
        Add("specdrawcard.group.print",          "Bố cục in",                                     "Print layout");
        Add("specdrawcard.group.cut",            "Bố cục cắt",                                     "Cut layout");

        Add("specdrawcard.thumb.open",           "Mở — {0}",                                      "Open — {0}");
        Add("specdrawcard.pdf.clickToView",      "Bấm để xem",                                    "Click to view");

        Add("specdrawcard.viewer.controls",      "Điều khiển trình xem",                          "Viewer controls");
        Add("specdrawcard.viewer.zoomOut",       "Thu nhỏ",                                       "Zoom out");
        Add("specdrawcard.viewer.zoomLevel",     "Mức phóng to",                                  "Zoom level");
        Add("specdrawcard.viewer.zoomIn",        "Phóng to",                                      "Zoom in");
        Add("specdrawcard.viewer.rotate",        "Xoay 90°",                                      "Rotate 90°");
        Add("specdrawcard.viewer.fitReset",      "Vừa khung / đặt lại (100%)",                    "Fit / reset (100%)");
        Add("specdrawcard.viewer.fullscreen",    "Toàn màn hình",                                 "Fullscreen");
        Add("specdrawcard.viewer.exitFullscreen","Thoát toàn màn hình",                           "Exit fullscreen");
        Add("specdrawcard.viewer.openNewWindow", "Mở trong cửa sổ mới",                           "Open in a new window");
        Add("specdrawcard.viewer.noPreview",     "Tệp này không xem trước trực tiếp được — dùng “↓ Lưu” ở thẻ Bản vẽ.", "No inline preview for this file — use “↓ Save” on the Drawings tab.");
        Add("specdrawcard.viewer.rendering",     "Đang hiển thị…",                                "Rendering…");
    }
}

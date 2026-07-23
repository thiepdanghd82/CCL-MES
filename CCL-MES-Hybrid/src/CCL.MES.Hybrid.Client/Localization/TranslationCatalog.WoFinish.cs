namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2C — WoFinishConfirm.razor (wofinish.*).
public sealed partial class TranslationCatalog
{
    private void RegisterWoFinish()
    {
        //     key                     vi                                              en
        Add("wofinish.header",       "Kết thúc WO {0}?",                              "Finish WO {0}?");
        Add("wofinish.about.pre",    "Bạn sắp chuyển WO sang trạng thái",             "You are about to move the WO to");
        Add("wofinish.about.post",   ". Sản lượng đã ghi nhận:",                      ". Recorded output:");
        Add("wofinish.total.done",   "Tổng đạt",                                      "Total Done");
        Add("wofinish.total.ng",     "Tổng NG",                                       "Total NG");
        Add("wofinish.target",       "Mục tiêu",                                      "Target");
        Add("wofinish.warn",         "Sau khi kết thúc, không thể thêm sản lượng cho WO này nữa.",
                                     "After finishing, no further output can be added to this WO.");
        Add("wofinish.cancel",       "Hủy",                                           "Cancel");
        Add("wofinish.confirm",      "Kết thúc WO",                                   "Finish WO");
    }
}

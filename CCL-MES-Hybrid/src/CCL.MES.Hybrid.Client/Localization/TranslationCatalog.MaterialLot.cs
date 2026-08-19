namespace CCL.MES.Hybrid.Client.Localization;

// A1 — mạch lô nguyên vật liệu (matlot.*). Đủ VI + EN trong cùng commit.
public sealed partial class TranslationCatalog
{
    private void RegisterMaterialLot()
    {
        //     key                             vi                                                        en

        // Nhãn màn hình truy xuất
        Add("matlot.title",                  "Truy xuất lô nguyên vật liệu",                           "Material Lot Genealogy");
        Add("matlot.subtitle",               "Từng lần quét lô lên đơn hàng · nối tới phiếu IQC",       "Every lot scan on this order, linked to its IQC report");
        Add("matlot.col.bom",                "Dòng BOM",                                               "BOM line");
        Add("matlot.col.material",           "Mã vật tư",                                              "Material");
        Add("matlot.col.lot",                "Mã lô",                                                  "Lot no");
        Add("matlot.col.status",             "Trạng thái lô",                                          "Lot status");
        Add("matlot.col.supplier",           "Nhà cung cấp",                                           "Supplier");
        Add("matlot.col.expiry",             "Hạn dùng",                                               "Expiry");
        Add("matlot.col.qty",                "Số lượng dùng",                                          "Qty used");
        Add("matlot.col.qty.total",          "Tổng đã dùng theo lô",                                   "Total used for lot");
        Add("matlot.col.scanned.by",         "Người quét",                                             "Scanned by");
        Add("matlot.col.scanned.at",         "Lúc",                                                    "At");
        Add("matlot.col.iqc",                "Phiếu IQC",                                              "IQC report");
        Add("matlot.col.iqc.fail",           "Hạng mục không đạt",                                     "Failed items");
        Add("matlot.empty",                  "Chưa có lần quét lô nào cho đơn hàng này.",              "No lot scans recorded for this order yet.");

        // Trạng thái lô
        Add("matlot.status.quarantine",      "Cách ly",                                                "Quarantine");
        Add("matlot.status.released",        "Đã duyệt",                                               "Released");
        Add("matlot.status.rejected",        "Từ chối",                                                "Rejected");
        Add("matlot.status.consumed",        "Đã dùng hết",                                            "Consumed");
        Add("matlot.status.expired",         "Hết hạn",                                                "Expired");

        // Thao tác
        Add("matlot.action.scan",            "Quét lô",                                                "Scan lot");
        Add("matlot.action.reverse",         "Đảo lần quét",                                           "Reverse scan");
        Add("matlot.action.reverse.reason",  "Lý do đảo",                                              "Reversal reason");
        Add("matlot.action.extend",          "Gia hạn lô",                                             "Extend lot");
        Add("matlot.action.set.status",      "Đổi trạng thái lô",                                      "Change lot status");
        Add("matlot.reversed.badge",         "Đã đảo",                                                 "Reversed");

        // Điểm chặn khi quét — mỗi mã lỗi một chuỗi, nói rõ NÊN LÀM GÌ TIẾP
        Add("matlot.err.not_found",          "Không tìm thấy mã lô này — kiểm tra nhãn hoặc báo kho nhập lô vào hệ thống.",
                                             "No such lot — check the label, or ask the store to register it.");
        Add("matlot.err.not_released",       "Lô chưa được QC duyệt (đang cách ly) — chưa được nạp lên máy.",
                                             "This lot has not been released by QC yet — do not load it.");
        Add("matlot.err.rejected",           "Lô đã bị IQC từ chối — không được dùng.",
                                             "This lot was rejected by IQC — it must not be used.");
        Add("matlot.err.expired",            "Lô đã quá hạn — cần QC kiểm lại và gia hạn trước khi dùng.",
                                             "This lot is past its expiry — QC must re-test and extend it first.");
        Add("matlot.err.part_mismatch",      "Lô này thuộc vật tư khác — có thể đang cầm nhầm cuộn.",
                                             "This lot belongs to a different material — you may have the wrong roll.");
        Add("matlot.err.depleted",           "Lô đã hết số lượng — lấy lô khác.",
                                             "This lot has no quantity left — take another lot.");
        Add("matlot.err.invalid_request",    "Dữ liệu quét không hợp lệ — kiểm tra mã lô và số lượng.",
                                             "Invalid scan — check the lot code and the quantity.");
        Add("matlot.err.conflict",           "Người khác vừa đổi lô này — quét lại để thấy số lượng hiện tại.",
                                             "Someone else changed this lot first — re-scan to see the current quantity.");
        Add("matlot.err.forbidden",          "Vai của bạn không được thực hiện thao tác này.",
                                             "Your role is not allowed to perform this action.");
        Add("matlot.err.invalid_status",     "Trạng thái lô không hợp lệ.",                            "Invalid lot status.");
        Add("matlot.err.same_signer",        "Người duyệt gia hạn phải khác người kiểm lại.",
                                             "The extension approver must differ from the re-tester.");
        Add("matlot.err.not_expired",        "Chỉ gia hạn được lô đã quá hạn.",                        "Only an expired lot can be extended.");
        Add("matlot.err.not_retested",       "QC phải ghi nhận kiểm lại trước khi gia hạn.",           "QC must record a re-test before extending.");
        Add("matlot.err.already_reversed",   "Lần quét này đã được đảo rồi.",                          "That scan was already reversed.");
        Add("matlot.err.duplicate",          "Mã lô này đã tồn tại cho vật tư đó.",                    "This lot already exists for that material.");

        // Grace period — phải nói rõ vì sao vẫn cho qua, để operator không tưởng là bình thường
        Add("matlot.warn.grace",             "Đã ghi nhận, nhưng lô này chưa hợp lệ — hệ đang trong giai đoạn chạy thử, sau này sẽ bị chặn.",
                                             "Recorded, but this lot is not valid — the system is in grace period and will block this later.");
        Add("matlot.warn.duplicate.scan",    "Mỗi lần quét là một dòng riêng trong hồ sơ — quét hai lần sẽ thấy hai dòng.",
                                             "Every scan is its own record line — scanning twice shows two lines.");
    }
}

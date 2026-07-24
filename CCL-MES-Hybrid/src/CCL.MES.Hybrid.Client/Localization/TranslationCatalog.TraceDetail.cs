namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2C — TraceabilityDetailDialog.razor (trace.*).
public sealed partial class TranslationCatalog
{
    private void RegisterTraceDetail()
    {
        //     key                              vi                                                      en
        Add("trace.aria.window",             "Truy xuất {0}",                                         "Traceability {0}");
        Add("trace.tab.product",             "Dữ liệu sản phẩm",                                     "Product data");
        Add("trace.frozen.tooltip",          "Đã chốt",                                              "Frozen");
        Add("trace.loading",                 "Đang tải…",                                            "Loading…");
        Add("trace.empty.notfrozen",         "Chưa chốt dữ liệu {0} — dữ liệu chưa được đóng băng.", "{0} data not frozen yet.");
        Add("trace.meta.frozen",             "Phiên bản {0} · chốt {1} bởi {2}",                     "Version {0} · frozen {1} by {2}");
        Add("trace.meta.variant",            "· biến thể {0}",                                       "· variant {0}");
        Add("trace.items.none",              "Không có hạng mục kiểm nào được ghi nhận cho công đoạn này.", "No inspected items recorded for this phase.");
        Add("trace.prod.materials.section",  "1. Vật tư đã xác nhận",                                "1. Materials confirmed");
        Add("trace.prod.tools.section",      "2. Công cụ đã xác nhận",                               "2. Tools confirmed");
        Add("trace.tools.none",              "Không có công cụ nào được ghi nhận cho lệnh sản xuất này.", "No tools recorded for this Work Order.");

        // Table column headers
        Add("trace.col.no",                  "STT",                                                  "No.");
        Add("trace.col.partno",              "Mã linh kiện",                                         "Part No");
        Add("trace.col.description",         "Mô tả",                                                "Description");
        Add("trace.col.qpa",                 "QPA (m²)",                                             "QPA (m²)");
        Add("trace.col.qtyrequired",         "SL yêu cầu",                                           "Qty. Required");
        Add("trace.col.uom",                 "ĐVT",                                                  "UoM");
        Add("trace.col.partscan",            "Quét linh kiện",                                       "Part Scan");
        Add("trace.col.partdescription",     "Mô tả linh kiện",                                      "Part Description");
        Add("trace.col.lot",                 "Lô",                                                   "Lot");
        Add("trace.col.status",              "Trạng thái",                                           "Status");
        Add("trace.col.ng.reasonnote",       "NG — lý do · ghi chú",                                 "NG — reason · note");
        Add("trace.col.tool",                "Công cụ",                                              "Tool");
        Add("trace.col.numbercode",          "Số / mã",                                              "Number / code");
        Add("trace.col.checkedby",           "Người kiểm",                                           "Checked by");
        Add("trace.col.checkedat",           "Thời điểm kiểm",                                       "Checked at");
        Add("trace.col.ngreason",            "Lý do NG",                                             "NG reason");
        Add("trace.col.item",                "Hạng mục",                                             "Item");
        Add("trace.col.ngnote",              "Ghi chú NG",                                           "NG note");

        // Frozen-snapshot HEADER labels. The English text is baked into the
        // immutable JSON at freeze time (TraceFreezeService) so it cannot be
        // localized at the source without re-freezing history — instead the
        // dialog maps the known baked English label → these keys at render.
        Add("trace.hdr.productcode",         "Mã sản phẩm",                                          "Product code");
        Add("trace.hdr.partdescription",     "Mô tả sản phẩm",                                       "Part description");
        Add("trace.hdr.customer",            "Khách hàng",                                           "Customer");
        Add("trace.hdr.targetqty",           "SL mục tiêu",                                          "Target qty");
        Add("trace.hdr.messtatus",           "Trạng thái MES",                                       "MES status");
        Add("trace.hdr.platecheck",          "Kiểm bản in",                                          "Plate check");
        Add("trace.hdr.cuttercheck",         "Kiểm khuôn cắt",                                       "Cutter check");
        Add("trace.hdr.judgment",            "Phán định",                                            "Judgment");
        // QC-tab (IPQC / FQC / OQC) frozen header labels.
        Add("trace.hdr.judgmentreason",      "Lý do phán định",                                      "Judgment reason");
        Add("trace.hdr.specialacceptreason", "Lý do chấp nhận đặc biệt",                             "Special-accept reason");
        Add("trace.hdr.ipqcsubmittedby",     "IPQC gửi bởi",                                         "IPQC submitted by");
        Add("trace.hdr.qaoutcome",           "Kết quả QA",                                           "QA outcome");
        Add("trace.hdr.qareason",            "Lý do QA",                                             "QA reason");
        Add("trace.hdr.qaapprovedby",        "QA phê duyệt bởi",                                     "QA approved by");
        Add("trace.hdr.inspectedby",         "Người kiểm",                                           "Inspected by");
        Add("trace.hdr.reviewedby",          "Người soát",                                           "Reviewed by");
        Add("trace.hdr.approvedby",          "Người duyệt",                                          "Approved by");

        // Fixed IPQC slot item labels (baked at freeze; Plan-C dynamic library
        // labels come from master data and are left as captured).
        Add("trace.item.material",           "Vật tư",                                               "Material");
        Add("trace.item.printa",             "In A (Màu)",                                           "Print A (Colour)");
        Add("trace.item.printb",             "In B (Chồng màu)",                                     "Print B (Registration)");
        Add("trace.item.printc",             "In C (Nội dung)",                                      "Print C (Content)");

        // Status VALUES (chips + frozen header status values). Only the known
        // enum tokens are mapped; data values (product code, customer, phase
        // token) never match and fall through unchanged.
        Add("trace.st.ok",                   "OK",                                                   "OK");
        Add("trace.st.ng",                   "NG",                                                   "NG");
        Add("trace.st.pending",              "Chờ",                                                  "Pending");
        Add("trace.st.pass",                 "Đạt",                                                  "Pass");
        Add("trace.st.reject",               "Loại",                                                 "Reject");
        Add("trace.st.gorun",                "Cho chạy",                                             "Go Run");
        Add("trace.st.stopline",             "Dừng chuyền",                                          "Stop Line");
        Add("trace.st.specialaccept",        "Chấp nhận đặc biệt",                                   "Special Accept");
        Add("trace.st.ok.specialaccept",     "OK · Chấp nhận đặc biệt",                              "OK · Special Accept");
        Add("trace.st.approved",             "Đã duyệt",                                             "Approved");
        Add("trace.st.rejected",             "Từ chối",                                              "Rejected");
        Add("trace.st.none",                 "—",                                                    "—");
    }
}

namespace CCL.MES.Hybrid.Client.Localization;

// feat/iqc-module-tabs — nhãn cho 3 sub-tab IQC (Dashboard · IQC Data · New
// Ticket), picker 4 nhóm, cột grid IQC Data, và 3 form placeholder (Chemical /
// Tools / Other). Đủ VI + EN parity (skill cmes-i18n-parity) — KHÔNG hardcode.
public sealed partial class TranslationCatalog
{
    private void RegisterIqcModule()
    {
        //     key                              vi                                     en
        // ── Sub-tabs ──────────────────────────────────────────────────────
        Add("iqc.tab.dashboard",           "Bảng điều khiển",                     "Dashboard");
        Add("iqc.tab.data",                "Dữ liệu IQC",                         "IQC Data");
        Add("iqc.tab.newticket",           "Phiếu mới",                           "New Ticket");

        // ── Group names ───────────────────────────────────────────────────
        Add("iqc.group.materials",         "Nguyên liệu",                         "Materials");
        Add("iqc.group.chemical",          "Hoá chất",                            "Chemical");
        Add("iqc.group.tools",             "Dụng cụ",                             "Tools");
        Add("iqc.group.other",             "Khác",                                "Other");
        Add("iqc.group.all",               "Tất cả",                              "All");

        // ── Dashboard (KPI placeholder có số liệu thật) ──────────────────
        Add("iqc.dash.title",              "Tổng quan IQC",                       "IQC overview");
        Add("iqc.dash.desc",               "Số liệu đếm thật từ phiếu đã lưu. Sẽ bổ sung biểu đồ & xu hướng.",
                                                                                  "Live counts from saved tickets. Charts & trends to follow.");
        Add("iqc.dash.total",              "Tổng số phiếu",                       "Total tickets");
        Add("iqc.dash.bygroup",            "Theo nhóm",                           "By group");
        Add("iqc.dash.bystatus",           "Theo trạng thái",                     "By status");
        Add("iqc.dash.pending",            "Chờ kiểm",                            "Pending");
        Add("iqc.dash.pass",               "Đạt",                                 "Pass");
        Add("iqc.dash.fail",               "Không đạt",                           "Fail");
        Add("iqc.dash.placeholder.note",   "Khu vực placeholder — sẽ bổ sung KPI sắp/đã hết hạn, xu hướng theo tuần.",
                                                                                  "Placeholder area — expiring-soon KPI and weekly trends to be added.");

        // ── IQC Data (list) ───────────────────────────────────────────────
        Add("iqc.data.title",              "Phiếu đã lưu",                        "Saved tickets");
        Add("iqc.data.search.ph",          "Tìm phiếu / Code IFS / mô tả…",       "Search receipt / Code IFS / description…");
        Add("iqc.data.filter.group",       "Nhóm",                                "Group");
        Add("iqc.data.empty",              "Chưa có phiếu nào khớp bộ lọc.",       "No tickets match the filter.");
        Add("iqc.data.loading",            "Đang tải…",                           "Loading…");
        Add("iqc.data.col.receipt",        "Phiếu nhập",                          "Receipt");
        Add("iqc.data.col.group",          "Nhóm",                                "Group");
        Add("iqc.data.col.codeifs",        "Code IFS",                            "Code IFS");
        Add("iqc.data.col.matdesc",        "Mô tả vật liệu",                      "Material description");
        Add("iqc.data.col.lot",            "Số lô / batch",                       "Lot/Batch");
        Add("iqc.data.col.manfdate",       "Ngày SX",                             "Manf date");
        Add("iqc.data.col.maker",          "Nhà sản xuất",                        "Maker");
        Add("iqc.data.col.supplier",       "Nhà cung cấp",                        "Supplier");
        Add("iqc.data.col.inspector",      "Người kiểm",                          "Inspector");
        Add("iqc.data.col.received",       "Ngày nhận",                           "Received");
        Add("iqc.data.col.qty",            "SL",                                  "Qty");
        Add("iqc.data.col.status",         "Trạng thái",                          "Status");
        Add("iqc.data.status.pending",     "Chờ kiểm",                            "Pending");
        Add("iqc.data.status.pass",        "Đạt",                                 "Pass");
        Add("iqc.data.status.fail",        "Không đạt",                           "Fail");
        Add("iqc.data.pager.status",       "Trang {0}/{1} · {2} phiếu",           "Page {0}/{1} · {2} tickets");
        Add("iqc.data.pager.prev",         "‹ Trước",                             "‹ Previous");
        Add("iqc.data.pager.next",         "Sau ›",                               "Next ›");
        // Row-action menu (RowContextMenu — L35).
        Add("iqc.data.menu.aria",          "Hành động phiếu {0}",                 "Actions for ticket {0}");
        Add("iqc.data.menu.view",          "Xem",                                 "View");
        Add("iqc.data.menu.open",          "Mở",                                  "Open");
        Add("iqc.data.menu.copy",          "Sao chép",                            "Copy");
        Add("iqc.data.menu.edit",          "Sửa",                                 "Edit");
        Add("iqc.data.menu.delete",        "Xoá",                                 "Delete");
        Add("iqc.data.menu.actions",       "Hành động",                           "Actions");
        // View detail dialog.
        Add("iqc.data.view.title",         "Chi tiết phiếu",                      "Ticket detail");
        Add("iqc.data.view.close",         "Đóng",                                "Close");
        // Not-yet-wired actions (Copy/Edit/Delete = placeholder — Henry mở rộng).
        Add("iqc.data.action.notyet",      "Chức năng này sẽ được bổ sung.",       "This action will be added later.");

        // ── New Ticket — group picker (transactional Modal, L34) ─────────
        Add("iqc.newticket.title",         "Tạo phiếu mới",                       "Create a new ticket");
        Add("iqc.newticket.pick",          "Chọn nhóm nguồn nhập",                "Pick the intake group");
        Add("iqc.newticket.pick.desc",     "Chọn 1 trong 4 nhóm để mở đúng biểu mẫu.",
                                                                                  "Pick one of four groups to open the matching form.");
        Add("iqc.newticket.cta",           "＋ Phiếu mới",                        "＋ New ticket");
        Add("iqc.group.materials.hint",    "Tra mô tả → chọn Code IFS (nhiều)",   "Search description → pick Code IFS (multi)");
        Add("iqc.group.chemical.hint",     "Phiếu hoá chất nhập kho",             "Incoming chemical ticket");
        Add("iqc.group.tools.hint",        "Phiếu dụng cụ / khuôn / dao",         "Tooling / die / cutter ticket");
        Add("iqc.group.other.hint",        "Nguồn nhập khác",                     "Other intake");

        // ── Materials inspection showcard (FloatingWindow — L34) ─────────
        Add("iqc.insp.title.new",          "Phiếu thanh tra nguyên liệu",         "Materials inspection ticket");
        Add("iqc.insp.aria",               "Phiếu thanh tra {0}",                 "Inspection ticket {0}");
        Add("iqc.insp.savedraft",          "Lưu nháp",                            "Save draft");
        Add("iqc.insp.complete",           "Hoàn tất phiếu",                      "Complete ticket");
        Add("iqc.insp.maxwins",            "Đang mở tối đa {0} phiếu. Đóng bớt để mở thêm.",
                                                                                  "Up to {0} tickets open. Close one to open another.");

        // ── Placeholder forms (Chemical / Tools / Other) ─────────────────
        Add("iqc.form.placeholder.badge",  "Biểu mẫu tạm — sẽ bổ sung trường",     "Placeholder form — fields to be added");
        Add("iqc.form.placeholder.note",   "Đây là biểu mẫu tối thiểu cho nhóm này. Các trường chuyên biệt sẽ được bổ sung sau.",
                                                                                  "This is a minimal form for this group. Group-specific fields will be added later.");
        Add("iqc.ph.f.desc",               "Mô tả",                               "Description");
        Add("iqc.ph.f.desc.ph",            "Mô tả vật tư nhập",                    "Describe the intake item");
        Add("iqc.ph.f.lot",                "Số lô / batch",                       "Lot/Batch No");
        Add("iqc.ph.f.lot.ph",             "Số lô / batch",                       "Lot / batch number");
        Add("iqc.ph.f.qty",                "Số lượng",                            "Quantity");
        Add("iqc.ph.f.qty.ph",             "Số lượng nhận",                       "Received quantity");
        Add("iqc.ph.f.uom",                "Đơn vị",                              "Unit");
        Add("iqc.ph.f.uom.ph",             "vd. kg, hộp, cái",                    "e.g. kg, box, pcs");
        Add("iqc.ph.f.inspector",          "Người kiểm",                          "Inspector");
        Add("iqc.ph.f.inspector.auto",     "(tự động từ tài khoản đăng nhập)",     "(auto from the signed-in account)");
        Add("iqc.ph.save",                 "Lưu phiếu",                           "Save ticket");
        Add("iqc.ph.saving",               "Đang lưu…",                           "Saving…");
        Add("iqc.ph.cancel",               "Huỷ",                                 "Cancel");
        Add("iqc.ph.saved",                "Đã tạo phiếu {0}.",                    "Ticket {0} created.");

        // ── P12 bước 3 — lưới hạng mục kiểm đã đóng băng ────────────────
        Add("iqc.items.empty",             "Phiếu này chưa có hạng mục kiểm nào. Mã nguyên liệu không tra được trong danh mục nên hệ thống không suy ra bộ hạng mục — nhập tay như trước.",
                                           "This ticket has no check items. The material code could not be resolved in the catalogue, so no item set was derived — enter them manually as before.");
        Add("iqc.items.spec",              "Theo tiêu chuẩn {0}",                 "Per standard {0}");
        Add("iqc.items.matrix",            "Nguyên liệu này chưa có tiêu chuẩn riêng — đang kiểm theo MA TRẬN MẶC ĐỊNH.",
                                           "This material has no dedicated standard yet — inspecting against the DEFAULT MATRIX.");
        Add("iqc.items.unspecified",       "chưa xác định",                       "not specified");
        Add("iqc.items.unspecified.why",   "Tiêu chuẩn gốc còn để trống — hỏi QA điền trước khi chấm ĐẠT.",
                                           "The source standard is still blank — ask QA to fill it in before passing this item.");
        Add("iqc.status.ok",               "Đạt",                                 "Pass");
        Add("iqc.status.ng",               "Không đạt",                           "Fail");
        Add("iqc.status.pending",          "Chưa kiểm",                           "Not checked");
        Add("iqc.items.after.create",      "Hạng mục kiểm được dựng khi phiếu được tạo — lưu phiếu rồi mở lại để chấm.",
                                           "Check items are built when the ticket is created — save the ticket, then reopen it to record results.");
        Add("iqc.step.visual.desc",        "Nhận dạng vật liệu và ngoại quan.",    "Material identification and visual appearance.");
        Add("iqc.step.functional.desc",    "Kích thước, bám dính, độ cứng và các phép đo lý hoá.",
                                           "Dimensions, adhesion, hardness and physical/chemical measurements.");

        // ── P13 bước 4b — ô đếm lỗi, ô đo, và kết luận MÁY chấm ────────
        Add("iqc.items.col.record",        "Ghi nhận",                            "Record");
        Add("iqc.items.defect.ph",         "số lỗi",                              "defects");
        Add("iqc.items.defect.hint",       "Không chấp nhận lỗi: chỉ 0 mới đạt.",  "Zero-defect: only 0 passes.");
        Add("iqc.items.measure.n",         "Đo {0} lần",                          "{0} measurements");
        Add("iqc.items.measure.ph",        "lần {0}",                             "#{0}");
        Add("iqc.items.limit",             "Ngưỡng {0}",                          "Limit {0}");
        Add("iqc.items.limit.face",        "lớp {0}",                             "{0} side");
        Add("iqc.items.tear",              "Rách vật liệu",                       "Material tore");
        Add("iqc.items.tear.why",          "Vật liệu rách trước khi bong keo ⇒ lực bám đã lớn hơn độ bền của chính vật liệu, nên tính ĐẠT.",
                                           "The material tore before the adhesive released ⇒ the bond exceeded the material's own strength, so it passes.");
        Add("iqc.items.save",              "Lưu",                                 "Save");
        Add("iqc.items.spec.pending",      "Bộ tiêu chuẩn {0} nhập từ file master và CHƯA được QC duyệt. Vẫn kiểm bình thường, nhưng đối chiếu lại với bản giấy trước khi chốt.",
                                           "Standard {0} was imported from the master file and has NOT been approved by QC yet. Carry on inspecting, but check it against the paper copy before completing.");
        Add("iqc.items.spec.rejected",     "Bộ tiêu chuẩn {0} đã bị QC bác. Hỏi QA trước khi chấm.",
                                           "Standard {0} was rejected by QC. Ask QA before recording results.");

        // Kết luận máy chấm — hiện nguyên nhân, không chỉ hiện đạt/trượt.
        Add("iqc.items.auto",              "Máy chấm",                            "Auto");
        Add("iqc.judge.zero_defect",       "Không có lỗi nào",                    "No defects found");
        Add("iqc.judge.defect_found",      "Có lỗi — không chấp nhận lỗi nào",     "Defect found — zero-defect rule");
        Add("iqc.judge.defect_incomplete", "Chưa đếm",                            "Not counted yet");
        Add("iqc.judge.defect_negative",   "Số lỗi không thể âm",                 "Defect count cannot be negative");
        Add("iqc.judge.no_defect_columns", "Không có ô đếm lỗi",                  "No defect column");
        Add("iqc.judge.all_in_range",      "Mọi phép đo trong ngưỡng",            "All measurements in range");
        Add("iqc.judge.below_low",         "Phép đo {0} dưới cận dưới",           "Measurement {0} is below the lower limit");
        Add("iqc.judge.above_up",          "Phép đo {0} trên cận trên",           "Measurement {0} is above the upper limit");
        Add("iqc.judge.measurement_missing", "Chưa đo lần {0}",                   "Measurement {0} not taken");
        Add("iqc.judge.no_measurements",   "Chưa có phép đo nào",                 "No measurements yet");
        Add("iqc.judge.no_numeric_limit",  "Tiêu chuẩn không có ngưỡng số — người kiểm chấm",
                                           "The standard has no numeric limit — the inspector decides");
        Add("iqc.judge.limit_has_no_bound", "Ngưỡng không có cận nào",            "The limit has no bound");
        Add("iqc.judge.tear_accepted",     "Rách vật liệu — tính đạt",            "Material tore — accepted");
        Add("iqc.judge.human_only",        "Người kiểm chấm",                     "Inspector decides");

        // Ghi đè kết luận máy.
        Add("iqc.items.override.need",     "Phán định của bạn khác máy chấm. Ghi lý do thì mới lưu được.",
                                           "Your verdict differs from the automatic judgement. Enter a reason to save it.");
        Add("iqc.items.override.ph",       "Vì sao khác máy chấm?",               "Why does it differ?");
        Add("iqc.items.override.by",       "{0} đã đổi khác máy chấm: {1}",        "{0} overrode the automatic judgement: {1}");
        Add("iqc.items.override.cancel",   "Bỏ",                                  "Discard");

        // ── P12 bước 2b — soạn tiêu chuẩn theo mã nguyên liệu ───────────
        Add("iqc.tab.spec",                "Tiêu chuẩn",                          "Standards");
        Add("iqc.spec.title",              "Tiêu chuẩn kiểm theo mã nguyên liệu",  "Inspection standards by material code");
        Add("iqc.spec.desc",               "Tra mã nguyên liệu để xem bộ hạng mục sẽ áp cho các lô nhập sau. Thêm hoặc gỡ hạng mục tại đây; phiếu ĐÃ mở giữ bản đóng băng riêng nên không bị ảnh hưởng.",
                                           "Look up a material code to see the item set that will apply to future incoming lots. Add or remove items here; tickets already opened keep their own frozen copy and are unaffected.");
        Add("iqc.spec.search.ph",          "Gõ mã hoặc mô tả — vd 336 hoặc PET",   "Type a code or description — e.g. 336 or PET");
        Add("iqc.spec.search.none",        "Không có nguyên liệu khớp.",           "No matching materials.");
        Add("iqc.spec.search.tooshort",    "Gõ tối thiểu 3 ký tự để gợi ý.",       "Type at least 3 characters for suggestions.");
        Add("iqc.spec.load",               "Tra",                                 "Look up");
        Add("iqc.spec.showinactive",       "Hiện cả hạng mục đã gỡ",              "Show removed items");
        Add("iqc.spec.none",               "Mã {0} chưa có tiêu chuẩn riêng — các lô nhập đang kiểm theo MA TRẬN MẶC ĐỊNH. Thêm hạng mục bên dưới để soạn tiêu chuẩn riêng cho mã này.",
                                           "Material {0} has no dedicated standard yet — incoming lots are inspected against the DEFAULT MATRIX. Add items below to build a dedicated standard.");
        Add("iqc.spec.via_mother",         "PartNo {0} đang xem tiêu chuẩn của mã mẹ {1}.",
                                           "PartNo {0} is showing the standard for mother code {1}.");
        Add("iqc.spec.ambiguous_mother",   "PartNo này gắn với hơn một mã mẹ — không chọn hộ. Tra đúng mã mẹ hoặc sửa catalog.",
                                           "This PartNo maps to more than one mother code — look up the mother directly or fix the catalog.");
        // Sửa tiêu chuẩn của mã mẹ = sửa cho CẢ HỌ. Nói rõ họ gồm những mã nào.
        Add("iqc.spec.mother",             "Mã mẹ",                               "Mother code");
        Add("iqc.spec.appliesto",          "Áp dụng cho {0} PartNo",              "Applies to {0} PartNo(s)");
        Add("iqc.spec.appliesto.why",      "Mọi thay đổi ở đây áp cho tất cả PartNo dưới mã mẹ này.",
                                           "Every change here applies to all PartNos under this mother code.");
        Add("iqc.spec.appliesto.more",     "… và {0} mã nữa",                     "… and {0} more");
        Add("iqc.spec.appliesto.none",     "Mã này chưa có PartNo con trong catalog.",
                                           "This code has no child PartNo in the catalog.");
        Add("iqc.spec.specno",             "Tiêu chuẩn {0}",                      "Standard {0}");
        Add("iqc.spec.frommaster",         "từ file master",                      "from master file");

        // ── P13: một mã có NHIỀU bộ tiêu chuẩn ────────────────────────────
        Add("iqc.spec.col.set",            "Bộ",                                  "Set");
        Add("iqc.spec.multi",              "Mã này đang có {0} bộ tiêu chuẩn. Phiếu kiểm gộp hạng mục của TẤT CẢ các bộ còn bật — gỡ bớt bộ không dùng để người kiểm khỏi làm trùng.",
                                                                                  "This code has {0} standard sets. A ticket merges items from ALL active sets — remove unused sets so the inspector does not repeat the same check.");
        Add("iqc.spec.set.remove",         "Gỡ bộ này",                           "Remove this set");
        Add("iqc.spec.consolidate",        "Gộp về một bộ",                       "Merge into one set");
        Add("iqc.spec.consolidate.why",    "Chép các hạng mục còn thiếu sang bộ mới nhất TRƯỚC, rồi mới tắt các bộ cũ — không mất phép kiểm nào.",
                                                                                  "Copies missing items into the newest set FIRST, then deactivates the old ones — no check is lost.");
        Add("iqc.spec.consolidated",       "Đã gộp về {0}: chép sang {1} hạng mục còn thiếu, tắt {2} bộ cũ.",
                                                                                  "Merged into {0}: copied {1} missing item(s), deactivated {2} old set(s).");
        Add("iqc.spec.set.restore",        "Dùng lại",                            "Restore");
        Add("iqc.spec.pendingqc",          "chờ QC duyệt",                        "pending QC");
        Add("iqc.spec.pendingqc.why",      "Bộ này nhập từ file master và chưa ai trong QC xác nhận. Phiếu vẫn dùng được.",
                                                                                  "Imported from the master file and not yet confirmed by QC. Tickets still use it.");
        Add("iqc.spec.frommaster.why",     "Bộ này đến từ file master. Hạng mục bạn thêm vào đây sẽ bị lần import kế tiếp ghi đè nếu file master không có dòng tương ứng.",
                                           "This set came from the master file. Items you add here may be overwritten by the next import if the master file has no matching row.");
        Add("iqc.spec.empty",              "Chưa có hạng mục nào.",               "No items yet.");
        Add("iqc.spec.col.group",          "Nhóm",                                "Group");
        Add("iqc.spec.col.freq",           "Tần suất",                            "Frequency");
        Add("iqc.spec.status.on",          "Đang dùng",                           "Active");
        Add("iqc.spec.status.off",         "Đã gỡ",                               "Removed");
        Add("iqc.spec.addrow",             "Thêm hạng mục",                       "Add item");
        Add("iqc.spec.addnew.save",        "Lưu hạng mục",                        "Save item");
        Add("iqc.spec.f.item",             "Hạng mục",                            "Check item");
        Add("iqc.spec.f.item.pick",        "— chọn hạng mục —",                    "— pick an item —");
        Add("iqc.spec.f.acc.ph",           "Tiêu chuẩn chấp nhận cho mã này",      "Acceptance criteria for this material");
        Add("iqc.spec.f.method.ph",        "Phương pháp đo",                       "Measurement method");
        Add("iqc.spec.f.freq.ph",          "vd. All lot, 1 lần/tháng",             "e.g. All lot, monthly");
        Add("iqc.spec.menu.aria",          "Hành động cho hạng mục {0}",           "Actions for item {0}");
        Add("iqc.spec.menu.deactivate",    "Gỡ hạng mục",                         "Remove item");
        Add("iqc.spec.menu.restore",       "Khôi phục",                           "Restore");

        // ── P12 — chốt phiếu: đánh giá hết hạng mục mới cho chốt ─────────
        Add("iqc.complete.progress",       "Đã kiểm {0}/{1} hạng mục",             "{0} of {1} items evaluated");
        Add("iqc.complete.action",         "Chốt phiếu",                          "Complete ticket");
        Add("iqc.complete.blocked",        "Còn {0} hạng mục chưa kiểm — phải đánh giá hết mới chốt được phiếu.",
                                           "{0} items still unevaluated — every item must be judged before the ticket can be completed.");
        Add("iqc.insp.savecheck",          "Lưu & bắt đầu kiểm",                   "Save & start inspecting");
        Add("iqc.insp.complete.moved",     "Hạng mục kiểm được dựng khi lưu phiếu. Lưu xong, mở lại phiếu để chấm rồi mới chốt được.",
                                           "Check items are built when the ticket is saved. Save first, reopen the ticket to record results, then complete it.");

        // ── P13 bước 6 — khối NG / claim NCC ────────────────────────────
        Add("iqc.tab.ng",                  "NG / claim",                          "NG / claim");
        Add("iqc.ng.title",                "Nguyên liệu NG & claim NCC",          "NG material & supplier claim");
        Add("iqc.ng.desc",                 "Ghi vụ hỏng ngay khi phát hiện — không bắt buộc có phiếu IQC. 38% vụ tìm thấy ở sản xuất.",
                                           "Record a defect as soon as it is found — no IQC ticket required. 38% of cases are found in production.");
        Add("iqc.ng.new",                  "＋ Ghi NG",                            "＋ Record NG");
        Add("iqc.ng.search.ph",            "Tìm PartNo…",                         "Search PartNo…");
        Add("iqc.ng.empty",                "Chưa có vụ NG nào khớp bộ lọc.",       "No NG records match the filter.");
        Add("iqc.ng.create.hint",          "Mặc định giai đoạn Unknown — đừng bịa là IQC nếu phát hiện ở máy.",
                                           "Default stage is Unknown — do not mark IQC unless the find was at incoming inspection.");
        Add("iqc.ng.cancel",               "Huỷ",                                 "Cancel");
        Add("iqc.ng.save",                 "Lưu",                                 "Save");
        Add("iqc.ng.savefail",             "Không lưu được. Kiểm tra dữ liệu rồi thử lại.",
                                           "Could not save. Check the data and try again.");
        Add("iqc.ng.col.when",             "Ngày phát hiện",                      "Detected");
        Add("iqc.ng.col.stage",            "Giai đoạn",                           "Stage");
        Add("iqc.ng.col.part",             "PartNo",                              "PartNo");
        Add("iqc.ng.col.lot",              "Lô NCC",                              "Supplier lot");
        Add("iqc.ng.col.defect",           "Lỗi",                                 "Defect");
        Add("iqc.ng.col.m2",               "m²",                                  "m²");
        Add("iqc.ng.col.qty",              "SL",                                  "Qty");
        Add("iqc.ng.col.rolls",            "Cuộn",                                "Rolls");
        Add("iqc.ng.col.status",           "Trạng thái",                          "Status");
        Add("iqc.ng.col.who",              "Người ghi",                           "Recorded by");
        Add("iqc.ng.uom",                  "Đơn vị SL",                           "Qty UoM");
        Add("iqc.ng.claimref",             "Số claim / tham chiếu",               "Claim ref");
        Add("iqc.ng.suppliernote",         "Ghi chú NCC",                          "Supplier note");
        Add("iqc.ng.closereason",          "Lý do không claim",                    "Reason for no claim");
        Add("iqc.ng.menu.more",            "Thêm hành động",                       "More actions");
        Add("iqc.ng.menu.aria",            "Hành động vụ NG {0}",                  "Actions for NG {0}");
        Add("iqc.ng.act.claim",            "Gửi claim",                            "File claim");
        Add("iqc.ng.act.confirm",          "NCC xác nhận",                         "Supplier confirmed");
        Add("iqc.ng.act.settle",           "Khép bồi thường",                      "Settle");
        Add("iqc.ng.act.close",            "Đóng không claim",                     "Close without claim");
        Add("iqc.ng.status.Open",          "Mở",                                  "Open");
        Add("iqc.ng.status.Claimed",       "Đã claim",                             "Claimed");
        Add("iqc.ng.status.SupplierConfirmed", "NCC xác nhận",                     "Supplier confirmed");
        Add("iqc.ng.status.Settled",       "Đã bồi thường",                        "Settled");
        Add("iqc.ng.status.ClosedNoClaim", "Đóng không claim",                     "Closed, no claim");
        Add("iqc.ng.stage.Unknown",        "Chưa rõ",                              "Unknown");
        Add("iqc.ng.stage.Iqc",            "IQC",                                 "IQC");
        Add("iqc.ng.stage.Production",     "Sản xuất",                             "Production");
        Add("iqc.ng.settle.Replacement",   "Thay hàng",                            "Replacement");
        Add("iqc.ng.settle.CreditNote",    "Giảm giá / CN",                        "Credit note");
        Add("iqc.ng.settle.Return",        "Trả hàng",                             "Return");
        Add("iqc.ng.settle.Scrap",         "Hủy / scrap",                          "Scrap");
        Add("iqc.ng.forbidden",            "Vai này không được ghi khối NG.",      "This role cannot write NG records.");
        Add("iqc.ng.close_reason_required","Phải ghi lý do khi đóng không claim.", "A reason is required to close without a claim.");
        Add("iqc.ng.not_found",            "Không tìm thấy vụ NG.",                "NG record not found.");
        Add("iqc.ng.invalid_transition",   "Bước này không hợp lệ với trạng thái hiện tại.",
                                           "That step is not valid from the current status.");
        Add("iqc.ng.settlement_required",  "Chọn hình thức bồi thường.",           "Pick a settlement type.");
        Add("iqc.ng.claim_required_before_settle", "Phải gửi claim trước khi khép bồi thường.",
                                           "A claim must be filed before settlement.");
        Add("iqc.ng.detected_at_required", "Thiếu ngày phát hiện.",                "Detected date is required.");
        Add("iqc.ng.defect_required",      "Thiếu tên hoặc mã lỗi.",               "Defect name or code is required.");
        Add("iqc.ng.quantity_required",    "Cần ít nhất một đơn vị số lượng (m², SL, hoặc cuộn).",
                                           "At least one quantity (m², qty, or rolls) is required.");
        Add("iqc.ng.quantity_must_be_positive", "Số lượng phải lớn hơn 0.",        "Quantity must be greater than 0.");
        Add("iqc.ng.material_required",    "Cần PartNo (hoặc phiếu IQC / lô).",    "PartNo (or an IQC ticket / lot) is required.");
    }
}

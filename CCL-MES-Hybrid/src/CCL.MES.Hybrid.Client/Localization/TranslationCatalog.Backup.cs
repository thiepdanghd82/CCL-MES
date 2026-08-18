namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2D — Pages/SettingsBackup.razor (backup.*).
public sealed partial class TranslationCatalog
{
    private void RegisterBackup()
    {
        //     key                                vi                                                     en
        Add("backup.pagetitle",                "Sao lưu & Phục hồi — CCL MES",                        "Backup & Restore — CCL MES");
        Add("backup.title",                    "Sao lưu & Phục hồi",                                  "Backup & Restore");
        Add("backup.loading",                  "Đang tải…",                                            "Loading…");
        Add("backup.on",                       "bật",                                                  "on");
        Add("backup.off",                      "tắt",                                                  "off");

        // Create a backup
        Add("backup.create.title",             "Tạo bản sao lưu",                                      "Create a backup");
        Add("backup.create.help",              "Tạo ngay một bản chụp (snapshot) của cơ sở dữ liệu SQLite. Bản chụp dùng SQLite online backup API và an toàn khi máy chủ đang phục vụ.",
                                               "Create a snapshot of the SQLite database immediately. The snapshot uses the SQLite online backup API and is safe while the server is serving.");
        Add("backup.create.button",            "Tạo bản chụp mới",                                     "Create new snapshot");
        Add("backup.create.busy",              "Đang tạo…",                                            "Creating…");
        Add("backup.create.success.prefix",    "Đã tạo:",                                            "Created:");
        Add("backup.create.success.sha",       "SHA-256:",                                             "SHA-256:");

        // Scheduled backup
        Add("backup.sched.title",              "Sao lưu theo lịch",                                    "Scheduled backup");
        Add("backup.sched.help",               "Tự động chụp ban đêm + lưu trữ blob + kiểm tra toàn vẹn. Chọn giờ chạy (ICT) và thời gian lưu giữ. Bản sao ngoài site là một cron job riêng (scripts/backup-offsite).",
                                               "Automated nightly snapshot + blob archive + integrity verify. Choose the run hour (ICT) and retention. The off-site copy is a separate cron job (scripts/backup-offsite).");
        Add("backup.sched.status",             "Trạng thái",                                           "Status");
        Add("backup.sched.status.enabled",     "Đang bật",                                             "Enabled");
        Add("backup.sched.status.disabled",    "Đang tắt",                                             "Disabled");
        Add("backup.sched.nextrun",            "Lần chạy kế tiếp",                                     "Next run");
        Add("backup.sched.lastrun",            "Lần chạy gần nhất",                                    "Last run");
        Add("backup.sched.lastrun.never",      "Chưa từng (phiên này)",                                "Never (this session)");
        Add("backup.sched.blobarchive",        "Lưu trữ blob",                                         "Blob archive");
        Add("backup.sched.webhook",            "Webhook cảnh báo",                                     "Alert webhook");
        Add("backup.sched.enable",             "Bật sao lưu ban đêm",                                  "Enable nightly backup");
        Add("backup.sched.hour",               "Giờ (0–23, ICT)",                                      "Hour (0–23, ICT)");
        Add("backup.sched.retention",          "Giữ trong (ngày)",                                     "Keep for (days)");
        Add("backup.sched.minkeep",            "Luôn giữ (gần nhất)",                                  "Always keep (most recent)");
        Add("backup.sched.save",               "Lưu lịch",                                             "Save schedule");
        Add("backup.sched.save.busy",          "Đang lưu…",                                            "Saving…");
        Add("backup.sched.save.ok",            "Đã lưu lịch.",                                          "Schedule saved.");
        Add("backup.sched.runnow",             "Chạy sao lưu ngay",                                    "Run backup now");
        Add("backup.sched.runnow.busy",        "Đang chạy…",                                            "Running…");

        // Restore from file
        Add("backup.restore.title",            "Phục hồi từ tệp",                                      "Restore from file");
        Add("backup.restore.help",             "Tải lên một tệp .db hoặc .bak do ứng dụng này tạo ra. Máy chủ sẽ kiểm tra header + schema SQLite TRƯỚC KHI áp dụng và sẽ tự động tạo một bản sao lưu tiền-phục-hồi để bạn có thể quay lại nếu cần.",
                                               "Upload a .db or .bak file created by this application. The server will verify the SQLite header + schema BEFORE applying it and will automatically create a pre-restore backup so you can roll back if needed.");
        Add("backup.restore.button",           "Phục hồi",                                             "Restore");
        Add("backup.restore.busy",             "Đang phục hồi…",                                        "Restoring…");
        Add("backup.restore.selected",         "Đã chọn:",                                             "Selected:");
        Add("backup.restore.confirm",          "Thao tác này sẽ GHI ĐÈ cơ sở dữ liệu hiện tại bằng \"{0}\". Máy chủ sẽ tự động tạo một bản sao lưu tiền-phục-hồi để quay lại. Bạn có chắc chắn muốn tiếp tục?",
                                               "This will OVERWRITE the current database with \"{0}\". The server will automatically create a pre-restore backup for rollback. Are you sure you want to continue?");
        Add("backup.restore.success.prefix",   "Phục hồi thành công. Bản sao lưu tiền-phục-hồi:",     "Restore succeeded. Pre-restore backup:");
        Add("backup.restore.success.suffix",   "Chúng tôi khuyến nghị khởi động lại ứng dụng để mọi dịch vụ đọc được cơ sở dữ liệu mới.",
                                               "We recommend restarting the application to ensure every service reads the new DB.");

        // Snapshot list
        Add("backup.list.title",               "Danh sách bản chụp ({0})",                             "Snapshot list ({0})");
        Add("backup.list.empty",               "Chưa có bản chụp nào. Hãy tạo bản đầu tiên ở phía trên.",
                                               "No snapshots yet. Create the first one above.");
        Add("backup.list.col.filename",        "Tên tệp",                                              "File name");
        Add("backup.list.col.size",            "Kích thước",                                           "Size");
        Add("backup.list.col.sha",             "SHA-256",                                              "SHA-256");
        Add("backup.list.col.created",         "Tạo lúc (UTC)",                                        "Created (UTC)");
        Add("backup.list.col.type",            "Loại",                                                 "Type");
        Add("backup.list.copy",                "Sao chép",                                             "Copy");
        Add("backup.chip.prerestore",          "tiền-phục-hồi",                                        "pre-restore");
        Add("backup.chip.snapshot",            "bản chụp",                                             "snapshot");
    }
}

namespace CCL.MES.Domain.Audit;

/// <summary>
/// Phase 6 Bước 5 — action codes for the audit log. const string (not
/// enum) so callers can introduce new actions in feature branches
/// without a Domain enum migration. Each new code only adds to the list
/// here; ordering is alphabetical for easy diff review.
/// </summary>
public static class AuditAction
{
    public const string BackupCreate          = "BACKUP_CREATE";
    public const string BackupRestore         = "BACKUP_RESTORE";          // emitted by scripts/BackupRestore (Source = Console)
    public const string IqcApprove            = "IQC_APPROVE";             // Phase 6 Bước 7 — pass/fail in Detail
    public const string IqcCreate             = "IQC_CREATE";              // Phase 6 Bước 7
    public const string LoginDisabled         = "LOGIN_DISABLED";          // valid creds but IsActive = false
    public const string LoginFail             = "LOGIN_FAIL";              // wrong username OR wrong password (same code — no oracle)
    public const string LoginOk               = "LOGIN_OK";
    public const string Logout                = "LOGOUT";
    public const string NpiImport             = "NPI_IMPORT";              // Phase 7 — NPI master CSV replace-all import; detail JSON: { table, parsed, inserted, skipped, backup_file, backup_sha256, elapsed_ms }
    public const string QcApprove             = "QC_APPROVE";              // pass/fail in Detail
    public const string QcCreate              = "QC_CREATE";
    public const string SpecApprove           = "SPEC_APPROVE";
    // Phase 8 PR #31d — Backfill 4 detail-sheet fields trên SpecPrint cho
    // ProductRevisions existing (KHÔNG tạo mới). detail JSON: { backfilled,
    // skipped, fields_touched, files: [{filename, status, ref_no}] }
    public const string SpecBackfillDetail    = "SPEC_BACKFILL_DETAIL";
    public const string SpecCreate            = "SPEC_CREATE";
    // Phase 8 PR #31c — Export list view (CSV / XLSX / PDF). detail JSON:
    // { format, search, rows, filename, content_length }
    public const string SpecExport            = "SPEC_EXPORT";
    // Phase 8 PR #31a — xlsx import (silkscreen). detail JSON: { spec_code,
    // ref_no, title, product_id, process_code, source, filename, rows_parsed,
    // warnings, created_new_product }
    public const string SpecImport            = "SPEC_IMPORT";
    // Refresh-samples Admin-only batch (idempotent). detail JSON: { added,
    // updated, skipped, files: [{filename, ref_no, status}] }
    public const string SpecRefreshSamples    = "SPEC_REFRESH_SAMPLES";
    public const string UserCreate            = "USER_CREATE";
    public const string UserDisplayChange     = "USER_DISPLAY_CHANGE";
    public const string UserResetPassword     = "USER_RESET_PASSWORD";
    public const string UserRoleChange        = "USER_ROLE_CHANGE";
    public const string UserSelfDisplayChange = "USER_SELF_DISPLAY_CHANGE";
    public const string UserSelfPasswordChange= "USER_SELF_PWD_CHANGE";
    public const string UserSetActive         = "USER_SET_ACTIVE";
    public const string WcActiveToggle        = "WC_ACTIVE_TOGGLE";        // Phase 8 — WorkCenter active flag flip; detail JSON: { wc_id, code, from, to }
    public const string WcCopy                = "WC_COPY";                 // Phase 8 — WorkCenter duplicate via context menu; detail JSON: { src_id, src_code, new_id, new_code }
    public const string WcUpdate              = "WC_UPDATE";               // Phase 8 — WorkCenter row edit via context menu; detail JSON: { wc_id, code, changes: {...} }
    public const string WoAdvance             = "WO_ADVANCE";
    public const string WoFlagsUpdate         = "WO_FLAGS_UPDATE";
}

namespace CCL.MES.Domain.Audit;

/// <summary>
/// Phase 6 Bước 5 — action codes for the audit log. const string (not
/// enum) so callers can introduce new actions in feature branches
/// without a Domain enum migration. Each new code only adds to the list
/// here; ordering is alphabetical for easy diff review.
/// </summary>
public static class AuditAction
{
    // Phase 9 audit-export — Admin downloaded the audit log via the
    // CSV / XLSX export endpoint. This is the "audit-the-audit" hook:
    // an admin lifting trail data out of the system is itself an event
    // that needs to appear in the trail (chống admin tampering — admin
    // không thể xóa rồi export một bản "sạch" mà KHÔNG để lại dấu vết
    // export-event). detail JSON: { format, search, action_filter,
    // actor_filter, from, to, rows, filename, content_length }.
    public const string AuditExport           = "AUDIT_EXPORT";
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
    // Phase 8 PR-L1 — Spec lifecycle Copy. Deep-clones content (4 sub-specs +
    // SpecPrintColor + flexo rows + QC plan QcWindow/Criterion); does NOT
    // clone QcCaptures (results stay with source rev) or Drawings (files are
    // version-controlled separately and require fresh upload chain).
    // detail JSON: { source_id, source_code, source_rev, new_id, new_code,
    //   product_id, cloned_counts: { material, print, diecut, finishing,
    //   print_colors, flexo_cutting, flexo_ink, qc_windows, qc_criteria } }
    public const string SpecCopy              = "SPEC_COPY";
    public const string SpecCreate            = "SPEC_CREATE";
    // Phase 8 PR #31c — Export list view (CSV / XLSX / PDF). detail JSON:
    // { format, search, rows, filename, content_length }
    public const string SpecExport            = "SPEC_EXPORT";
    // Phase 8 PR #31a — xlsx import (silkscreen). detail JSON: { spec_code,
    // ref_no, title, product_id, process_code, source, filename, rows_parsed,
    // warnings, created_new_product }
    public const string SpecImport            = "SPEC_IMPORT";
    // Phase 8 PR-L3 — Spec lifecycle Purge (HostedService hard-delete after
    // retention). Per-spec audit emit BEFORE the delete so the forensic trail
    // survives. detail JSON: { spec_code, rev_id, rev_code, trashed_at,
    // age_days, drawing_blob_keys_removed, blob_cleanup_failures }.
    public const string SpecPurge             = "SPEC_PURGE";
    // Phase 8 PR-D-3 — QC Plans tab atomic per-stage upsert. detail JSON:
    // { revision_id, stage, criteria_count, created, updated, deleted }
    public const string SpecQcPlanUpsert      = "SPEC_QC_PLAN_UPSERT";
    // Phase 8 PR-L2 — Spec lifecycle Revise. Creates a new Draft revision with
    // ParentRevisionId pointing at the source (lineage), bumps RevisionCode via
    // SpecRevisionHelpers, deep-clones content (same scope as Copy), auto-
    // supersedes the source. Per approved Q1: reason mandatory (>=5 chars)
    // captured in ChangeSummary. detail JSON: { source_id, source_code,
    // source_rev, source_status_before, new_id, new_rev, reason, cloned_counts:
    // {...same shape as SPEC_COPY...} }.
    public const string SpecRevise            = "SPEC_REVISE";
    // Phase 8 PR-L2 — Spec lifecycle Supersede (manual). Operator types the
    // SpecCode to confirm; server validates the typed value matches before
    // flipping Status. Gate: Status in {Approved, Released}. detail JSON:
    // { rev_id, rev_code, spec_code, from_status, manual: true }.
    public const string SpecSupersede         = "SPEC_SUPERSEDE";
    // Phase 8 PR-L1 — Spec edit (Draft-only). Surface for Title / RefNo /
    // InspectionLevel / SpecPrint ColorSpecJson/ProcessCode/ProductSize params.
    // Approved/Released/Superseded revs are immutable — server returns 422
    // when invoked on a non-Draft revision.
    // detail JSON: { rev_id, rev_code, fields_changed: [...] }.
    public const string SpecUpdate            = "SPEC_UPDATE";
    // Phase 8 PR-D-4 — QC Capture (NPI spec-level inspection result). detail JSON:
    // { revision_id, stage, criterion_id, result, has_measurement, ng_reason_code,
    //   has_comment }
    public const string SpecQcCapture         = "SPEC_QC_CAPTURE";
    // Phase 8 PR-D-5b — Drawing version uploaded (FilesystemBlobStore persisted).
    // detail JSON: { revision_id, drawing_id, kind, version_no, filename,
    //   sha256_short, size_bytes, has_change_reason }
    public const string DrawingUpload         = "DRAWING_UPLOAD";
    // Phase 8 PR-D-5c — Drawing approval chip decided (Approve or Reject). detail
    // JSON: { revision_id, drawing_id, version_id, version_no, role, decision,
    //   has_comment, version_status_after, drawing_status_after }.
    public const string DrawingDecide         = "DRAWING_DECIDE";
    // Phase 8 PR-D-5c — Drawing version rolled to Superseded when a newer version
    // becomes Approved. Emit one row per superseded version. detail JSON:
    // { revision_id, drawing_id, superseded_version_id, superseded_version_no,
    //   by_version_id, by_version_no, by_decided_user }.
    public const string DrawingSupersede      = "DRAWING_SUPERSEDE";
    // Refresh-samples Admin-only batch (idempotent). detail JSON: { added,
    // updated, skipped, files: [{filename, ref_no, status}] }
    public const string SpecRefreshSamples    = "SPEC_REFRESH_SAMPLES";
    // Phase 8 PR-L3 — Spec lifecycle Restore (un-trash). detail JSON:
    // { rev_id, rev_code, spec_code, was_trashed_at }.
    public const string SpecRestore           = "SPEC_RESTORE";
    // Phase 8 PR-L3 — Spec lifecycle Trash (soft-delete). Blocked at service
    // when WO active references still exist (FK ON DELETE RESTRICT defence in
    // depth — soft-delete keeps the row, but Purge would later try to hard-
    // delete and violate the FK). detail JSON: { rev_id, rev_code, spec_code,
    // status }.
    public const string SpecTrash             = "SPEC_TRASH";
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
    // Phase 8 PR #32d — Work Order created from Demo template (operator clicked
    // a Demo card on /workorders). detail JSON: { template_code, wo_no, wo_id,
    // customer_id, product_id, machine_code, target_qty, uom, source: "demo" }.
    // CreateAsync (Phase 6 service) does NOT emit — audit is at controller
    // callsite so service body stays untouched.
    public const string WoCreate              = "WO_CREATE";
    // Phase 8 PR #32c — WO list export (CSV / XLSX). detail JSON:
    // { format, rows, filename, content_length }
    public const string WoExport              = "WO_EXPORT";
    public const string WoFlagsUpdate         = "WO_FLAGS_UPDATE";

    // ───────────────────────────────────────────────────────────────
    // P10.7a-1 — canonical MES audit codes reserved per
    // docs/P10.7-WO-STATE-CONTRACT.md §3.2 + §7.1. Reserving in this
    // PR (additive) so feature branches downstream do not invent
    // divergent names. Alphabetical to keep the diff review clean.
    // Every code below carries the contract-required JSON envelope
    // (wo_id, wo_no, shift_code, from_phase, to_phase, ok) via
    // CCL.MES.Api.Audit.AuditEmitHelper — never raw inline new {}.
    // ───────────────────────────────────────────────────────────────

    /// <summary>QC FQC per-criterion check row. detail JSON:
    /// { wo_id, criterion_key, criterion_value: 'OK'|'NG'|'NA', inspector_id }.</summary>
    public const string FqcCheck              = "FQC_CHECK";
    /// <summary>FQC inspector step-up signed in (4-eye principle).</summary>
    public const string FqcSignin             = "FQC_SIGNIN";
    /// <summary>FQC inspector signed out.</summary>
    public const string FqcSignout            = "FQC_SIGNOUT";
    /// <summary>Idempotency-Key replay attempted with DIFFERENT body —
    /// indicates client UI bug. detail JSON: { key, endpoint_path,
    /// expected_body_sha, received_body_sha, actor_id }.</summary>
    public const string IdempotencyReplay     = "IDEMPOTENCY_REPLAY";
    /// <summary>IPQC per-criterion check row.</summary>
    public const string IpqcCheck             = "IPQC_CHECK";
    /// <summary>IPQC judgement = GO_RUN. detail JSON: { ipqc_user_id,
    /// criteria_summary: {5-entry map} }.</summary>
    public const string IpqcGoRun             = "IPQC_GO_RUN";
    /// <summary>IPQC step-up signed in.</summary>
    public const string IpqcSignin            = "IPQC_SIGNIN";
    /// <summary>IPQC signed out.</summary>
    public const string IpqcSignout           = "IPQC_SIGNOUT";
    /// <summary>IPQC judgement = SPECIAL_ACCEPT (routes WO to QA_PENDING).
    /// detail JSON: { ipqc_user_id, reason (≤500 chars, sanitized) }.</summary>
    public const string IpqcSpecialAccept     = "IPQC_SPECIAL_ACCEPT";
    /// <summary>IPQC judgement = STOP_LINE (rolls WO back to PREPRESS).</summary>
    public const string IpqcStopLine          = "IPQC_STOP_LINE";
    /// <summary>QC OQC per-criterion check row.</summary>
    public const string OqcCheck              = "OQC_CHECK";
    /// <summary>OQC inspector step-up signed in.</summary>
    public const string OqcSignin             = "OQC_SIGNIN";
    /// <summary>OQC signed out.</summary>
    public const string OqcSignout            = "OQC_SIGNOUT";
    /// <summary>PREPRESS row-level cutter check failed.</summary>
    public const string PrepressCutterNg      = "PREPRESS_CUTTER_NG";
    /// <summary>PREPRESS row-level cutter check passed.</summary>
    public const string PrepressCutterOk      = "PREPRESS_CUTTER_OK";
    /// <summary>PREPRESS material row failed. detail JSON:
    /// { bom_line_idx, actual_value, ng_reason_code, ng_note }.</summary>
    public const string PrepressMatNg         = "PREPRESS_MAT_NG";
    /// <summary>PREPRESS material row passed.</summary>
    public const string PrepressMatOk         = "PREPRESS_MAT_OK";
    /// <summary>PREPRESS plate check failed.</summary>
    public const string PrepressPlateNg      = "PREPRESS_PLATE_NG";
    /// <summary>PREPRESS plate check passed.</summary>
    public const string PrepressPlateOk      = "PREPRESS_PLATE_OK";
    /// <summary>QA Manager approves IPQC SPECIAL_ACCEPT.</summary>
    public const string QaApproveSpecial      = "QA_APPROVE_SPECIAL";
    /// <summary>QA Manager rejects IPQC SPECIAL_ACCEPT (rolls back
    /// to PREPRESS).</summary>
    public const string QaRejectSpecial       = "QA_REJECT_SPECIAL";
    /// <summary>Admin / sys-only console or endpoint recovery action
    /// (e.g. /admin-force-phase). detail JSON: { from_phase,
    /// to_phase, reason, sys_user_id }.</summary>
    public const string SysRecovery           = "SYS_RECOVERY";
    /// <summary>Admin / sys cancels a non-terminal WO. detail JSON:
    /// { from_phase, reason, force: true }.</summary>
    public const string WoCancel              = "WO_CANCEL";
    /// <summary>FQC inspector rejected at least 1 criterion (rolls
    /// WO back to PREPRESS).</summary>
    public const string WoFqcReject           = "WO_FQC_REJECT";
    /// <summary>OQC inspector rejected (signed §10.2 answer:
    /// re-inspect by FQC, not full rework).</summary>
    public const string WoOqcReject           = "WO_OQC_REJECT";
    /// <summary>Operator finished a run session. detail JSON:
    /// { qty_good_total, qty_ng_total, sessions_count }.</summary>
    public const string WoRunFinish           = "WO_RUN_FINISH";
    /// <summary>Operator paused running session. detail JSON:
    /// { reason_code, note, session_id }.</summary>
    public const string WoRunPause            = "WO_RUN_PAUSE";
    /// <summary>Operator resumed paused session.</summary>
    public const string WoRunResume           = "WO_RUN_RESUME";
    /// <summary>Operator started a new run session.</summary>
    public const string WoRunStart            = "WO_RUN_START";
    /// <summary>Operator scanned WO QR. State-neutral event — does
    /// NOT change phase; serves as physical-presence trail.
    /// detail JSON: { machine_id, device_id from X-Device-Id header }.</summary>
    public const string WoScan                = "WO_SCAN";
    /// <summary>Setting was aborted by admin/sys; row re-opens fresh.</summary>
    public const string WoSettingAbort        = "WO_SETTING_ABORT";
    /// <summary>Setting timer closed. detail JSON: { op_user_id,
    /// duration_sec }.</summary>
    public const string WoSettingDone         = "WO_SETTING_DONE";
    /// <summary>Setting timer started. detail JSON: { op_user_id,
    /// setting_start_at }.</summary>
    public const string WoSettingStart        = "WO_SETTING_START";
    /// <summary>Optimistic-concurrency conflict — write attempted
    /// against stale RowVersion. detail JSON: { attempted_action,
    /// client_version: base64, server_version: base64, actor_id }.
    /// Emitted by every mutation endpoint when If-Match check fails.</summary>
    public const string WoStateConflict       = "WO_STATE_CONFLICT";
}

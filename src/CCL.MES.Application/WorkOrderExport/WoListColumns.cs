using System.Globalization;
using CCL.MES.Application.Services;
using CCL.MES.Domain;

namespace CCL.MES.Application.WorkOrderExport;

/// <summary>
/// Phase 8 PR #32c — Canonical 12-column SSoT for Work Order list export
/// (CSV + XLSX). Mirrors the card view body fields + Section flag so
/// operators can sort/filter Active vs Closed in Excel post-export.
///
/// Pattern follows <c>SpecListColumns</c> from PR #31c.
/// </summary>
public static class WoListColumns
{
    public static readonly IReadOnlyList<WoListColumn> All = new[]
    {
        new WoListColumn("wo_no",         "WO No",         WoColumnType.Text, 14),
        new WoListColumn("customer",      "Customer",      WoColumnType.Text, 20),
        new WoListColumn("product_code",  "Product Code",  WoColumnType.Text, 18),
        new WoListColumn("product_name",  "Product Name",  WoColumnType.Text, 26),
        new WoListColumn("machine",       "Machine",       WoColumnType.Text, 12),
        new WoListColumn("process",       "Process",       WoColumnType.Text, 18),
        new WoListColumn("target_qty",    "Target Qty",    WoColumnType.Int,  10),
        new WoListColumn("uom",           "UoM",           WoColumnType.Text,  6),
        new WoListColumn("produced_qty",  "Produced Qty",  WoColumnType.Int,  12),
        new WoListColumn("current_step",  "Current Step",  WoColumnType.Text, 16),
        new WoListColumn("status",        "Status",        WoColumnType.Text, 14),
        new WoListColumn("section",       "Section",       WoColumnType.Text,  9),
    };

    /// <summary>
    /// Pre-flatten row → 12 display strings for CSV rendering. Section is
    /// the literal "Active" / "Closed" label resolved from the caller's
    /// split (NOT WoStatus.ToString) so the column matches the card-view
    /// grouping operators see in the UI.
    /// </summary>
    public static string[] ToDisplayCells(WorkOrderCardItem row, string section, CultureInfo culture)
    {
        return new[]
        {
            row.WoNo ?? "",
            row.CustomerName ?? "",
            row.ProductCode ?? "",
            row.ProductName ?? "",
            row.MachineCode ?? "",
            row.ProcessLabel ?? "",
            row.TargetQty.ToString(culture),
            row.Uom ?? "",
            row.ProducedQty.ToString(culture),
            StepDisplay(row.CurrentStep),
            StatusDisplay(row.Status),
            section,
        };
    }

    /// <summary>
    /// Typed projection for Excel — int stays int for native cell type
    /// + numberFormat. Null becomes empty cell.
    /// </summary>
    public static object?[] ToTypedCells(WorkOrderCardItem row, string section)
    {
        return new object?[]
        {
            row.WoNo,
            row.CustomerName,
            row.ProductCode,
            row.ProductName,
            row.MachineCode,
            row.ProcessLabel,
            row.TargetQty,
            row.Uom,
            row.ProducedQty,
            StepDisplay(row.CurrentStep),
            StatusDisplay(row.Status),
            section,
        };
    }

    /// <summary>
    /// 7-step ProcessStepCode → operator-friendly label. Mirrors the
    /// "Current Step" badge text in the planner Table view. NOT i18n'd
    /// (export is locale-agnostic — operators may share the file with
    /// EN-speaking auditors); the in-app Help docs can map labels.
    /// </summary>
    public static string StepDisplay(ProcessStepCode step) => step switch
    {
        ProcessStepCode.PrePressCheck => "Pre-Press",
        ProcessStepCode.OpSetting     => "Setting",
        ProcessStepCode.IpqcApproval  => "IPQC",
        ProcessStepCode.ReadyToRun    => "Ready",
        ProcessStepCode.Running       => "Running",
        ProcessStepCode.Fqc           => "FQC",
        ProcessStepCode.Oqc           => "OQC",
        _                             => step.ToString(),
    };

    public static string StatusDisplay(WoStatus status) => status switch
    {
        WoStatus.Draft      => "Draft",
        WoStatus.InProgress => "In Progress",
        WoStatus.OnHold     => "On Hold",
        WoStatus.Finished   => "Finished",
        WoStatus.Closed     => "Closed",
        WoStatus.Cancelled  => "Cancelled",
        _                   => status.ToString(),
    };
}

public enum WoColumnType
{
    Text,
    Int,
}

public record WoListColumn(string Key, string Label, WoColumnType Type, int WidthCh);

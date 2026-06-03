using CCL.MES.Shared.Hardware;

namespace CCL.MES.Hybrid.Client.Hardware;

/// <summary>
/// Per-platform label printer (Zebra/TSC ZPL, TCP or USB). STUB-only in
/// P10.3 — the interface holds the slot so future printer impls plug in
/// without re-versioning the abstraction. <see cref="IsAvailableAsync"/>
/// always reports <c>"not_implemented"</c>. <see cref="PrintZplAsync"/>
/// throws <see cref="NotImplementedException"/>.
///
/// <para>
/// Lights up when a concrete MES workflow files a print request —
/// see plan §1 priority cut. Routing card printing today lives in v1.2
/// (Ops Control), not here.
/// </para>
/// </summary>
public interface ILabelPrinterService
{
    /// <summary>
    /// Probe whether print is possible. Returns
    /// <see cref="HardwareAvailability.NotImplemented"/> in W1.
    /// </summary>
    Task<HardwareAvailability> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// Send a ZPL command stream to the configured printer. ZPL is the
    /// Zebra command language; TSC printers accept a similar dialect
    /// the future impl normalises. W1 throws.
    /// </summary>
    Task PrintZplAsync(string zpl, CancellationToken ct = default);
}

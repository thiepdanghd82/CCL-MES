using CCL.MES.Shared.Hardware;

namespace CCL.MES.Hybrid.Client.Hardware;

/// <summary>
/// Per-platform weighing scale (serial-port USB, RS-232 via adapter).
/// STUB-only in P10.3 — same justification as
/// <see cref="ILabelPrinterService"/>. Lights up when a concrete
/// weigh-and-record workflow (ink dispensing, raw-mat receiving) is
/// greenlit and the platform impls are written against a known scale
/// protocol family (Mettler-Toledo MT-SICS / Ohaus SCP / A&amp;D
/// SE-style — first stripe is the most common in CCL Vietnam).
/// </summary>
public interface IWeighScaleService
{
    /// <summary>Probe — reports <c>"not_implemented"</c> in W1.</summary>
    Task<HardwareAvailability> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// Stream live weight samples in grams. Operator typically
    /// observes the modal until weight stabilises within an
    /// epsilon, then taps Confirm. Out of scope for P10.3.
    /// </summary>
    IAsyncEnumerable<decimal> WeightStreamGramsAsync(CancellationToken ct);
}

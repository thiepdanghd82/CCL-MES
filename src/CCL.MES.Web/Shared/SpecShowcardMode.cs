namespace CCL.MES.Web.Shared;

/// <summary>
/// Phase 8 PR-B — Render mode cho <see cref="SpecShowcard"/> component.
///
///   • Full     — full-page detail (9 section đầy đủ + doc header + audit log)
///   • Compact  — modal peek (Identity + Compliance + Product Info mini + first N rows + audit subset)
///   • Preview  — CreateSpecModal live preview during edit (Compact - audit since records chưa tồn tại)
/// </summary>
public enum SpecShowcardMode
{
    Full = 0,
    Compact = 1,
    Preview = 2,
}

namespace CCL.MES.Shared.RunningSurface;

/// <summary>
/// P10.7f follow-up — derive which SETTING sub-tabs (Print / Cut) a WO needs
/// from its routing plan's leg kinds. Pure; no I/O.
///
/// Rules:
///   - <c>PRINT</c> or <c>PRINT_CUT</c> present → the Print tab applies.
///   - <c>CUT</c>   or <c>PRINT_CUT</c> present → the Cut tab applies.
///   - Nothing derivable (empty plan / unmapped routing / only TAPE|ASSEMBLY
///     silkscreen semis) → BOTH true. We never hide a tab we are unsure about:
///     the operator keeps full checklist coverage, matching the pre-P10.7f
///     "both tabs always" behaviour. This is the safe fallback the SETTING
///     dashboard relies on for the common 1-leg WO whose legs are never
///     materialised (RoutingController forks only at ≥2 legs).
/// </summary>
public static class SettingProcessScope
{
    public static (bool HasPrint, bool HasCut) FromLegKinds(IEnumerable<string> legKinds)
    {
        var print = false;
        var cut = false;
        foreach (var raw in legKinds)
        {
            var k = (raw ?? string.Empty).Trim();
            if (k.Equals("PRINT", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("PRINT_CUT", StringComparison.OrdinalIgnoreCase))
                print = true;
            if (k.Equals("CUT", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("PRINT_CUT", StringComparison.OrdinalIgnoreCase))
                cut = true;
        }

        // Unknown → both. Never leave an operator with zero applicable tabs.
        return (!print && !cut) ? (true, true) : (print, cut);
    }
}

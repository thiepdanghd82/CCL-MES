using CCL.MES.Hybrid.Client.Qms;
using Microsoft.Maui.Storage;

namespace CCL.MES.Hybrid.Services;

/// <summary>Ghi ngưỡng Pareto IQC vào MAUI Preferences — sống qua đổi tab và relaunch.</summary>
public sealed class MauiIqcDashboardPrefs : IIqcDashboardPrefs
{
    internal const string Key = "cclmes.hybrid.iqc.dash.thr.v1";

    public int GetThresholdPct()
    {
        var n = Preferences.Default.Get(Key, 80);
        return Math.Clamp(n, 50, 100);
    }

    public void SetThresholdPct(int pct)
        => Preferences.Default.Set(Key, Math.Clamp(pct, 50, 100));
}

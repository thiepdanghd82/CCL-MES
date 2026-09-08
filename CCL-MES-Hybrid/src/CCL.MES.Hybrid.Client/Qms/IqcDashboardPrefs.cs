namespace CCL.MES.Hybrid.Client.Qms;

/// <summary>
/// Ngưỡng Pareto IQC Dashboard — nhớ trên máy (đổi tab / tắt app không về 80%).
/// MAUI host ghi Preferences; test dùng bản in-memory.
/// </summary>
public interface IIqcDashboardPrefs
{
    int GetThresholdPct();
    void SetThresholdPct(int pct);
}

public sealed class InMemoryIqcDashboardPrefs : IIqcDashboardPrefs
{
    private int _pct = 80;
    private readonly object _lock = new();

    public int GetThresholdPct()
    {
        lock (_lock) return _pct;
    }

    public void SetThresholdPct(int pct)
    {
        lock (_lock) _pct = Math.Clamp(pct, 50, 100);
    }
}

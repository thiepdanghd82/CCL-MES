namespace CCL.MES.Hybrid.Client.Files;

/// <summary>
/// Stub fallback when no real impl is registered (tests / non-MAUI
/// hosts). Always returns null so callers degrade to operator-cancelled
/// path instead of crashing on a null DI resolution.
/// </summary>
public sealed class StubFilePickerService : IFilePickerService
{
    public Task<PickedFile?> PickXlsxAsync(CancellationToken ct = default) =>
        Task.FromResult<PickedFile?>(null);
}

#if MACCATALYST || IOS
using Foundation;
using UIKit;

namespace CCL.MES.Hybrid.Platforms.MacCatalyst;

/// <summary>
/// Opens the system Settings page where the operator can re-grant
/// camera access. Catalyst inherits the iOS
/// <c>UIApplication.OpenSettingsUrlString</c> URL which deep-links to
/// the app's own privacy preferences inside System Settings. If that
/// fails (older OS, sandbox quirk), falls back to the macOS-specific
/// <c>x-apple.systempreferences</c> URL scheme that points at
/// Privacy → Camera directly.
///
/// <para>
/// No silent fallback (P10.2 lesson). The bool result tells the caller
/// whether ANY settings URL was opened so the UI can show "Đã mở
/// System Settings — bật quyền rồi quay lại app" vs "Không mở được
/// Settings, vào thủ công Apple Menu → System Settings → Privacy &amp;
/// Security → Camera".
/// </para>
/// </summary>
internal static class CameraSettingsHelper
{
    public static async Task<bool> TryOpenAsync()
    {
        var app = UIApplication.SharedApplication;
        var primary = new NSUrl(UIApplication.OpenSettingsUrlString);
        if (app.CanOpenUrl(primary))
        {
            try { return await OpenAsync(primary); } catch { /* fall through */ }
        }
        var fallback = new NSUrl("x-apple.systempreferences:com.apple.preference.security?Privacy_Camera");
        if (app.CanOpenUrl(fallback))
        {
            try { return await OpenAsync(fallback); } catch { /* fall through */ }
        }
        return false;
    }

    private static Task<bool> OpenAsync(NSUrl url)
    {
        var tcs = new TaskCompletionSource<bool>();
        UIApplication.SharedApplication.OpenUrl(url, new UIApplicationOpenUrlOptions(), success =>
        {
            tcs.TrySetResult(success);
        });
        return tcs.Task;
    }
}
#endif

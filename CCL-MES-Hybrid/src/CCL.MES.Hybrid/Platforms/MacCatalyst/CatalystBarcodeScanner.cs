#if MACCATALYST || IOS
using System.Runtime.CompilerServices;
using AVFoundation;
using CCL.MES.Hybrid.Client.Hardware;
using CCL.MES.Shared.Hardware;
using Foundation;
using Microsoft.Extensions.Options;
using UIKit;

namespace CCL.MES.Hybrid.Platforms.MacCatalyst;

/// <summary>
/// Mac Catalyst impl of <see cref="IBarcodeScannerService"/>. Runs
/// an <see cref="AVCaptureSession"/> in a modal
/// <see cref="CatalystScannerViewController"/> presented from the
/// current key window's root view controller.
///
/// <para>
/// Permission flow follows the P10.2 "no silent fail" lesson exactly:
/// every denial path surfaces a structured <see cref="HardwareAvailability"/>
/// with operator-actionable Vietnamese text. The /hardware page test
/// panel pairs the banner with a "Mở Settings" button that fires
/// <see cref="CameraSettingsHelper.TryOpenAsync"/>.
/// </para>
/// <para>
/// Adhoc dev-build gotcha (carried lesson from P10.2 Keychain
/// MissingEntitlement): on builds with no provisioning profile +
/// no entitlements, some Catalyst OS versions return
/// <see cref="AVAuthorizationStatus.Denied"/> immediately on the
/// first RequestAccess call without showing the consent dialog. We
/// catch + log that scenario explicitly so future debugging finds
/// it instantly instead of staring at a "permission_denied" with no
/// trace. Signed prod builds with entitlements behave normally.
/// </para>
/// </summary>
public sealed class CatalystBarcodeScanner : IBarcodeScannerService
{
    private readonly HardwareOptions _opts;

    // AVMetadataObjectTypes we ask AVFoundation to decode. Covers the
    // formats CCL Vietnam typically uses on production floor (QR for
    // WO + Sample tracking; Code128 + Code39 for vendor labels;
    // EAN/UPC for retail SKUs; DataMatrix for IPQC; PDF417 + Aztec
    // for completeness). Adding a format here is the ONLY place new
    // symbology lights up — the BarcodeFormatMapper already handles
    // every type listed.
    private static readonly AVMetadataObjectType[] SupportedTypes =
    {
        AVMetadataObjectType.QRCode,
        AVMetadataObjectType.Code128Code,
        AVMetadataObjectType.Code39Code,
        AVMetadataObjectType.Code39Mod43Code,
        AVMetadataObjectType.Code93Code,
        AVMetadataObjectType.EAN13Code,
        AVMetadataObjectType.EAN8Code,
        AVMetadataObjectType.UPCECode,
        AVMetadataObjectType.DataMatrixCode,
        AVMetadataObjectType.PDF417Code,
        AVMetadataObjectType.AztecCode,
        AVMetadataObjectType.ITF14Code,
        AVMetadataObjectType.Interleaved2of5Code,
    };

    public CatalystBarcodeScanner(IOptions<HardwareOptions> opts)
    {
        _opts = opts.Value;
    }

    public async Task<HardwareAvailability> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!_opts.ScanEnabled)
            return HardwareAvailability.FeatureDisabled();

        var status = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Video);
        return status switch
        {
            AVAuthorizationStatus.Authorized    => HardwareAvailability.Ok(),
            AVAuthorizationStatus.NotDetermined => HardwareAvailability.Ok(), // probe is cheap; let ScanOnce trigger the consent dialog
            AVAuthorizationStatus.Denied
            or AVAuthorizationStatus.Restricted => HardwareAvailability.PermissionDenied("camera"),
            _ => HardwareAvailability.Ok(),
        };
    }

    public async Task<ScanResult?> ScanOnceAsync(CancellationToken ct = default)
    {
        if (!_opts.ScanEnabled) return null;

        // Resolve permission BEFORE constructing the AVCaptureSession.
        // RequestAccess returns synchronously when already determined.
        var status = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Video);
        if (status == AVAuthorizationStatus.NotDetermined)
        {
            try
            {
                var granted = await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video);
                if (!granted) return null;
            }
            catch (Exception ex)
            {
                // Adhoc-build gotcha — leave a breadcrumb for ops.
                Console.WriteLine($"[CatalystScanner] RequestAccessAsync threw on probable-adhoc build: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }
        else if (status is AVAuthorizationStatus.Denied or AVAuthorizationStatus.Restricted)
        {
            // Caller (UI panel) already surfaced the banner via
            // IsAvailableAsync(); returning null from ScanOnce here
            // matches the "user cancelled" path so the UI can stay
            // simple.
            return null;
        }

        var topVc = GetTopViewController();
        if (topVc is null) return null;

        var tcs = new TaskCompletionSource<(string? RawType, string? Code)?>();
        CatalystScannerViewController? modal = null;
        try
        {
            modal = new CatalystScannerViewController
            {
                Completion = tcs,
                MetadataObjectTypes = SupportedTypes,
                ModalPresentationStyle = UIModalPresentationStyle.FullScreen,
            };

            await topVc.PresentViewControllerAsync(modal, animated: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CatalystScanner] PresentViewController failed: {ex}");
            modal?.Dispose();
            return null;
        }

        // Allow the caller's CT to cancel the scan — sets the result
        // to null and lets the modal dismiss itself.
        using var reg = ct.Register(() =>
        {
            tcs.TrySetResult(null);
            try { modal?.DismissViewController(true, null); } catch { /* already gone */ }
        });

        var result = await tcs.Task;
        if (result is null) return null;

        var rawType = result.Value.RawType;
        var code = result.Value.Code;
        if (string.IsNullOrEmpty(code)) return null;

        var format = BarcodeFormatMapper.FromAVRawIdentifier(rawType);
        format = BarcodeFormatMapper.DisambiguateEAN13UpcA(format, code);

        return new ScanResult(
            Code: code,
            Format: format,
            CapturedAt: DateTimeOffset.Now,
            SourceDevice: modal?.SourceDeviceLabel);
    }

    public async IAsyncEnumerable<ScanResult> ScanStreamAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        // W2 ships ScanOnce only. Stream surface stays empty (matches
        // the stub contract) until a workflow asks for batch decode.
        await Task.CompletedTask;
        yield break;
    }

    private static UIViewController? GetTopViewController()
    {
        // Catalyst has a single active scene; walk to its key window's
        // root + dive into any modal stack already presented (e.g.
        // the BlazorWebView's host view controller).
        var keyWindow = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(s => s.Windows)
            .FirstOrDefault(w => w.IsKeyWindow);
        var vc = keyWindow?.RootViewController;
        while (vc?.PresentedViewController is not null)
            vc = vc.PresentedViewController;
        return vc;
    }
}
#endif

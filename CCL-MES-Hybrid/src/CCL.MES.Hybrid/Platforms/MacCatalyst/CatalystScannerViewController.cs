#if MACCATALYST || IOS
using AVFoundation;
using CoreFoundation;
using CoreGraphics;
using Foundation;
using UIKit;

namespace CCL.MES.Hybrid.Platforms.MacCatalyst;

/// <summary>
/// Full-screen modal that runs an <see cref="AVCaptureSession"/> with
/// metadata-output decoding. Surfaces the first scanned payload back
/// to the caller via a <see cref="TaskCompletionSource"/>. Owns the
/// UI: navy background, centred camera preview, scan reticle, Đóng
/// button, plus a footer line with the source-device label so the
/// operator can confirm which camera the app picked.
/// </summary>
internal sealed class CatalystScannerViewController : UIViewController, IAVCaptureMetadataOutputObjectsDelegate
{
    // Configured by the caller before PresentViewController.
    public required TaskCompletionSource<(string? RawType, string? Code)?> Completion { get; init; }
    public required AVMetadataObjectType[] MetadataObjectTypes { get; init; }

    private AVCaptureSession? _session;
    private AVCaptureVideoPreviewLayer? _previewLayer;
    private UIView? _reticle;
    private UILabel? _statusLabel;
    private bool _completed;

    public string? SourceDeviceLabel { get; private set; }

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.FromRGB(0x16, 0x1b, 0x22); // navy from app.css

        try
        {
            SetupCaptureSession();
            SetupOverlays();
        }
        catch (Exception ex)
        {
            // Bubble the failure to the caller — the modal dismisses
            // itself and the caller gets a structured null + error
            // banner on the test panel.
            CompleteAndDismiss((null, null));
            Console.WriteLine($"[CatalystScanner] ViewDidLoad failure: {ex}");
        }
    }

    public override void ViewWillAppear(bool animated)
    {
        base.ViewWillAppear(animated);
        _session?.StartRunning();
    }

    public override void ViewWillDisappear(bool animated)
    {
        base.ViewWillDisappear(animated);
        _session?.StopRunning();
    }

    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        if (_previewLayer is not null)
            _previewLayer.Frame = View!.Bounds;
        LayoutReticleAndChrome();
    }

    private void SetupCaptureSession()
    {
        var device = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);
        if (device is null)
        {
            SourceDeviceLabel = "no-camera";
            CompleteAndDismiss((null, null));
            return;
        }

        // SourceDevice label: best-effort — Catalyst returns
        // "FaceTime HD Camera" or "Built-in" depending on bind.
        SourceDeviceLabel = device.LocalizedName ?? device.UniqueID ?? "camera";

        var input = AVCaptureDeviceInput.FromDevice(device, out var err);
        if (input is null || err is not null)
        {
            Console.WriteLine($"[CatalystScanner] AVCaptureDeviceInput failed: {err?.LocalizedDescription}");
            CompleteAndDismiss((null, null));
            return;
        }

        _session = new AVCaptureSession();
        if (!_session.CanAddInput(input))
        {
            CompleteAndDismiss((null, null));
            return;
        }
        _session.AddInput(input);

        var metaOut = new AVCaptureMetadataOutput();
        if (!_session.CanAddOutput(metaOut))
        {
            CompleteAndDismiss((null, null));
            return;
        }
        _session.AddOutput(metaOut);
        // Filter to supported types so decode work doesn't waste cycles
        // on QC2 / face / body-pose detection.
        metaOut.MetadataObjectTypes = AggregateMetadataMask();
        metaOut.SetDelegate(this, DispatchQueue.MainQueue);

        _previewLayer = new AVCaptureVideoPreviewLayer(_session)
        {
            VideoGravity = AVLayerVideoGravity.ResizeAspectFill,
            Frame = View!.Bounds,
        };
        View.Layer.AddSublayer(_previewLayer);
    }

    private AVMetadataObjectType AggregateMetadataMask()
    {
        var mask = (AVMetadataObjectType)0;
        foreach (var t in MetadataObjectTypes)
            mask |= t;
        return mask;
    }

    private void SetupOverlays()
    {
        // Reticle — semi-transparent rounded rect.
        _reticle = new UIView
        {
            BackgroundColor = UIColor.Clear,
            Layer =
            {
                BorderColor = UIColor.FromRGB(0x2f, 0x80, 0xed).CGColor, // brand
                BorderWidth = 3,
                CornerRadius = 14,
            },
        };
        View!.AddSubview(_reticle);

        // Title.
        var title = new UILabel
        {
            Text = "Quét mã",
            TextColor = UIColor.White,
            Font = UIFont.BoldSystemFontOfSize(20),
            TextAlignment = UITextAlignment.Center,
        };
        title.SizeToFit();
        title.Tag = 1001;
        View.AddSubview(title);

        // Status line under reticle (source-device + cancel hint).
        _statusLabel = new UILabel
        {
            Text = $"Nguồn: {SourceDeviceLabel ?? "—"}",
            TextColor = UIColor.FromRGBA(255, 255, 255, 200),
            Font = UIFont.SystemFontOfSize(13),
            TextAlignment = UITextAlignment.Center,
        };
        _statusLabel.SizeToFit();
        View.AddSubview(_statusLabel);

        // Đóng button.
        var cancel = UIButton.FromType(UIButtonType.System);
        cancel.SetTitle("Đóng", UIControlState.Normal);
        cancel.SetTitleColor(UIColor.White, UIControlState.Normal);
        cancel.Layer.BorderColor = UIColor.FromRGBA(255, 255, 255, 120).CGColor;
        cancel.Layer.BorderWidth = 1;
        cancel.Layer.CornerRadius = 6;
        cancel.ContentEdgeInsets = new UIEdgeInsets(8, 18, 8, 18);
        cancel.TouchUpInside += (_, _) => CompleteAndDismiss(null);
        cancel.Tag = 1002;
        View.AddSubview(cancel);
    }

    private void LayoutReticleAndChrome()
    {
        if (View is null) return;
        var bounds = View.Bounds;
        var side = (nfloat)Math.Min(bounds.Width, bounds.Height) * 0.55f;
        var rx = (bounds.Width - side) / 2;
        var ry = (bounds.Height - side) / 2;
        if (_reticle is not null)
            _reticle.Frame = new CGRect(rx, ry, side, side);

        if (View.ViewWithTag(1001) is UILabel title)
        {
            var w = (nfloat)320;
            title.Frame = new CGRect((bounds.Width - w) / 2, 56, w, 28);
        }
        if (_statusLabel is not null)
        {
            var w = (nfloat)420;
            _statusLabel.Frame = new CGRect((bounds.Width - w) / 2, ry + side + 18, w, 22);
        }
        if (View.ViewWithTag(1002) is UIButton cancel)
        {
            cancel.SizeToFit();
            var s = cancel.Frame.Size;
            cancel.Frame = new CGRect((bounds.Width - s.Width) / 2,
                bounds.Height - s.Height - 40,
                s.Width, s.Height);
        }
    }

    [Export("captureOutput:didOutputMetadataObjects:fromConnection:")]
    public void DidOutputMetadataObjects(AVCaptureOutput captureOutput,
        NSObject[] metadataObjects, AVCaptureConnection connection)
    {
        if (_completed || metadataObjects.Length == 0) return;
        foreach (var raw in metadataObjects)
        {
            if (raw is AVMetadataMachineReadableCodeObject code &&
                !string.IsNullOrEmpty(code.StringValue))
            {
                var typeId = code.Type.ToString();
                CompleteAndDismiss((typeId, code.StringValue));
                return;
            }
        }
    }

    private void CompleteAndDismiss((string? RawType, string? Code)? value)
    {
        if (_completed) return;
        _completed = true;
        _session?.StopRunning();
        DismissViewController(true, () => Completion.TrySetResult(value));
    }
}
#endif

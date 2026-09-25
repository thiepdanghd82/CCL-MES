using CCL.MES.Hybrid.Platforms.MacCatalyst;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace CCL.MES.Hybrid;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	// ── Tab / Shift+Tab — dotnet/maui#13934 ──────────────────────────────
	// ĐO 2026-09-25 (đầu dò keydown trên app thật): chữ, ArrowDown, Enter đều
	// tới JS; phím Tab thì KHÔNG MỘT LẦN NÀO — UIKit nuốt nó ở tầng native
	// trước khi WKWebView nhận. Nên MacCatalystKeyboardFix (JS) không bao giờ
	// thấy Tab, và con trỏ kẹt trong ô nhập (ô Tên đăng nhập → Mật khẩu).
	//
	// Bắt Tab bằng UIKeyCommand với WantsPriorityOverSystemBehavior (giành
	// trước hành vi hệ thống), rồi giao cho window.cclFocusMove trong trang —
	// logic chọn ô kế vẫn nằm MỘT chỗ ở MacCatalystKeyboardFix.razor.
	// AppDelegate nằm cuối responder chain nên key command này phủ mọi màn.
	private static readonly UIKeyCommand[] FocusKeyCommands = BuildFocusKeyCommands();

	private static UIKeyCommand[] BuildFocusKeyCommands()
	{
		var next = UIKeyCommand.Create(new NSString("\t"), 0, new Selector("cclFocusNext:"));
		var prev = UIKeyCommand.Create(new NSString("\t"), UIKeyModifierFlags.Shift, new Selector("cclFocusPrev:"));
		next.WantsPriorityOverSystemBehavior = true;
		prev.WantsPriorityOverSystemBehavior = true;
		return new[] { next, prev };
	}

	public override UIKeyCommand[] KeyCommands => FocusKeyCommands;

	[Export("cclFocusNext:")]
	public void CclFocusNext(UIKeyCommand cmd) => MoveWebFocus(shift: false);

	[Export("cclFocusPrev:")]
	public void CclFocusPrev(UIKeyCommand cmd) => MoveWebFocus(shift: true);

	private static void MoveWebFocus(bool shift)
		=> CatalystWebViewHolder.WebView?.EvaluateJavaScript(
			$"window.cclFocusMove && window.cclFocusMove({(shift ? "true" : "false")})",
			(_, err) => { if (err is not null) Console.WriteLine("[keyboard-fix] cclFocusMove FAIL: " + err.LocalizedDescription); });
}

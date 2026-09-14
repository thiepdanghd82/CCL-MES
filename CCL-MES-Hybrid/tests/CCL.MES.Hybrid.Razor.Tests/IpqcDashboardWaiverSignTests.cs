using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.IpqcReview;
using CCL.MES.Shared.ReasonCodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// Hộp KÝ DUYỆT waiver vật tư (Thiệp chốt 2026-09-14): bấm "Kỹ sư phê duyệt"
/// KHÔNG gửi thẳng nữa — mở hộp xác nhận tài khoản + mật khẩu, để hồ sơ đứng
/// tên đúng kỹ sư đã duyệt.
///
/// <para>Đây là surface GIAO DỊCH (điền → xác nhận → đóng) nên dùng
/// <c>&lt;Modal&gt;</c> căn giữa, KHÔNG phải FloatingWindow — theo bảng quyết
/// định của skill <c>cmes-floating-showcard</c>.</para>
/// </summary>
public sealed class IpqcDashboardWaiverSignTests : TestContext
{
    public IpqcDashboardWaiverSignTests()
    {
        Services.AddSingleton<ICclApiClient>(new RecordingApi());
        var session = new StubAuthSession();
        session.SetUser("admin-session", "Admin");   // máy chung đăng nhập bằng admin
        Services.AddSingleton<CCL.MES.Hybrid.Client.Auth.IAuthSession>(session);
        Services.AddI18n();
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        this.AddTestAuthorization().SetAuthorized("admin-session");
    }

    private static IpqcView View() => new()
    {
        WoId = 59, WoNo = "WO-TEST-02", MesPhase = "IPQC_WAIT", ETag = "v1",
        ResolvedLines = "LABEL",
        Items = new[]
        {
            new IpqcViewItem { ItemKey = "LBL-A1", ProcessLine = "LABEL", GroupLabel = "A·Ngoại quan",
                Label = "Bẩn", AcceptanceCriteria = "Không bẩn", Status = "Ok", DefectCode = "DIRT" },
        },
    };

    /// <summary>Một dòng vật tư LỆCH đã xác nhận ⇒ đang chờ kỹ sư ký.</summary>
    private static IpqcMaterialSystemView Material() => new()
    {
        WoId = 59, WoNo = "WO-TEST-02", MesPhase = "IPQC_WAIT", ETag = "v1",
        AnyPendingWaiver = true,
        Rows = new[]
        {
            new IpqcMaterialRow
            {
                BomLineIdx = 0, MaterialCode = "30120406", Status = "Ok",
                IsDivergent = true, DivergenceApprovalStatus = "PendingEngineer",
            },
        },
    };

    private static List<ReasonCodeOption> Scraps() => new()
    {
        new() { Code = "DIRT", LabelEn = "Dirt", LabelVi = "Bẩn", Kind = "Scrap", Sort = 1 },
    };

    private IRenderedComponent<IpqcDashboard> Render()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(View());
        api.IpqcMaterialSystemImpl = (_, _) => Task.FromResult(Material());
        return RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 59L)
            .Add(d => d.ScrapReasons, Scraps()));
    }

    [Fact]
    public void Bam_phe_duyet_thi_MO_HOP_KY_chu_khong_gui_thang()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var cut = Render();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-material-row-0-waiver-reason']")));
        cut.Find("[data-testid='ipqc-material-row-0-waiver-reason']")
           .Input("Lô thay thế đã kiểm");
        cut.Find("[data-testid='ipqc-material-row-0-waiver-approve']").Click();

        cut.WaitForAssertion(() =>
        {
            // Hộp ký hiện ra…
            Assert.NotNull(cut.Find("[data-testid='ipqc-waiver-sign-user']"));
            Assert.NotNull(cut.Find("[data-testid='ipqc-waiver-sign-pwd']"));
            // …và CHƯA gửi gì lên máy chủ.  ← đỏ nếu bấm là gửi thẳng như trước
            Assert.Empty(api.PostIpqcMaterialApproveDivergenceCalls);
        });
    }

    [Fact]
    public void Ky_xong_thi_gui_kem_tai_khoan_va_mat_khau_nguoi_ky()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var cut = Render();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-material-row-0-waiver-reason']")));
        cut.Find("[data-testid='ipqc-material-row-0-waiver-reason']").Input("Lô thay thế đã kiểm");
        cut.Find("[data-testid='ipqc-material-row-0-waiver-approve']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-waiver-sign-user']")));
        cut.Find("[data-testid='ipqc-waiver-sign-user']").Input("ky-su-nam");
        cut.Find("[data-testid='ipqc-waiver-sign-pwd']").Input("mat-khau-cua-nam");
        cut.Find("[data-testid='ipqc-waiver-sign-confirm']").Click();

        cut.WaitForAssertion(() =>
        {
            var call = Assert.Single(api.PostIpqcMaterialApproveDivergenceCalls);
            Assert.Equal(0, call.BomLineIdx);
            Assert.Equal("Approve", call.Req.Outcome);
            Assert.Equal("Lô thay thế đã kiểm", call.Req.Reason);
            // Người ký KHÁC người đang đăng nhập — đó là cả mục đích.
            Assert.Equal("ky-su-nam", call.Req.SignerUsername);
            Assert.Equal("mat-khau-cua-nam", call.Req.SignerPassword);
        });
    }

    [Fact]
    public void Dong_hop_ky_thi_XOA_mat_khau_khoi_bo_nho()
    {
        // Máy dùng chung: để mật khẩu nằm lại là người sau bấm nút sẽ ký bằng
        // tài khoản người trước.
        var cut = Render();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-material-row-0-waiver-reason']")));
        cut.Find("[data-testid='ipqc-material-row-0-waiver-reason']").Input("x");
        cut.Find("[data-testid='ipqc-material-row-0-waiver-approve']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-waiver-sign-pwd']")));
        cut.Find("[data-testid='ipqc-waiver-sign-pwd']").Input("bi-mat");
        cut.Find("[data-testid='ipqc-waiver-sign-cancel']").Click();

        // Mở lại: ô mật khẩu phải TRỐNG.
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='ipqc-waiver-sign-pwd']")));
        cut.Find("[data-testid='ipqc-material-row-0-waiver-approve']").Click();
        cut.WaitForAssertion(() =>
        {
            var pwd = cut.Find("[data-testid='ipqc-waiver-sign-pwd']");
            Assert.True(string.IsNullOrEmpty(pwd.GetAttribute("value")));
        });
    }
}

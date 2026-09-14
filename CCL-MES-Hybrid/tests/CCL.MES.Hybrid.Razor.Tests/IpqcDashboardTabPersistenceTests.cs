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
/// 2026-09-14 (Thiệp báo từ chuyền) — xác nhận một hạng mục ở tab B hoặc D thì
/// màn hình VĂNG về tab A.
///
/// <para>Nguyên nhân: <c>ReloadAsync()</c> đặt lại <c>_activeProcess</c> +
/// <c>_activeTab</c> về mặc định một cách VÔ ĐIỀU KIỆN, mà nó chạy sau MỖI lần
/// ghi một hạng mục. Với 14 hạng mục trải trên 3 tab, người đứng máy phải bấm
/// lại tab sau từng ô — và tệ hơn: ô kế tiếp họ định chấm đã không còn ở chỗ
/// mắt đang nhìn, rất dễ chấm nhầm hàng.</para>
///
/// <para>Mặc định chỉ được áp khi lựa chọn hiện tại KHÔNG CÒN tồn tại (đổi WO,
/// hoặc công đoạn/tab vừa hết hạng mục).</para>
/// </summary>
public sealed class IpqcDashboardTabPersistenceTests : TestContext
{
    private const string TabA = "A·Ngoại quan";
    private const string TabB = "B·Kích thước";

    public IpqcDashboardTabPersistenceTests()
    {
        Services.AddSingleton<ICclApiClient>(new RecordingApi());
        var session = new StubAuthSession();
        session.SetUser("qc-user", "QC");
        Services.AddSingleton<CCL.MES.Hybrid.Client.Auth.IAuthSession>(session);
        Services.AddI18n();
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        this.AddTestAuthorization().SetAuthorized("qc-user");
    }

    /// <summary>Một công đoạn (LABEL → IN), HAI tab A và B — đúng hình dạng đã
    /// làm vỡ việc trên chuyền.</summary>
    private static IpqcView TwoTabs(string b1Status) => new()
    {
        WoId = 59, WoNo = "WO-TEST-02", MesPhase = "IPQC_WAIT", ETag = "v1",
        ResolvedLines = "LABEL",
        Items = new[]
        {
            new IpqcViewItem { ItemKey = "LBL-A1", ProcessLine = "LABEL", GroupLabel = TabA,
                Label = "Bẩn / đốm / chấm", AcceptanceCriteria = "Không bẩn", Status = "Pending", DefectCode = "DIRT" },
            new IpqcViewItem { ItemKey = "LBL-B1", ProcessLine = "LABEL", GroupLabel = TabB,
                Label = "Kích thước tổng thể", AcceptanceCriteria = "Trong dung sai", Status = b1Status, DefectCode = "DIM" },
            new IpqcViewItem { ItemKey = "LBL-B2", ProcessLine = "LABEL", GroupLabel = TabB,
                Label = "Bước nhảy", AcceptanceCriteria = "Đúng bước", Status = "Pending", DefectCode = "STEP" },
        },
    };

    private static List<ReasonCodeOption> Scraps() => new()
    {
        new() { Code = "DIRT", LabelEn = "Dirt", LabelVi = "Bẩn", Kind = "Scrap", Sort = 1 },
        new() { Code = "DIM", LabelEn = "Dim", LabelVi = "Kích thước", Kind = "Scrap", Sort = 2 },
        new() { Code = "STEP", LabelEn = "Step", LabelVi = "Bước", Kind = "Scrap", Sort = 3 },
    };

    [Fact]
    public void Xac_nhan_o_tab_B_thi_KHONG_duoc_vang_ve_tab_A()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        // Lần nạp đầu: B1 còn Pending. Sau khi ghi OK, server trả view mới với
        // B1 = Ok — đúng như đời thật, và ReloadAsync sẽ chạy với view mới ấy.
        var confirmed = false;
        api.IpqcViewImpl = (_, _) => Task.FromResult(TwoTabs(confirmed ? "Ok" : "Pending"));

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 59L)
            .Add(d => d.ScrapReasons, Scraps()));

        // Mặc định đứng ở tab A.
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-item-LBL-A1']")));

        // Người kiểm chuyển sang tab B.
        cut.Find($"[data-testid='ipqc-tab-{TabB}']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='ipqc-item-LBL-B1']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-item-LBL-A1']"));
        });

        // Chấm OK một hạng mục của tab B → ReloadAsync chạy.
        confirmed = true;
        cut.Find("[data-testid='ipqc-item-LBL-B1-ok']").Click();

        cut.WaitForAssertion(() =>
        {
            // ← đỏ nếu ReloadAsync đặt lại tab: LBL-B2 biến mất, LBL-A1 hiện ra
            Assert.NotNull(cut.Find("[data-testid='ipqc-item-LBL-B2']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-item-LBL-A1']"));
        });
    }

    [Fact]
    public void Tab_dang_dung_BIEN_MAT_thi_moi_roi_ve_mac_dinh()
    {
        // Mặt còn lại của cùng một luật: giữ lựa chọn KHÔNG có nghĩa là bám vào
        // một tab không còn tồn tại. Nạp lại mà view chỉ còn tab A thì phải rơi
        // về A, không được hiện một danh sách rỗng.
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var shrunk = false;
        api.IpqcViewImpl = (_, _) => Task.FromResult(shrunk
            ? new IpqcView
            {
                WoId = 59, WoNo = "WO-TEST-02", MesPhase = "IPQC_WAIT", ETag = "v2",
                ResolvedLines = "LABEL",
                Items = new[]
                {
                    new IpqcViewItem { ItemKey = "LBL-A1", ProcessLine = "LABEL", GroupLabel = TabA,
                        Label = "Bẩn / đốm / chấm", AcceptanceCriteria = "Không bẩn",
                        Status = "Pending", DefectCode = "DIRT" },
                },
            }
            : TwoTabs("Pending"));

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 59L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find($"[data-testid='ipqc-tab-{TabB}']")));
        cut.Find($"[data-testid='ipqc-tab-{TabB}']").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-item-LBL-B1']")));

        shrunk = true;
        cut.Find("[data-testid='ipqc-item-LBL-B1-ok']").Click();

        cut.WaitForAssertion(() =>
        {
            // Tab B không còn ⇒ rơi về A và thấy hạng mục, KHÔNG phải màn trống.
            Assert.NotNull(cut.Find("[data-testid='ipqc-item-LBL-A1']"));
            Assert.Empty(cut.FindAll($"[data-testid='ipqc-tab-{TabB}']"));
        });
    }
}

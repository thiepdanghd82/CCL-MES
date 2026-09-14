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
/// 2026-09-14 — một WO chỉ có MỘT <c>RowVersion</c>, nhưng màn IPQC giữ HAI
/// bản sao của nó: <c>_view.ETag</c> (hạng mục + phán định) và
/// <c>_material.ETag</c> (vật tư). Cả hai đều là
/// <c>Convert.ToBase64String(wo.RowVersion)</c>, chỉ khác chỗ cất.
///
/// <para>Ghi qua đường VẬT TƯ chỉ nạp lại <c>_material</c> ⇒ <c>_view.ETag</c>
/// lạc hậu trong im lặng ⇒ bấm phán định là 409 <c>wo.state_conflict</c>, với
/// câu "Another operation has already updated this WO" — trong khi "operation
/// khác" ấy chính là người vừa bấm.</para>
///
/// <para>Đo được trên live: một dòng <c>WO_STATE_CONFLICT</c> lúc 04:40:55
/// ngay sau bốn dòng <c>WO_IPQC_MATERIAL_CHECK</c> lúc 04:40:46–49.</para>
/// </summary>
public sealed class IpqcDashboardEtagSyncTests : TestContext
{
    private const string Old = "v1";
    private const string Fresh = "v2";

    public IpqcDashboardEtagSyncTests()
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

    /// <summary>Hạng mục đã chấm xong hết ⇒ khối phán định hiện ra.</summary>
    private static IpqcView ReadyView(string etag) => new()
    {
        WoId = 59, WoNo = "WO-TEST-02", MesPhase = "IPQC_WAIT", ETag = etag,
        ResolvedLines = "LABEL",
        IsReadyForJudgment = true, AllOk = true, AnyNg = false,
        Items = new[]
        {
            new IpqcViewItem { ItemKey = "LBL-A1", ProcessLine = "LABEL", GroupLabel = "A·Ngoại quan",
                Label = "Bẩn / đốm / chấm", AcceptanceCriteria = "Không bẩn",
                Status = "Ok", DefectCode = "DIRT" },
        },
    };

    private static IpqcMaterialSystemView MaterialView(string etag, string rowStatus) => new()
    {
        WoId = 59, WoNo = "WO-TEST-02", MesPhase = "IPQC_WAIT", ETag = etag,
        AllResolved = true,
        Rows = new[]
        {
            new IpqcMaterialRow
            {
                BomLineIdx = 0, MaterialCode = "30032128",
                MaterialDescription = "OPAT-C0026", Status = rowStatus,
            },
        },
    };

    private static List<ReasonCodeOption> Scraps() => new()
    {
        new() { Code = "DIRT", LabelEn = "Dirt", LabelVi = "Bẩn", Kind = "Scrap", Sort = 1 },
    };

    [Fact]
    public void Ghi_VAT_TU_xong_thi_phan_dinh_phai_dung_ETag_MOI()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();

        // View hạng mục KHÔNG BAO GIỜ được nạp lại trên đường vật tư — nó vẫn
        // trả ETag cũ. Đó chính là điều kiện đã làm hỏng việc trên chuyền.
        api.IpqcViewImpl = (_, _) => Task.FromResult(ReadyView(Old));

        var written = false;
        api.IpqcMaterialSystemImpl = (_, _) =>
            Task.FromResult(MaterialView(written ? Fresh : Old, written ? "Ok" : "Pending"));

        // Server ghi xong, RowVersion nhảy, và trả ETag MỚI ngay trong phản hồi.
        api.PutIpqcMaterialSystemImpl = (_, _, _, _, _) =>
        {
            written = true;
            return Task.FromResult(new IpqcMaterialSetResponse
            {
                Ok = true, ETag = Fresh, MesPhase = "IPQC_WAIT", AllResolved = true,
            });
        };

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 59L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-material-row-0-ok']")));
        cut.Find("[data-testid='ipqc-material-row-0-ok']").Click();
        cut.WaitForAssertion(() => Assert.Single(api.PutIpqcMaterialSystemCalls));

        cut.Find("[data-testid='ipqc-judgment-gorun']").Click();

        cut.WaitForAssertion(() =>
        {
            var call = Assert.Single(api.PostIpqcJudgmentCalls);
            // ← đỏ nếu _view.ETag còn "v1": server trả 409 wo.state_conflict
            Assert.Equal(Fresh, call.ETag);
        });
    }

    [Fact]
    public void Vat_tu_bi_TU_CHOI_409_thi_ETag_moi_van_phai_duoc_nhan()
    {
        // 409 cũng mang ETag mới của server — chính là để client tự đồng bộ rồi
        // bấm lại được. Bỏ qua nó thì người đứng máy kẹt vĩnh viễn ở một màn
        // hình nói "có người khác vừa sửa", dù chẳng có ai khác.
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(ReadyView(Old));
        api.IpqcMaterialSystemImpl = (_, _) => Task.FromResult(MaterialView(Old, "Pending"));
        api.PutIpqcMaterialSystemImpl = (_, _, _, _, _) => Task.FromResult(new IpqcMaterialSetResponse
        {
            Ok = false, ErrorCode = "wo.state_conflict", ETag = Fresh, MesPhase = "IPQC_WAIT",
        });

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 59L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-material-row-0-ok']")));
        cut.Find("[data-testid='ipqc-material-row-0-ok']").Click();
        cut.WaitForAssertion(() => Assert.Single(api.PutIpqcMaterialSystemCalls));

        cut.Find("[data-testid='ipqc-judgment-gorun']").Click();

        cut.WaitForAssertion(() =>
        {
            var call = Assert.Single(api.PostIpqcJudgmentCalls);
            Assert.Equal(Fresh, call.ETag);
        });
    }
}

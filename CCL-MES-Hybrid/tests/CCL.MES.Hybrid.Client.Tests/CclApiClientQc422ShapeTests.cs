using System.Net;
using CCL.MES.Hybrid.Client.Tests._Support;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.IpqcReview;
using Microsoft.Extensions.Options;
using Xunit;

namespace CCL.MES.Hybrid.Client.Tests;

/// <summary>
/// L54 tái phát (2026-09-14, Thiệp báo từ chuyền) — hai HÌNH DẠNG thân khác
/// nhau trên cùng một endpoint.
///
/// <para><c>WoMutationControllerBase.Invalid()</c> trả <see cref="ApiError"/>
/// <c>{code,message}</c> cho 422, còn 200/409 trả envelope <c>{ok,errorCode}</c>.
/// Đọc thân 422 thành envelope thì <c>ErrorCode</c> ra <c>null</c>, và màn hình
/// hiện <i>"Máy chủ trả về Ok=false nhưng không có mã lỗi — báo IT"</i> cho MỌI
/// lỗi nghiệp vụ: sai mật khẩu ký, chưa xác nhận đủ hạng mục, sai pha.</para>
///
/// <para>Hậu quả đo được: người đứng máy gõ sai mật khẩu ký và được bảo đi
/// "báo IT" — trong khi server đã nói rõ lý do và đã ghi đúng dòng audit
/// <c>WO_IPQC_SIGN_DENIED</c>. Lỗi nằm TRỌN ở phía client.</para>
///
/// <para>L54 đã sửa đúng ca này cho SettingChecks từ trước, kèm chú thích
/// nguyên văn "not 'no error code — report to IT'" — nhưng không ai mang sang
/// ba đường còn lại (IPQC · vật tư IPQC · FQC/OQC). Test này khoá cả ba.</para>
/// </summary>
public sealed class CclApiClientQc422ShapeTests
{
    /// <summary>Thân 422 THẬT mà server phát ra — ApiError, KHÔNG phải envelope.</summary>
    private static HttpResponseMessage ApiError422(string code) =>
        StubHttpHandler.Json(HttpStatusCode.UnprocessableEntity,
            new { code, message = "không quan trọng với phép kiểm này" });

    [Theory]
    // Chữ ký điện tử — bốn lý do từ chối, mỗi lý do một câu tiếng Việt riêng.
    [InlineData("ipqc.signature_required")]
    [InlineData("ipqc.signature_invalid")]
    [InlineData("ipqc.signer_not_allowed")]
    [InlineData("ipqc.signature_locked")]
    // Các lỗi nghiệp vụ đã có từ trước — cũng đang hỏng y hệt.
    [InlineData("ipqc.not_ready_for_judgment")]
    [InlineData("wo.invalid_phase")]
    public async Task Judgment_422_phai_giu_duoc_MA_LOI_that(string code)
    {
        var (client, stub) = Build();
        stub.Responder = (_, _) => Task.FromResult(ApiError422(code));

        var resp = await client.PostIpqcJudgmentAsync(
            workOrderId: 42, ifMatchETag: "E=", new SubmitIpqcJudgmentRequest { Judgment = "GoRun" });

        Assert.False(resp.Ok);
        // ← đỏ nếu client đọc 422 thành envelope: ErrorCode null ⇒ "báo IT"
        Assert.Equal(code, resp.ErrorCode);
    }

    [Fact]
    public async Task Vat_tu_IPQC_422_phai_giu_duoc_ma_loi()
    {
        var (client, stub) = Build();
        stub.Responder = (_, _) => Task.FromResult(ApiError422("ipqc.waiver_reason_required"));

        var resp = await client.PostIpqcMaterialApproveDivergenceAsync(
            workOrderId: 42, ifMatchETag: "E=", bomLineIdx: 0,
            new ApproveDivergenceRequest { Outcome = "Approve", Reason = "" });

        Assert.False(resp.Ok);
        Assert.Equal("ipqc.waiver_reason_required", resp.ErrorCode);
    }

    [Fact]
    public async Task Than_422_la_rong_thi_van_KHONG_duoc_de_ma_loi_trong()
    {
        // Ca biên: thân lạ / rỗng. Bất biến cần khoá là "ErrorCode không bao
        // giờ rỗng" — rỗng mới là thứ đẩy màn hình về câu "báo IT". Giá trị
        // cụ thể do `ApiError` tự lấp ("error.unknown"), giống hệt đường
        // SettingChecks đã chạy từ L54; test không ép một chuỗi khác để tránh
        // tạo ra hai hành vi lệch nhau giữa các đường.
        var (client, stub) = Build();
        stub.Responder = (_, _) => Task.FromResult(
            StubHttpHandler.Json(HttpStatusCode.UnprocessableEntity, new { }));

        var resp = await client.PostIpqcJudgmentAsync(
            workOrderId: 42, ifMatchETag: "E=", new SubmitIpqcJudgmentRequest { Judgment = "GoRun" });

        Assert.False(resp.Ok);
        Assert.False(string.IsNullOrEmpty(resp.ErrorCode));
    }

    [Fact]
    public async Task Duong_200_va_409_VAN_doc_envelope_nhu_cu()
    {
        // Bản sửa không được làm hỏng hai đường đang đúng: 409 mang ETag mới
        // để client tự đồng bộ lại, 200 mang pha sau khi ghi.
        var (client, stub) = Build();
        stub.Responder = (_, _) => Task.FromResult(
            StubHttpHandler.Json(HttpStatusCode.Conflict, new IpqcSetResponse
            {
                Ok = false, ErrorCode = "wo.state_conflict", ETag = "FRESH=", MesPhase = "IPQC_WAIT",
            }));

        var resp = await client.PostIpqcJudgmentAsync(
            workOrderId: 42, ifMatchETag: "STALE=", new SubmitIpqcJudgmentRequest { Judgment = "GoRun" });

        Assert.Equal("wo.state_conflict", resp.ErrorCode);
        Assert.Equal("FRESH=", resp.ETag);      // ← đỏ nếu 409 bị cuốn vào nhánh 422
    }

    private static (CclApiClient client, StubHttpHandler stub) Build()
    {
        var stub = new StubHttpHandler();
        var http = new HttpClient(stub) { BaseAddress = new Uri("http://localhost:5100") };
        var opts = Options.Create(new ApiClientOptions { BaseUrl = "http://localhost:5100" });
        return (new CclApiClient(http, opts), stub);
    }
}
